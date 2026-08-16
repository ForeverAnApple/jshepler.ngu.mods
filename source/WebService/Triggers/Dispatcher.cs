using System;
using System.Collections.Generic;
using System.Net;

namespace jshepler.ngu.mods.WebService.Triggers
{
    internal class Dispatcher
    {
        private static bool InManualFight => Plugin.Character.adventureController.fightInProgress && !Plugin.Character.adventure.autoattacking;

        internal static Action HandleRequest(HttpListenerContext context, string trigger)
        {
            if (!TriggerConfig.RemoteTriggersEnabled)
                return () => Plugin.ShowOverrideNotification("remote triggers disabled");

            switch (trigger)
            {
                case "autoboost":
                    return autoBoost();

                case "automerge":
                    return autoMerge();

                case "tossgold":
                    return tossGold();

                case "fightboss":
                    return fightBoss();

                case "kitty":
                    return kitty();

                case "save":
                    return save();

                case "autoboss":
                    return autoBossToggle();

                case "funnelenergy":
                    return funnelToggle(isEnergy: true);

                case "funnelmagic":
                    return funnelToggle(isEnergy: false);

                case "testfree":
                    return testMove(context, isSeed: false);

                case "testseed":
                    return testMove(context, isSeed: true);

                case "testap":
                    return testAp(context);

                default:
                    return () => Plugin.ShowOverrideNotification($"unknown trigger: {trigger}");
            }
        }

        internal static Action HandleTwitchReward(Twitch.Reward reward)
        {
            if (!TriggerConfig.RemoteTriggersEnabled)
                return () => Plugin.ShowOverrideNotification("remote triggers disabled");

            if (reward.title == Options.Twitch.RewardTriggers.Merge.Value)
                return autoMerge();

            if (reward.title == Options.Twitch.RewardTriggers.Boost.Value)
                return autoBoost();

            if (reward.title == Options.Twitch.RewardTriggers.MergeBoost.Value)
                return () =>
                {
                    autoMerge()();
                    autoBoost()();
                };

            if (reward.title == Options.Twitch.RewardTriggers.FightBoss.Value)
                return fightBoss();

            if (reward.title == Options.Twitch.RewardTriggers.TossGold.Value)
                return tossGold();

            if (reward.title == Options.Twitch.RewardTriggers.Kitty.Value)
                return kitty();

            return () => Plugin.ShowOverrideNotification($"TWITCH: no match found for \"{reward.title}\"");
        }

        internal static Action autoBoost()
        {
            if(!TriggerConfig.AutoBoostEnabled)
                return () => Plugin.ShowOverrideNotification("trigger: autoboost disabled");

            return () =>
            {
                Plugin.ShowOverrideNotification("trigger: autoboost");
                Plugin.Character.inventoryController.autoBoost();
            };
        }

        internal static Action autoMerge()
        {
            if(!TriggerConfig.AutoMergeEnabled)
                return () => Plugin.ShowOverrideNotification("trigger: automerge disabed");

            return () =>
            {
                Plugin.ShowOverrideNotification("trigger: automerge");
                Plugin.Character.inventoryController.autoMerge();
            };
        }

        internal static Action tossGold()
        {
            if (!TriggerConfig.TossGoldEnabled)
                return () => Plugin.ShowOverrideNotification("trigger: toss gold disabed");

            if (InManualFight)
                return () => Plugin.ShowOverrideNotification("trigger: toss gold ignored - manual adventure fight in progress");

            if (TossGold.IsRunning)
                return () => Plugin.ShowOverrideNotification("trigger: toss gold ignored - already running");

            if (!Plugin.Character.pitController.canToss())
                return () => Plugin.ShowOverrideNotification("trigger: toss gold ignored - pit not ready");

            return () =>
            {
                Plugin.ShowOverrideNotification("trigger: toss gold");
                Plugin.Character.StartCoroutine(TossGold.Run());
            };
        }

        internal static Action fightBoss()
        {
            if (!TriggerConfig.FightBossEnabled)
                return () => Plugin.ShowOverrideNotification("trigger: fight boss disabled");

            if (Hardcore.IsHardcoreGame)
                return () => Plugin.ShowOverrideNotification("trigger: fight boss disabled - HARDCORE MODE");

            if (InManualFight)
                return () => Plugin.ShowOverrideNotification("trigger: fight boss ignored - manual adventure fight in progress");

            if (FightBoss.IsRunning)
                return () => Plugin.ShowOverrideNotification("trigger: fight boss ignored - already running");

            if (Plugin.Character.bossController.isFighting)
                return () => Plugin.ShowOverrideNotification("trigger: fight boss ignored - fight in progress");

            if (!mods.FightBoss.CanFight)
                return () => Plugin.ShowOverrideNotification("trigger: fight boss ignored - boss not beatable");

            return () =>
            {
                Plugin.ShowOverrideNotification("trigger: fightboss");
                Plugin.Character.StartCoroutine(FightBoss.Run());
            };
        }

