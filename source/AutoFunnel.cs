using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace jshepler.ngu.mods
{
    // keeps newly generated energy/magic out of the idle pool by repeatedly dumping it
    // into whichever sink already holds the most of it
    [HarmonyPatch]
    internal class AutoFunnel
    {
        private const float TICK_SECONDS = 0.5f;
        private static readonly Color OffColor = new Color32(150, 150, 150, 255);
        private static readonly Vector2 ButtonSize = new Vector2(120f, 24f);
        private static readonly Vector2 TopRight = new Vector2(1f, 1f);

        // both toggles hang off the top-right corner of their menu's header, but each screen's
        // corner is occupied differently, so the two offsets are tuned separately
        private static readonly Vector2 EnergyButtonOffset = new Vector2(-8f, -8f);

        // clears the "Cap All Rituals" + "WTF do I do?" pair that ends flush at the header's right edge
        private static readonly Vector2 MagicButtonOffset = new Vector2(-8f, -56f);

        internal static bool EnergyEnabled
        {
            get => Options.AutoFunnel.EnergyEnabled.Value;
            set => Options.AutoFunnel.EnergyEnabled.Value = value;
        }

        internal static bool MagicEnabled
        {
            get => Options.AutoFunnel.MagicEnabled.Value;
            set => Options.AutoFunnel.MagicEnabled.Value = value;
        }

        // -1 = nothing assigned anywhere; for energy, upgrades are indexed as (7 + augId)
        internal static int EnergyTarget = -1;
        internal static long EnergyTargetAmount = 0L;
        internal static int MagicTarget = -1;
        internal static long MagicTargetAmount = 0L;

        private static Image _energyImage;
        private static Text _energyText;
        private static Image _magicImage;
        private static Text _magicText;

        private static float _elapsed;
        private static string _lastEnergyTargetName;
        private static string _lastMagicTargetName;

        [HarmonyPrepare, HarmonyPatch]
        private static void prep(MethodBase original)
        {
            if (original != null)
                return;

            Plugin.OnGameStart += (o, e) =>
            {
                buildUI();
                Plugin.OnUpdate += OnUpdate;
            };
        }

        private static void buildUI()
        {
            var character = Plugin.Character;

            var augGO = character.augmentsController.totalPowerText.transform.parent
                .CreateModButton("Auto Funnel Toggle", ButtonSize, EnergyButtonOffset, TopRight);

            _energyImage = augGO.GetComponent<Image>();
            _energyText = augGO.GetComponentInChildren<Text>();
            augGO.GetComponent<Button>().onClick.AddListener(() =>
            {
                EnergyEnabled = !EnergyEnabled;
                updateVisuals();
            });

            var bmGO = character.bloodMagicController.bloodText.transform.parent
                .CreateModButton("Auto Funnel Toggle", ButtonSize, MagicButtonOffset, TopRight);

            _magicImage = bmGO.GetComponent<Image>();
            _magicText = bmGO.GetComponentInChildren<Text>();
            bmGO.GetComponent<Button>().onClick.AddListener(() =>
            {
                MagicEnabled = !MagicEnabled;
                updateVisuals();
            });

            updateVisuals();
            Plugin.LogInfo($"AutoFunnel: energy toggle at {augGO.Path()}, magic toggle at {bmGO.Path()}");
        }

        private static void OnUpdate(object sender, EventArgs e)
        {
            _elapsed += Time.deltaTime;
            if (_elapsed < TICK_SECONDS)
                return;

            _elapsed = 0f;

            updateVisuals();
            findEnergyTarget();
            findMagicTarget();

            if (EnergyEnabled)
                funnelEnergy();

            if (MagicEnabled)
                funnelMagic();
        }

        private static void findEnergyTarget()
        {
            var character = Plugin.Character;
            var controllers = character.augmentsController.augments;

            EnergyTarget = -1;
            EnergyTargetAmount = 0L;

            for (var i = 0; i < controllers.Length; i++)
            {
                var aug = character.augments.augs[i];

                if (!controllers[i].augLocked() && aug.augEnergy > EnergyTargetAmount)
                {
                    EnergyTargetAmount = aug.augEnergy;
                    EnergyTarget = i;
                }

                if (!controllers[i].upgradeLocked() && aug.upgradeEnergy > EnergyTargetAmount)
                {
                    EnergyTargetAmount = aug.upgradeEnergy;
                    EnergyTarget = controllers.Length + i;
                }
            }
        }

        private static void findMagicTarget()
        {
            var character = Plugin.Character;
            var controllers = character.bloodMagicController.bloodMagics;
            var unlocked = Math.Min(character.bloodMagicController.ritualsUnlocked(), Math.Min(controllers.Length, character.bloodMagic.ritual.Count));

            MagicTarget = -1;
            MagicTargetAmount = 0L;

            for (var i = 0; i < unlocked; i++)
            {
                var magic = character.bloodMagic.ritual[i].magic;
                if (magic > MagicTargetAmount)
                {
                    MagicTargetAmount = magic;
                    MagicTarget = i;
                }
            }
        }

        private static void funnelEnergy()
        {
            var character = Plugin.Character;
            if (EnergyTarget < 0 || character.idleEnergy <= 0L)
                return;

            var controllers = character.augmentsController.augments;
            var isUpgrade = EnergyTarget >= controllers.Length;
            var id = isUpgrade ? EnergyTarget - controllers.Length : EnergyTarget;

            var name = $"{(isUpgrade ? "upgrade" : "aug")} {id + 1}";
            if (name != _lastEnergyTargetName)
            {
                _lastEnergyTargetName = name;
                Plugin.LogInfo($"AutoFunnel: energy target is now {name}");
            }

            // the game's add buttons read the amount from the shared energy/magic input panel
            var input = character.input;
            var requested = input.energyMagicInput;
            input.energyMagicInput = character.idleEnergy;

            if (isUpgrade)
                controllers[id].addEnergyUpgrade();
            else
                controllers[id].addEnergyAug();

            input.energyMagicInput = requested;
        }

        private static void funnelMagic()
        {
            var character = Plugin.Character;
            if (MagicTarget < 0 || character.magic.idleMagic <= 0L)
                return;

            var name = $"ritual {MagicTarget + 1}";
            if (name != _lastMagicTargetName)
            {
                _lastMagicTargetName = name;
                Plugin.LogInfo($"AutoFunnel: magic target is now {name}");
            }

            var input = character.input;
            var requested = input.energyMagicInput;
            input.energyMagicInput = character.magic.idleMagic;

            character.bloodMagicController.bloodMagics[MagicTarget].add();

            input.energyMagicInput = requested;
        }

        private static void updateVisuals()
        {
            if (_energyImage != null)
            {
                _energyImage.color = EnergyEnabled ? Plugin.ButtonColor_Green : OffColor;
                _energyText.text = EnergyEnabled ? "Funnel: ON" : "Funnel: OFF";
            }

            if (_magicImage != null)
            {
                _magicImage.color = MagicEnabled ? Plugin.ButtonColor_Green : OffColor;
                _magicText.text = MagicEnabled ? "Funnel: ON" : "Funnel: OFF";
            }
        }
    }
}
