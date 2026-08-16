using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace jshepler.ngu.mods
{
    // a basic training cap button can only allocate what's idle. when this is on, the shortfall is
    // pulled out of augmentation first - largest sink first - so the cap lands full instead of partial
    [HarmonyPatch]
    internal class CapPull
    {
        private static readonly Color OffColor = new Color32(150, 150, 150, 255);
        private static readonly Vector2 ButtonSize = new Vector2(130f, 24f);

        // bottom-right: the top-right corner of these menus is where the game parks its own furniture
        private static readonly Vector2 ButtonOffset = new Vector2(-8f, 8f);
        private static readonly Vector2 BottomRight = new Vector2(1f, 0f);

        internal static bool Enabled
        {
            get => Options.CapPull.Enabled.Value;
            set => Options.CapPull.Enabled.Value = value;
        }

        private static Image _image;
        private static Text _text;

        [HarmonyPrepare, HarmonyPatch]
        private static void prep(MethodBase original)
        {
            if (original != null)
                return;

            Plugin.OnGameStart += (o, e) =>
            {
                buildUI();
                Plugin.OnUpdate += (s, a) => updateVisual();
            };
        }

        private static void buildUI()
        {
            // MenuSwapper.swapIn/swapOut position allMenus[id] and its first child, so child 0 IS the menu panel
            var menu = Plugin.Character.menuSwapper.allMenus[(int)Menu.BasicTraining];
            if (menu == null || menu.transform.childCount == 0)
            {
                Plugin.LogInfo("CapPull: basic training menu not found, toggle not created");
                return;
            }

            var go = menu.transform.GetChild(0)
                .CreateModButton("Cap Pull Toggle", ButtonSize, ButtonOffset, BottomRight);

            _image = go.GetComponent<Image>();
            _text = go.GetComponentInChildren<Text>();

            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                Enabled = !Enabled;
                updateVisual();
            });

            updateVisual();
            Plugin.LogInfo($"CapPull: toggle added at {go.Path()}");
        }

        private static void updateVisual()
        {
            if (_image == null)
                return;

            _image.color = Enabled ? Plugin.ButtonColor_Green : OffColor;
            _text.text = Enabled ? "Cap Pull: ON" : "Cap Pull: OFF";
        }

        // void prefixes: they cannot skip the original and cannot override the existing
        // BasicTraining.cs prefixes' decision. Priority.First only puts the pull before them,
        // so the cap code - vanilla or modded - sees the freed energy as ordinary idle energy.
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(OffenseTraining), "cap", new Type[0])]
        private static void OffenseTraining_cap_prefix(OffenseTraining __instance)
        {
            pullForRow(__instance.id, isOffense: true);
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(DefenseTraining), "cap", new Type[0])]
        private static void DefenseTraining_cap_prefix(DefenseTraining __instance)
        {
            pullForRow(__instance.id, isOffense: false);
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(OffenseTraining), "addSum")]
        private static void OffenseTraining_addSum_prefix()
        {
            pullForSum(isOffense: true);
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(DefenseTraining), "addSum")]
        private static void DefenseTraining_addSum_prefix()
        {
            pullForSum(isOffense: false);
        }

        private static void pullForRow(int id, bool isOffense)
        {
            if (!Enabled)
                return;

            var character = Plugin.Character;
            var training = character.training;

            long target;
            long available;

            // with syncTraining on, BasicTraining.CapPair caps the attack AND defense row of this
            // id out of one idle pool, so the row's shortfall is the pair's shortfall
            if (character.settings.syncTraining)
            {
                target = (long)training.attackCaps[id] + training.defenseCaps[id];
                available = character.idleEnergy + training.attackEnergy[id] + training.defenseEnergy[id];
            }
            else
            {
                target = isOffense ? training.attackCaps[id] : training.defenseCaps[id];
                available = character.idleEnergy + (isOffense ? training.attackEnergy[id] : training.defenseEnergy[id]);
            }

            pull(target - available, $"{(isOffense ? "attack" : "defense")} {id + 1}");
        }

        private static void pullForSum(bool isOffense)
        {
            if (!Enabled)
                return;

            var character = Plugin.Character;
            var training = character.training;

            // mirrors BasicTraining.CalcAmountToAddOffense/Defense exactly, including its lack of
            // per-row clamping - the shortfall has to match what the cap-all code actually requests
            var offense = 0L;
            var defense = 0L;
            for (var i = 0; i < training.attackCaps.Length; i++)
            {
                offense += training.attackCaps[i] - training.attackEnergy[i];
                defense += training.defenseCaps[i] - training.defenseEnergy[i];
            }

            var needed = character.settings.syncTraining ? offense + defense : (isOffense ? offense : defense);

            pull(needed - character.idleEnergy, isOffense ? "attack (cap all)" : "defense (cap all)");
        }

        // takes exactly `needed` out of augmentation, largest sink first, via the game's own remove
        // buttons. returns what it actually got - less than needed means the cap will land partial,
        // which is what vanilla would have done with that much idle energy anyway.
        private static long pull(long needed, string label)
        {
            if (needed <= 0L)
                return 0L;

            var character = Plugin.Character;
            var controllers = character.augmentsController.augments;
            var input = character.input;
            var requested = input.energyMagicInput;

            var pulled = 0L;

            while (pulled < needed)
            {
                var most = 0L;
                var id = -1;
                var isUpgrade = false;

                for (var i = 0; i < controllers.Length; i++)
                {
                    var aug = character.augments.augs[i];

                    if (!controllers[i].augLocked() && aug.augEnergy > most)
                    {
                        most = aug.augEnergy;
                        id = i;
                        isUpgrade = false;
                    }

                    if (!controllers[i].upgradeLocked() && aug.upgradeEnergy > most)
                    {
                        most = aug.upgradeEnergy;
                        id = i;
                        isUpgrade = true;
                    }
                }

                if (id < 0)
                    break;

                var before = character.idleEnergy;

                input.energyMagicInput = Math.Min(most, needed - pulled);

                if (isUpgrade)
                    controllers[id].removeEnergyUpgrade();
                else
                    controllers[id].removeEnergyAug();

                var moved = character.idleEnergy - before;
                if (moved <= 0L) // a sink that won't give anything up would otherwise spin forever
                    break;

                pulled += moved;
            }

            input.energyMagicInput = requested;

            if (pulled > 0L)
                Plugin.LogInfo($"CapPull: {label} short {needed}, pulled {pulled} from augmentation");

            return pulled;
        }

        // the same call the row's cap button makes (OffenseClickCap/DefenseClickCap.OnPointerDown)
        internal static bool InvokeCap(int flatIndex)
        {
            var character = Plugin.Character;
            var offense = character.allOffenseController.trains;
            var defense = character.allDefenseController.trains;

            if (flatIndex < 0 || flatIndex >= offense.Length + defense.Length)
                return false;

            if (flatIndex < offense.Length)
                offense[flatIndex].cap();
            else
                defense[flatIndex - offense.Length].cap();

            return true;
        }
    }
}
