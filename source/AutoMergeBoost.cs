using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace jshepler.ngu.mods
{
    // periodic merge + boost built on the game's own routines. merging is vanilla autoMerge()
    // outright; boosting is vanilla autoBoost() with the infinity cube step dropped and the
    // targets reordered by how much boost capacity they can still absorb.
    [HarmonyPatch]
    internal class AutoMergeBoost
    {
        private static readonly Color OffColor = new Color32(150, 150, 150, 255);
        private static readonly Vector2 ButtonSize = new Vector2(150f, 24f);
        private static readonly Vector2 ButtonOffset = new Vector2(-8f, 8f);
        private static readonly Vector2 BottomRight = new Vector2(1f, 0f);

        // one iteration per distinct id in the bag; the processed set already bounds the sweep,
        // this is just a hard stop in case the inventory grows a pathological shape
        private const int MaxSweepGroups = 500;

        internal static bool Enabled
        {
            get => Options.AutoMergeBoost.Enabled.Value;
            set => Options.AutoMergeBoost.Enabled.Value = value;
        }

        internal static int IntervalSeconds => Mathf.Max(10, Options.AutoMergeBoost.IntervalSeconds.Value);

        internal static int Runs = 0;
        internal static string LastRunSummary = "never run";

        private static Image _image;
        private static Text _text;
        private static float _elapsed;

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
            // MenuSwapper.swapIn/swapOut position allMenus[id] and its first child, so child 0 IS the menu panel
            var menu = Plugin.Character.menuSwapper.allMenus[(int)Menu.Inventory];
            if (menu == null || menu.transform.childCount == 0)
            {
                Plugin.LogInfo("AutoMergeBoost: inventory menu not found, toggle not created");
                return;
            }

            var go = menu.transform.GetChild(0)
                .CreateModButton("Auto Merge Boost Toggle", ButtonSize, ButtonOffset, BottomRight);

            _image = go.GetComponent<Image>();
            _text = go.GetComponentInChildren<Text>();

            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                Enabled = !Enabled;
                updateVisual();
            });

            updateVisual();
            Plugin.LogInfo($"AutoMergeBoost: toggle added at {go.Path()}");
        }

        private static void updateVisual()
        {
            if (_image == null)
                return;

            _image.color = Enabled ? Plugin.ButtonColor_Green : OffColor;
            _text.text = Enabled ? "Merge+Boost: ON" : "Merge+Boost: OFF";
        }

        private static void OnUpdate(object sender, EventArgs e)
        {
            updateVisual();

            if (!Enabled)
            {
                _elapsed = 0f;
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed < IntervalSeconds)
                return;

            _elapsed = 0f;
            RunPass("timer");
        }

        internal static void RunPass(string reason)
        {
            var character = Plugin.Character;
            var controller = character.inventoryController;

            // the game refuses its own auto merge/boost mid-drag; an item is in hand and slot
            // indices are in flux, so this is the one moment a pass could genuinely lose an item
            if (controller.midDrag)
            {
                LastRunSummary = $"{reason}: skipped, drag in progress";
                Plugin.LogInfo($"AutoMergeBoost: {LastRunSummary}");
                return;
            }

            var before = snapshot();

            var invWide = 0;

            try
            {
                controller.autoMerge();
                invWide = mergeInventoryWide();
                boostAllExceptCube();
            }

            catch (Exception ex)
            {
                Runs++;
                LastRunSummary = $"{reason}: ABORTED - {ex.GetType().Name}: {ex.Message}";
                Plugin.LogInfo($"AutoMergeBoost: pass aborted, inventory may be partly processed\n{ex}");
                return;
            }

            Runs++;
            LastRunSummary = $"{reason}: {diff(before)} invWide={invWide}";
            Plugin.LogInfo($"AutoMergeBoost: {LastRunSummary}");
        }

        // vanilla autoMerge() only aims mergeAll() at the equipped slots, the accessories, the
        // macguffins and the first totalInvMergeSlots() inventory slots - duplicates sitting deeper
        // in the bag never merge. this sweeps the whole bag with the same primitive.
        private static int mergeInventoryWide()
        {
            var character = Plugin.Character;
            var controller = character.inventoryController;

            var processed = new HashSet<int>();
            var merged = 0;

            // each mergeAll() drains one id group, so the loop is bounded by the distinct ids present
            for (var guard = 0; guard < MaxSweepGroups; guard++)
            {
                var receiver = nextGroupReceiver(processed);
                if (receiver < 0)
                    break;

                var id = character.inventory.inventory[receiver].id;
                processed.Add(id);

                var before = countCopies(id);
                controller.mergeAll(receiver);
                merged += Math.Max(0, before - countCopies(id));
            }

            return merged;
        }

        // recomputed from live inventory on every call: a merge that trips checkItemTransform
        // replaces the receiver with a different item, so any plan built up front goes stale
        private static int nextGroupReceiver(HashSet<int> processed)
        {
            var character = Plugin.Character;
            var inventory = character.inventory.inventory;
            var spaces = Math.Min(character.inventoryController.curSpaces(), inventory.Count);

            for (var i = 0; i < spaces; i++)
            {
                var item = inventory[i];

                if (item.id == 0 || processed.Contains(item.id))
                    continue;

                // boosts stay with vanilla: mergeAll would merge them under its own maxxed/level
                // rules, but boost levelling is the auto-boost path's business, not this sweep's.
                // macguffins are already pulled into the equipped macguffin slots by autoMerge().
                if (item.isBoost() || item.isMacGuffin())
                    continue;

                var receiver = -1;
                var bestLevel = -1;
                var copies = 0;

                for (var j = 0; j < spaces; j++)
                {
                    if (inventory[j].id != item.id)
                        continue;

                    copies++;

                    // highest level wins; ties go to the lowest slot index for determinism
                    if (inventory[j].level > bestLevel)
                    {
                        bestLevel = inventory[j].level;
                        receiver = j;
                    }
                }

                if (copies >= 2 && receiver >= 0)
                    return receiver;

                processed.Add(item.id);
            }

            return -1;
        }

        private static int countCopies(int id)
        {
            var character = Plugin.Character;
            var inventory = character.inventory.inventory;
            var spaces = Math.Min(character.inventoryController.curSpaces(), inventory.Count);
            var count = 0;

            for (var i = 0; i < spaces; i++)
                if (inventory[i].id == id)
                    count++;

            return count;
        }

        // vanilla InventoryController.autoBoost() with two changes:
        //   1. no infinityCubeAll() - the user wants the cube left out of everything
        //   2. targets sorted by remaining boost capacity instead of fixed slot order
        // every mutation still goes through the game's own applyAllBoosts(int)
        private static void boostAllExceptCube()
        {
            var character = Plugin.Character;
            var controller = character.inventoryController;

            var equipped = new List<int> { -1, -2, -3, -4, -5 };

            if (controller.weapon2Unlocked())
                equipped.Add(-6);

            for (var i = 10000; controller.accessoryID(i) < controller.accessorySpaces(); i++)
                equipped.Add(i);

            foreach (var slot in byCapacityDescending(equipped))
                controller.applyAllBoosts(slot);

            // the inventory-side merge slots, gated on the player's own setting exactly as vanilla does
            if (controller.totalInvMergeSlots() > 0 && character.settings.invAutoBoostOn)
            {
                var slots = new List<int>();
                for (var i = 0; i < controller.totalInvMergeSlots(); i++)
                    if (i < character.inventory.inventory.Count && character.inventory.inventory[i].isEquipment())
                        slots.Add(i);

                foreach (var slot in byCapacityDescending(slots))
                    controller.applyAllBoosts(slot);
            }

            controller.updateInventory();
        }

        // "highest available stats down": most unfilled boostable capacity first, and anything
        // already full is skipped outright
        private static List<int> byCapacityDescending(List<int> slots)
        {
            var ordered = new List<int>();

            foreach (var slot in slots)
                if (capacityRemaining(Plugin.Character.inventory.GetItem(slot)) > 0f)
                    ordered.Add(slot);

            ordered.Sort((a, b) => capacityRemaining(Plugin.Character.inventory.GetItem(b))
                .CompareTo(capacityRemaining(Plugin.Character.inventory.GetItem(a))));

            return ordered;
        }

        // mirrors the headroom Equipment.boostEquip() fills: floor(cap * (1 + level/100)) - cur
        private static float capacityRemaining(Equipment e)
        {
            if (e == null || e.id == 0 || e.isBoost())
                return 0f;

            var mult = 1f + (float)e.level / 100f;
            var room = 0f;

            room += Mathf.Max(0f, Mathf.Floor(e.capAttack * mult) - e.curAttack);
            room += Mathf.Max(0f, Mathf.Floor(e.capDefense * mult) - e.curDefense);
            room += Mathf.Max(0f, Mathf.Floor(e.spec1Cap * mult) - e.spec1Cur);
            room += Mathf.Max(0f, Mathf.Floor(e.spec2Cap * mult) - e.spec2Cur);
            room += Mathf.Max(0f, Mathf.Floor(e.spec3Cap * mult) - e.spec3Cur);

            return room;
        }

        private class Snapshot
        {
            internal int[] Ids;
            internal bool[] Boosts;
        }

        private static Snapshot snapshot()
        {
            var inventory = Plugin.Character.inventory.inventory;
            var shot = new Snapshot
            {
                Ids = new int[inventory.Count],
                Boosts = new bool[inventory.Count]
            };

            for (var i = 0; i < inventory.Count; i++)
            {
                shot.Ids[i] = inventory[i].id;
                shot.Boosts[i] = inventory[i].isBoost();
            }

            return shot;
        }

        // a merged item leaves its inventory slot empty; a consumed boost leaves the slot empty or,
        // when the recycle bonus fires, holding the next tier down. classification comes from what
        // the slot held BEFORE the pass, so a degraded boost can't be miscounted as a merge.
        private static string diff(Snapshot before)
        {
            var inventory = Plugin.Character.inventory.inventory;
            var merged = new List<int>();
            var boosts = 0;

            for (var i = 0; i < before.Ids.Length && i < inventory.Count; i++)
            {
                var was = before.Ids[i];
                if (was == 0 || was == inventory[i].id)
                    continue;

                if (before.Boosts[i])
                    boosts++;
                else
                    merged.Add(was);
            }

            var ids = merged.Count == 0 ? "none" : string.Join(",", merged.ConvertAll(i => i.ToString()).ToArray());

            return $"merged={merged.Count} (ids: {ids}) boostsConsumed={boosts}";
        }
    }
}
