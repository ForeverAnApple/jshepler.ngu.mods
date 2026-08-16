using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace jshepler.ngu.mods
{
    [HarmonyPatch]
    internal class AutoBoss
    {
        private const float TICK_SECONDS = 0.5f;
        private static readonly Color OffColor = new Color32(150, 150, 150, 255);

        internal static bool Enabled
        {
            get => Options.AutoBoss.Enabled.Value;
            set => Options.AutoBoss.Enabled.Value = value;
        }

        internal static bool IsRunning = false;

        private static Image _image;
        private static Text _text;
        private static float _elapsed;

        [HarmonyPostfix, HarmonyPatch(typeof(ButtonShower), "Start")]
        private static void ButtonShower_Start_postfix(ButtonShower __instance)
        {
            if (_image != null)
                return;

            // the Stop Button is a direct child of the boss menu panel (see FightBoss.ButtonShower_Start_postfix)
            var parent = __instance.character.bossController.stopButton.transform.parent;
            var go = parent.CreateModButton("Auto Boss Toggle", new Vector2(110f, 24f), new Vector2(-8f, -8f), new Vector2(1f, 1f));

            _image = go.GetComponent<Image>();
            _text = go.GetComponentInChildren<Text>();

            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                Enabled = !Enabled;
                updateVisual();
            });

            updateVisual();
            Plugin.LogInfo($"AutoBoss: toggle added at {go.Path()}");

            Plugin.OnUpdate += OnUpdate;
        }

        private static void OnUpdate(object sender, EventArgs e)
        {
            _elapsed += Time.deltaTime;
            if (_elapsed < TICK_SECONDS)
                return;

            _elapsed = 0f;

            // the config can also change from outside the toggle (config file reload, other mods)
            updateVisual();

            if (!Enabled || IsRunning || Hardcore.IsHardcoreGame)
                return;

            var bossController = Plugin.Character.bossController;
            if (bossController.nukeBoss || bossController.isFighting)
                return;

            if (!FightBoss.CanNuke && !FightBoss.CanFight)
                return;

            Plugin.Character.StartCoroutine(run());
        }

        // FightBoss.RunFight() nukes first, so nuking is already prioritized
        private static IEnumerator run()
        {
            IsRunning = true;

            yield return FightBoss.RunFight();

            IsRunning = false;
        }

        private static void updateVisual()
        {
            if (_image == null)
                return;

            var enabled = Enabled;
            _image.color = enabled ? Plugin.ButtonColor_Green : OffColor;
            _text.text = enabled ? "Auto: ON" : "Auto: OFF";
        }
    }
}
