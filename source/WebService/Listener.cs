using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;

namespace jshepler.ngu.mods.WebService
{
    internal static class ContentTypes
    {
        internal const string PlainText = "text/plain";
        internal const string JSON = "application/json";
        internal const string HTML = "text/html";
    }

    [HarmonyPatch]
    internal class Listener
    {
        // because the listener is not running in the UI thread, or even part of Unity's lifecycle (e.g. Update),
        // anything that needs to do something in the UI (e.g. showing notifications) is added to queue and executed in Update
        private static Queue<Action> _actions = new();

        [HarmonyPrepare, HarmonyPatch]
        private static void prep(MethodBase original)
        {
            if (original != null)
                return;

            Plugin.OnGameStart += (o, e) =>
            {
                // OnGameStart fires again when a save is loaded - a second listener on the same
                // prefix throws and must not be started (this killed the listener 2026-08-16)
                if (_started)
                    return;
                _started = true;

                if (HttpListener.IsSupported)
                    Task.Run(() => Supervise());
                else
                    Plugin.LogInfo("HttpListener NOT SUPPORTED!!!");
            };

            Plugin.OnUpdate += (o, e) =>
            {
                while (true)
                {
                    Action action;
                    lock (_actions)
                    {
                        if (_actions.Count == 0)
                            break;
                        action = _actions.Dequeue();
                    }

                    try { action(); }
                    catch (Exception ex) { Plugin.LogInfo($"Listener: queued action failed: {ex}"); }
                }
            };
        }

        private static bool _started = false;

        // the accept loop can die in ways no per-request handling can catch (e.g. GetContextAsync
        // throwing on a client that aborts mid-accept, which killed the listener 2026-08-16);
        // whatever happens, tear the listener down and start a fresh one
        private static async Task Supervise()
        {
            while (true)
            {
                try
                {
                    await RunListener();
                    Plugin.LogInfo("Listener: accept loop exited, restarting");
                }
                catch (Exception ex)
                {
                    Plugin.LogInfo($"Listener: accept loop died, restarting: {ex}");
                }

                await Task.Delay(3000);
            }
        }

        private static async Task RunListener()
        {
            using var listener = new HttpListener();
            listener.Prefixes.Add(Options.RemoteTriggers.UrlPrefix.Value);
            listener.Start();

            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception ex)
                {
                    Plugin.LogInfo($"Listener: accept failed: {ex.Message}");
                    if (!listener.IsListening)
                        throw;
                    continue;
                }

                // each request gets its own task: a slow or half-dead client can only wedge its
                // own response, never the accept loop (a blocked inline SendResponse starved the
                // whole listener 2026-08-16 - accepted connections queued forever with no reply)
                _ = Task.Run(() => HandleContext(context));
            }
        }

        // one bad request must never kill or block the transport
        private static void HandleContext(HttpListenerContext context)
        {
            try
            {
                // handle preflight requests
                if (context.Request.HttpMethod == "OPTIONS")
                {
                    context.Response.SendResponse(HttpStatusCode.OK);
                    return;
                }

                // [0] /
                // [1] ngu/
                var segments = context.Request.Url.Segments.Skip(2).Select(s => s.TrimEnd('/').ToLowerInvariant()).ToArray();
                if (segments.Length == 0)
                {
                    context.Response.SendResponse(HttpStatusCode.NotFound);
                    return;
                }

                Dispatch(context, segments);
            }
            catch (Exception ex)
            {
                Plugin.LogInfo($"Listener: request handling failed: {ex}");
                try { context.Response.SendResponse(HttpStatusCode.InternalServerError); } catch { }
            }
        }

        // queues the action for the unity thread and waits for it, so a trigger's response means "done", not "queued"
        // (the event is deliberately not disposed - on timeout the queued action still needs it)
        private static void RunOnMainThread(Action action)
        {
            var done = new ManualResetEventSlim(false);

            lock (_actions)
            {
                _actions.Enqueue(() =>
                {
                    try { action(); }
                    finally { done.Set(); }
                });
            }

            done.Wait(5000);
        }

        private static void Dispatch(HttpListenerContext context, string[] segments)
        {
            switch (segments[0])
            {
                case "totaltimeplayed":
                    TotalTimePlayed.HandleRequest(context);
                    break;

                case "trigger":
                    RunOnMainThread(Triggers.Dispatcher.HandleRequest(context, segments[1]));
                    context.Response.SendResponse(HttpStatusCode.OK);
                    break;

                case "ngu2go":
                    lock (_actions) { _actions.Enqueue(GO.NGU2GO.HandleRequest(context, segments[1])); }
                    break;

                case "go2ngu":
                    lock (_actions) { _actions.Enqueue(GO.GO2NGU.HandleRequest(context, segments[1])); }
                    break;

                case "data":
                    lock (_actions) { _actions.Enqueue(Data.HandleRequest(context, segments)); }
                    break;

                case "autoboost":
                case "automerge":
                case "tossgold":
                case "fightboss":
                case "kitty":
                    lock (_actions) { _actions.Enqueue(Triggers.Dispatcher.HandleRequest(context, segments[0])); }
                    context.Response.SendResponse(HttpStatusCode.OK);
                    break;

                case "twitch":
                    lock (_actions) { _actions.Enqueue(Twitch.API.HandleAuthRedirectRequest(context)); }
                    break;

                default:
                    context.Response.SendResponse(HttpStatusCode.BadRequest, $"unknown handler: {segments[0]}");
                    break;
            }
        }
    }

    internal static class HttpListenerResponseExtension
    {
        internal static void SendResponse(this HttpListenerResponse response, HttpStatusCode status, string responseString = "OK", string contentType = ContentTypes.PlainText)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "POST, GET");
            response.StatusCode = (int)status;
            response.ContentType = contentType;

            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
            response.Close();
        }
    }
}