        internal static Action save()
        {
            if (!TriggerConfig.SaveEnabled)
                return () => Plugin.ShowOverrideNotification("trigger: save disabled");

            return () =>
            {
                Plugin.ShowOverrideNotification("trigger: save");

                // same dispatch the game's periodic autosave does in OpenFileDialog.Update()
                var character = Plugin.Character;
                switch (character.platform)
                {
                    case platform.Kartridge:
                        character.saveLoad.quicklySaveStandalone();
                        break;

                    case platform.Steam:
                        character.saveLoad.quicklySaveSteam();
                        character.saveLoad.saveGamestateToSteamCloud();
                        break;

                    default:
                        character.saveLoad.quicklySave();
                        break;
                }

                AutoSaves.DoSave("RemoteSave");
            };
        }

        internal static Action autoBossToggle()
        {
            if (!TriggerConfig.AutoBossEnabled)
                return () => Plugin.ShowOverrideNotification("trigger: autoboss disabled");

            return () =>
            {
                mods.AutoBoss.Enabled = !mods.AutoBoss.Enabled;
                Plugin.ShowOverrideNotification($"trigger: autoboss {(mods.AutoBoss.Enabled ? "ON" : "OFF")}");
            };
        }

        internal static Action funnelToggle(bool isEnergy)
        {
            var name = isEnergy ? "funnelenergy" : "funnelmagic";

            if (isEnergy ? !TriggerConfig.FunnelEnergyEnabled : !TriggerConfig.FunnelMagicEnabled)
                return () => Plugin.ShowOverrideNotification($"trigger: {name} disabled");

            return () =>
            {
                bool state;

                if (isEnergy)
                    state = mods.AutoFunnel.EnergyEnabled = !mods.AutoFunnel.EnergyEnabled;
                else
                    state = mods.AutoFunnel.MagicEnabled = !mods.AutoFunnel.MagicEnabled;

                Plugin.ShowOverrideNotification($"trigger: {name} {(state ? "ON" : "OFF")}");
            };
        }

        // grants an exact amount of AP (no gain bonuses applied, unlike Character.addAP)
        internal static Action testAp(HttpListenerContext context)
        {
            if (!TriggerConfig.TestEnabled)
                return () => Plugin.ShowOverrideNotification("trigger: testap disabled");

            var query = parseQuery(context);
            var amountValue = query.TryGetValue("amount", out var a) ? a : string.Empty;
            if (!long.TryParse(amountValue, out var amount) || amount <= 0L)
                return () => Plugin.ShowOverrideNotification("trigger: testap - amount must be a positive integer");

            return () =>
            {
                var arbitrary = Plugin.Character.arbitrary;
                var before = arbitrary.curArbitraryPoints;
                arbitrary.curArbitraryPoints += amount;
                arbitrary.curLifetimePoints += amount;
                Plugin.Character.allArbitrary.updateMenu();

                var message = $"trigger: testap - AP {before:N0} -> {arbitrary.curArbitraryPoints:N0}";
                Plugin.LogInfo(message);
                Plugin.ShowOverrideNotification(message);
            };
        }

        // testseed pushes idle energy/magic into a named sink, testfree pulls it back out;
        // together they let the funnel be driven end to end without touching the game's UI
        internal static Action testMove(HttpListenerContext context, bool isSeed)
        {
            var name = isSeed ? "testseed" : "testfree";

            if (!TriggerConfig.TestEnabled)
                return () => Plugin.ShowOverrideNotification($"trigger: {name} disabled");

            var query = parseQuery(context);

            var res = query.TryGetValue("res", out var resValue) ? resValue : string.Empty;
            if (res != "energy" && res != "magic")
                return () => Plugin.ShowOverrideNotification($"trigger: {name} - res must be 'energy' or 'magic'");

            if (!query.TryGetValue("sink", out var sinkValue) || !int.TryParse(sinkValue, out var sink) || sink < 0)
                return () => Plugin.ShowOverrideNotification($"trigger: {name} - sink must be a non-negative integer");

            var amountValue = query.TryGetValue("amount", out var a) ? a : "all";
            var takeAll = amountValue == "all";

            long amount = 0L;
            if (!takeAll && (!long.TryParse(amountValue, out amount) || amount <= 0L))
                return () => Plugin.ShowOverrideNotification($"trigger: {name} - amount must be a positive integer or 'all'");

            return () =>
            {
                try
                {
                    if (res == "energy")
                        moveEnergy(name, isSeed, sink, takeAll, amount);
                    else
                        moveMagic(name, isSeed, sink, takeAll, amount);
                }

                catch (Exception ex)
                {
                    Plugin.LogInfo($"trigger: {name} threw:\n{ex}");
                    Plugin.ShowOverrideNotification($"trigger: {name} failed - see log");
                }
            };
        }

