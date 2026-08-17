using System;
using System.Linq;
using System.Net;
using SimpleJSON;

namespace jshepler.ngu.mods.WebService.GO
{
    internal class NGU2GO
    {
        internal static Action HandleRequest(HttpListenerContext context, string resource)
        {
            string json;

            switch (resource)
            {
                case "augstats":
                    json = BuildAugStats();
                    context.Response.SendResponse(HttpStatusCode.OK, json, ContentTypes.JSON);
                    return () => Plugin.ShowOverrideNotification("NGU2GO: aug stats");

                case "ngustats":
                    json = BuildNGUStats();
                    context.Response.SendResponse(HttpStatusCode.OK, json, ContentTypes.JSON);
                    return () => Plugin.ShowOverrideNotification("NGU2GO: ngu stats");

                case "nakedemr":
                    json = NakedEMR3.BuildNakedEMR3();
                    context.Response.SendResponse(HttpStatusCode.OK, json, ContentTypes.JSON);
                    return () => Plugin.ShowOverrideNotification("NGU2GO: naked EMR3");

                case "hacks":
                    json = Hacks.BuildHackStats();
                    context.Response.SendResponse(HttpStatusCode.OK, json, ContentTypes.JSON);
                    return () => Plugin.ShowOverrideNotification("NGU2GO: hacks");

                // no notification - the local GO autosync polls this every 10s, a toast would be permanent
                case "equipped":
                    json = Loadouts.BuildCurrentEquipJson();
                    context.Response.SendResponse(HttpStatusCode.OK, json, ContentTypes.JSON);
                    return () => { };

                // read-only snapshot for external verification - no notification, it gets polled
                case "status":
                    json = BuildStatus();
                    context.Response.SendResponse(HttpStatusCode.OK, json, ContentTypes.JSON);
                    return () => { };

                // read-only item dump (id + level per container) - no notification
                case "inventory":
                    json = BuildInventory();
                    context.Response.SendResponse(HttpStatusCode.OK, json, ContentTypes.JSON);
                    return () => { };

                case "wishstats":
                    json = BuildWishStats();
                    context.Response.SendResponse(HttpStatusCode.OK, json, ContentTypes.JSON);
                    return () => Plugin.ShowOverrideNotification("NGU2GO: wish stats");

                default:
                    context.Response.SendResponse(HttpStatusCode.BadRequest, $"unknown resource: {resource}");
                    return () => Plugin.ShowOverrideNotification($"NGU2GO: unknown resource: {resource}");
            }
        }

        private static string BuildAugStats()
        {
            var root = new JSONObject();
            root.Add("augspeed", Plugin.Character.augmentsController.getTotalSpeedFactor());
            root.Add("ecap", Plugin.Character.totalCapEnergy());
            root.Add("gps", Plugin.Character.goldPerSecond());
            root.Add("lsc", Plugin.Character.challenges.laserSwordChallenge.curCompletions);
            root.Add("nac", Plugin.Character.challenges.noAugsChallenge.curCompletions);
            root.Add("version", (int)Plugin.Character.settings.rebirthDifficulty);

            return root.ToString();
        }

        private static string BuildNGUStats()
        {
            var character = Plugin.Character;

            var en = character.NGU.skills.Select(s => s.level).ToArray();
            var ee = character.NGU.skills.Select(s => s.evilLevel).ToArray();
            var es = character.NGU.skills.Select(s => s.sadisticLevel).ToArray();

            var eNGUs = new JSONArray();
            for (var x = 0; x < 9; x++)
            {
                var o = new JSONObject();
                o.Add("normal", en[x]);
                o.Add("evil", ee[x]);
                o.Add("sadistic", es[x]);
                eNGUs.Add(o);
            }

            var energy = new JSONObject();
            energy.Add("ngus", eNGUs);
            energy.Add("cap", Plugin.Character.totalCapEnergy());
            energy.Add("nguspeed", character.totalNGUSpeedBonus() * character.totalEnergyPower() * character.NGUController.energyNGUBonus() * character.allDiggers.totalEnergyNGUBonus() * character.adventureController.itopod.totalEnergyNGUBonus() * character.inventory.macguffinBonuses[4] * character.hacksController.totalEnergyNGUBonus() * character.beastQuestPerkController.totalEnergyNGUSpeed() * character.allChallenges.trollChallenge.totalEnergyNGUBonus() * character.wishesController.totalEnergyNGUSpeed() * character.cardsController.getBonus(cardBonus.energyNGUSpeed));


            var mn = character.NGU.magicSkills.Select(s => s.level).ToArray();
            var me = character.NGU.magicSkills.Select(s => s.evilLevel).ToArray();
            var ms = character.NGU.magicSkills.Select(s => s.sadisticLevel).ToArray();

            var mNGUs = new JSONArray();
            for (var x = 0; x < 7; x++)
            {
                var o = new JSONObject();
                o.Add("normal", mn[x]);
                o.Add("evil", me[x]);
                o.Add("sadistic", ms[x]);
                mNGUs.Add(o);
            }

            var magic = new JSONObject();
            magic.Add("ngus", mNGUs);
            magic.Add("cap", Plugin.Character.totalCapMagic());
            magic.Add("nguspeed", character.totalNGUSpeedBonus() * character.totalMagicPower() * character.NGUController.magicNGUBonus() * character.allDiggers.totalMagicNGUBonus() * character.adventureController.itopod.totalMagicNGUBonus() * character.allChallenges.trollChallenge.totalMagicNGUBonus() * character.inventory.macguffinBonuses[5] * character.hacksController.totalMagicNGUBonus() * character.beastQuestPerkController.totalMagicNGUSpeed() * character.wishesController.totalMagicNGUSpeed() * character.cardsController.getBonus(cardBonus.magicNGUSpeed));

            var quirks = new JSONObject();
            quirks.Add("e2n", (character.beastQuest.quirkLevel[14] == 1));
            quirks.Add("s2e", (character.beastQuest.quirkLevel[89] == 1));

            var root = new JSONObject();
            root.Add("energy", energy);
            root.Add("magic", magic);
            root.Add("quirk", quirks);
            root.Add("blueHeart", character.inventory.itemList.itemMaxxed[(int)GameData.Items.Heart_Blue]);

            return root.ToString();
        }

