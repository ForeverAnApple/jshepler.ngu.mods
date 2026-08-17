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

            void add(Equipment e, string where)
            {
                if (e == null || e.id == 0)
                    return;

                var o = new JSONObject();
                o.Add("id", e.id);
                o.Add("level", e.level);
                o.Add("where", where);
                items.Add(o);
            }

            add(inv.head, "head");
            add(inv.chest, "chest");
            add(inv.legs, "legs");
            add(inv.boots, "boots");
            add(inv.weapon, "weapon");
            add(inv.weapon2, "weapon2");
            foreach (var acc in inv.accs)
                add(acc, "accessory");
            foreach (var item in inv.inventory)
                add(item, "inventory");
            foreach (var item in inv.daycare)
                add(item, "daycare");

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