        private static void moveEnergy(string name, bool isSeed, int sink, bool takeAll, long amount)
        {
            var character = Plugin.Character;
            var controllers = character.augmentsController.augments;

            // flat index: 0..6 augs, 7..13 their upgrades - same convention as the status endpoint
            var isUpgrade = sink >= controllers.Length;
            var id = isUpgrade ? sink - controllers.Length : sink;

            if (id >= controllers.Length)
            {
                Plugin.ShowOverrideNotification($"trigger: {name} - sink {sink} out of range (0-{controllers.Length * 2 - 1})");
                return;
            }

            var aug = character.augments.augs[id];
            var sinkName = $"{(isUpgrade ? "upgrade" : "aug")} {id + 1}";

            var before = isUpgrade ? aug.upgradeEnergy : aug.augEnergy;
            var request = takeAll ? (isSeed ? character.idleEnergy : before) : amount;

            if (request <= 0L)
            {
                Plugin.ShowOverrideNotification($"trigger: {name} - nothing to move for {sinkName}");
                return;
            }

            var input = character.input;
            var requested = input.energyMagicInput;
            input.energyMagicInput = request;

            if (isSeed)
            {
                if (isUpgrade) controllers[id].addEnergyUpgrade();
                else controllers[id].addEnergyAug();
            }
            else
            {
                if (isUpgrade) controllers[id].removeEnergyUpgrade();
                else controllers[id].removeEnergyAug();
            }

            input.energyMagicInput = requested;

            var after = isUpgrade ? aug.upgradeEnergy : aug.augEnergy;
            var message = $"trigger: {name} energy {sinkName} {before} -> {after}, idle {character.idleEnergy}";

            Plugin.ShowOverrideNotification(message);
            Plugin.LogInfo(message);
        }

        private static void moveMagic(string name, bool isSeed, int sink, bool takeAll, long amount)
        {
            var character = Plugin.Character;
            var controllers = character.bloodMagicController.bloodMagics;
            var unlocked = Math.Min(character.bloodMagicController.ritualsUnlocked(), Math.Min(controllers.Length, character.bloodMagic.ritual.Count));

            if (sink >= unlocked)
            {
                Plugin.ShowOverrideNotification($"trigger: {name} - sink {sink} out of range (0-{unlocked - 1})");
                return;
            }

            var ritual = character.bloodMagic.ritual[sink];
            var sinkName = $"ritual {sink + 1}";

            var before = ritual.magic;
            var request = takeAll ? (isSeed ? character.magic.idleMagic : before) : amount;

            if (request <= 0L)
            {
                Plugin.ShowOverrideNotification($"trigger: {name} - nothing to move for {sinkName}");
                return;
            }

            var input = character.input;
            var requested = input.energyMagicInput;
            input.energyMagicInput = request;

            if (isSeed)
                controllers[sink].add();
            else
                controllers[sink].removeMagic();

            input.energyMagicInput = requested;

            var message = $"trigger: {name} magic {sinkName} {before} -> {ritual.magic}, idle {character.magic.idleMagic}";

            Plugin.ShowOverrideNotification(message);
            Plugin.LogInfo(message);
        }

        // hand-rolled so malformed input degrades to a missing key instead of throwing on the listener thread
        private static Dictionary<string, string> parseQuery(HttpListenerContext context)
        {
            var query = new Dictionary<string, string>();
            var raw = context.Request.Url.Query;

            if (string.IsNullOrEmpty(raw))
                return query;

            foreach (var pair in raw.TrimStart('?').Split('&'))
            {
                if (pair.Length == 0)
                    continue;

                var parts = pair.Split(new[] { '=' }, 2);
                var key = Uri.UnescapeDataString(parts[0]).Trim().ToLowerInvariant();

                if (key.Length > 0)
                    query[key] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]).Trim().ToLowerInvariant() : string.Empty;
            }

            return query;
        }

        internal static Action kitty()
        {
            if (!TriggerConfig.KittyEnabled)
                return () => Plugin.ShowOverrideNotification("trigger: kitty ignored - disabled");

            if (Kitty.IsRunning)
                return () => Plugin.ShowOverrideNotification("trigger: kitty ignored - already running");

            return () =>
            {
                Plugin.ShowOverrideNotification("trigger: kitty");
                Plugin.Character.StartCoroutine(Kitty.Run());
            };
        }
    }
}