        private static string BuildInventory()
        {
            var inv = Plugin.Character.inventory;
            var items = new JSONArray();

            // slot is the game's own id space (-1..-6 equipped, 10000+ accessories, 0+ inventory,
            // 100000+ daycare) so an emptied slot shows up as a missing ordinal in an external diff
            void add(Equipment e, string where, int slot)
            {
                if (e == null || e.id == 0)
                    return;

                var o = new JSONObject();
                o.Add("slot", slot);
                o.Add("id", e.id);
                o.Add("level", e.level);
                o.Add("where", where);
                o.Add("type", e.type.ToString());
                o.Add("removable", e.removable);
                o.Add("isBoost", e.isBoost());
                o.Add("curAttack", e.curAttack);
                o.Add("capAttack", e.capAttack);
                o.Add("curDefense", e.curDefense);
                o.Add("capDefense", e.capDefense);
                o.Add("spec1Cur", e.spec1Cur);
                o.Add("spec1Cap", e.spec1Cap);
                o.Add("spec2Cur", e.spec2Cur);
                o.Add("spec2Cap", e.spec2Cap);
                o.Add("spec3Cur", e.spec3Cur);
                o.Add("spec3Cap", e.spec3Cap);
                items.Add(o);
            }

            add(inv.head, "head", -1);
            add(inv.chest, "chest", -2);
            add(inv.legs, "legs", -3);
            add(inv.boots, "boots", -4);
            add(inv.weapon, "weapon", -5);
            add(inv.weapon2, "weapon2", -6);
            for (var i = 0; i < inv.accs.Count; i++)
                add(inv.accs[i], "accessory", 10000 + i);
            for (var i = 0; i < inv.inventory.Count; i++)
                add(inv.inventory[i], "inventory", i);
            for (var i = 0; i < inv.daycare.Count; i++)
                add(inv.daycare[i], "daycare", 100000 + i);

            var root = new JSONObject();
            root.Add("items", items);
            return root.ToString();
        }

        private static string BuildStatus()
        {
            var character = Plugin.Character;

            var boss = new JSONObject();
            boss.Add("bossID", character.bossID);
            boss.Add("canNuke", FightBoss.CanNuke);
            boss.Add("canFight", FightBoss.CanFight);
            boss.Add("nuking", character.bossController.nukeBoss);
            boss.Add("fighting", character.bossController.isFighting);
            boss.Add("autoBossEnabled", AutoBoss.Enabled);
            boss.Add("autoBossRunning", AutoBoss.IsRunning);
            boss.Add("hardcore", Hardcore.IsHardcoreGame);

            var augAssignments = new JSONArray();
            var augControllers = character.augmentsController.augments;
            for (var i = 0; i < augControllers.Length; i++)
            {
                var aug = character.augments.augs[i];

                if (!augControllers[i].augLocked())
                {
                    var o = new JSONObject();
                    o.Add("index", i);
                    o.Add("kind", "aug");
                    o.Add("id", i);
                    o.Add("energy", aug.augEnergy);
                    augAssignments.Add(o);
                }

                if (!augControllers[i].upgradeLocked())
                {
                    var o = new JSONObject();
                    o.Add("index", augControllers.Length + i);
                    o.Add("kind", "upgrade");
                    o.Add("id", i);
                    o.Add("energy", aug.upgradeEnergy);
                    augAssignments.Add(o);
                }
            }

            var ritualAssignments = new JSONArray();
            var bmControllers = character.bloodMagicController.bloodMagics;
            var unlocked = Math.Min(character.bloodMagicController.ritualsUnlocked(), Math.Min(bmControllers.Length, character.bloodMagic.ritual.Count));
            for (var i = 0; i < unlocked; i++)
            {
                var o = new JSONObject();
                o.Add("index", i);
                o.Add("magic", character.bloodMagic.ritual[i].magic);
                ritualAssignments.Add(o);
            }

            // flat index: 0..5 attack rows, 6..11 defense rows - the index trigger/testcap takes
            var trainings = new JSONArray();
            var offense = character.allOffenseController.trains;
            var defense = character.allDefenseController.trains;
            for (var i = 0; i < offense.Length + defense.Length; i++)
            {
                var isOffense = i < offense.Length;
                var row = isOffense ? i : i - offense.Length;

                var o = new JSONObject();
                o.Add("index", i);
                o.Add("kind", isOffense ? "attack" : "defense");
                o.Add("id", row);
                o.Add("energy", isOffense ? character.training.attackEnergy[row] : character.training.defenseEnergy[row]);
                o.Add("cap", isOffense ? character.training.attackCaps[row] : character.training.defenseCaps[row]);
                o.Add("locked", isOffense ? offense[row].locked() : defense[row].locked());
                trainings.Add(o);
            }

            var funnel = new JSONObject();
            funnel.Add("energyEnabled", AutoFunnel.EnergyEnabled);
            funnel.Add("magicEnabled", AutoFunnel.MagicEnabled);
            funnel.Add("energyTarget", AutoFunnel.EnergyTarget);
            funnel.Add("energyTargetAmount", AutoFunnel.EnergyTargetAmount);
            funnel.Add("magicTarget", AutoFunnel.MagicTarget);
            funnel.Add("magicTargetAmount", AutoFunnel.MagicTargetAmount);
            funnel.Add("ritualsUnlockedCount", unlocked);

            // the time machine's two bars, enrolled as candidates in both funnels by one toggle.
            // energyTarget/magicTarget == tmSink means the time machine won this tick.
            funnel.Add("tmEnrolled", AutoFunnel.TimeMachineEnabled);
            funnel.Add("tmSink", AutoFunnel.TimeMachineSink);
            funnel.Add("tmEnergy", character.machine.speedEnergy);
            funnel.Add("tmMagic", character.machine.goldMultiMagic);
            funnel.Add("tmEnergyAllocator", AutoFunnel.TmEnergyAllocatorOn);
            funnel.Add("tmMagicAllocator", AutoFunnel.TmMagicAllocatorOn);
            funnel.Add("augAssignments", augAssignments);
            funnel.Add("ritualAssignments", ritualAssignments);

            var root = new JSONObject();
            root.Add("menuID", character.menuID);
            root.Add("curEnergy", character.curEnergy);
            root.Add("idleEnergy", character.idleEnergy);
            root.Add("curMagic", character.magic.curMagic);
            root.Add("idleMagic", character.magic.idleMagic);

            // the funnel overrides this while adding and restores it - a value that moves between
            // polls means the user's typed amount is being clobbered
            root.Add("input", character.input.energyMagicInput);
            root.Add("ap", character.arbitrary.curArbitraryPoints);
            root.Add("exp", character.realExp);
            root.Add("boss", boss);
            root.Add("funnel", funnel);
            var mergeBoost = new JSONObject();
            mergeBoost.Add("enabled", AutoMergeBoost.Enabled);
            mergeBoost.Add("intervalSeconds", AutoMergeBoost.IntervalSeconds);
            mergeBoost.Add("runs", AutoMergeBoost.Runs);
            mergeBoost.Add("lastRunSummary", AutoMergeBoost.LastRunSummary);
            root.Add("autoMergeBoost", mergeBoost);

            root.Add("capPullEnabled", CapPull.Enabled);
            root.Add("syncTraining", character.settings.syncTraining);
            root.Add("trainings", trainings);

            return root.ToString();
        }

        private static string BuildWishStats()
        {
            var character = Plugin.Character;

            var root = new JSONObject();
            root.Add("blueHeart", character.inventory.itemList.itemMaxxed[(int)GameData.Items.Heart_Blue]);
            root.Add("epow", character.totalEnergyPower());
            root.Add("ecap", character.totalCapEnergy());
            root.Add("mpow", character.totalMagicPower());
            root.Add("mcap", character.totalCapMagic());
            root.Add("rpow", character.totalRes3Power());
            root.Add("rcap", character.totalCapRes3());
            root.Add("wishspeed", character.wishesController.totalWishSpeedBonuses());

            return root.ToString();
        }
    }
}
