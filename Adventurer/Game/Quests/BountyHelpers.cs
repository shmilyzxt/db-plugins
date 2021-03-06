﻿using System.Collections.Generic;
using System.Linq;
using Adventurer.Game.Actors;
using Adventurer.Game.Events;
using Adventurer.Settings;
using Adventurer.Util;
using Zeta.Common;
using Zeta.Game;
using Zeta.Game.Internals;
using Zeta.Game.Internals.Actors;
using Zeta.Game.Internals.Actors.Gizmos;
using Logger = Adventurer.Util.Logger;

namespace Adventurer.Game.Quests
{
    public static class BountyHelpers
    {

        //ActorId: 365020, Type: Item, Name: Khanduran Rune //A1
        //ActorId: 364281, Type: Item, Name: Caldeum Nightshade //A2
        //ActorId: 364290, Type: Item, Name: Arreat War Tapestry //A3
        //ActorId: 364305, Type: Item, Name: Corrupted Angel Flesh //A4
        //ActorId: 364975, Type: Item, Name: Westmarch Holy Water //A5

        public static readonly int Act1BountyMatSNO = 365020;
        public static readonly int Act2BountyMatSNO = 364281;
        public static readonly int Act3BountyMatSNO = 364290;
        public static readonly int Act4BountyMatSNO = 364305;
        public static readonly int Act5BountyMatSNO = 364975;

        public static long GetActMatsCount(Act act)
        {
            var matSno = 0;
            switch (act)
            {
                case Act.A1:
                    matSno = Act1BountyMatSNO;
                    break;
                case Act.A2:
                    matSno = Act2BountyMatSNO;
                    break;
                case Act.A3:
                    matSno = Act3BountyMatSNO;
                    break;
                case Act.A4:
                    matSno = Act4BountyMatSNO;
                    break;
                case Act.A5:
                    matSno = Act5BountyMatSNO;
                    break;
            }
            var stashCount = ZetaDia.Me.Inventory.StashItems.Where(i => i.IsValid && i.ActorSNO == matSno).Sum(i => i.ItemStackQuantity);
            var backpackCount = ZetaDia.Me.Inventory.Backpack.Where(i => i.IsValid && i.ActorSNO == matSno).Sum(i => i.ItemStackQuantity);
            return stashCount + backpackCount;
        }


        public static Vector3 ScanForMarkerLocation(int markerNameHash, int searchRadius)
        {
            var miniMapMarker = AdvDia.CurrentWorldMarkers
                .FirstOrDefault(m => m.NameHash == markerNameHash && m.Position.Distance2D(AdvDia.MyPosition) <= searchRadius && !EntryPortals.IsEntryPortal(AdvDia.CurrentWorldDynamicId, m.NameHash));
            if (miniMapMarker != null)
            {
                return miniMapMarker.Position;
            }
            return Vector3.Zero;
        }

        public static Vector3 ScanForActorLocation(int actorId, int searchRadius)
        {
            var actor =
                ZetaDia.Actors.GetActorsOfType<DiaObject>(true)
                    .FirstOrDefault(a => a.IsFullyValid() && a.ActorSNO == actorId && a.Position.Distance2D(AdvDia.MyPosition) <= searchRadius);
            if (actor != null)
            {
                return actor.Position;
            }
            return Vector3.Zero;
        }


        public static bool AreAllActBountiesCompleted(Act act)
        {
            return !ZetaDia.ActInfo.Bounties.Any(b => b.Act == act && b.Info.State != QuestState.Completed);
        }


        public static DiaGizmo GetPortalNearMarkerPosition(Vector3 position)
        {
            if (JustEnteredWorld)
            {
                return null;
            }
            using (new PerformanceLogger())
            {
                var gizmo = ZetaDia.Actors.GetActorsOfType<DiaGizmo>(true)
                    .Where(o => o != null && o.IsFullyValid() && o is GizmoPortal && o.Position.Distance(position) <= 10f)
                    .OrderBy(o => o.Distance)
                    .FirstOrDefault();
                return gizmo;
            }
        }



        public static bool JustEnteredWorld
        {
            get
            {
                if (PluginTime.CurrentMillisecond - PluginEvents.WorldChangeTime < 5000)
                {
                    return true;
                }
                return false;
            }
        }

        public static bool AreAllActBountiesSupported(Act act)
        {
            var result =
                ZetaDia.ActInfo.Bounties.Count(
                    b => b.Act == act && BountyDataFactory.GetBountyData((int) b.Quest) != null) == 5;
            if (!result)
            {
                Logger.Debug("[Bounties] Unsupported bounties are detected in {0}", act);
            }
            return result;
        }

        public static bool IsActEnabledOnSettings(Act act)
        {
            switch (act)
            {
                case Act.A1:
                    return PluginSettings.Current.BountyAct1;
                case Act.A2:
                    return PluginSettings.Current.BountyAct2;
                case Act.A3:
                    return PluginSettings.Current.BountyAct3;
                case Act.A4:
                    return PluginSettings.Current.BountyAct4;
                case Act.A5:
                    return PluginSettings.Current.BountyAct5;
            }
            return false;
        }

        public static readonly Dictionary<Act, SNOQuest> ActBountyFinishingQuests = new Dictionary<Act, SNOQuest>
        {
            {Act.A1,SNOQuest.x1_AdventureMode_BountyTurnin_A1},
            {Act.A2,SNOQuest.x1_AdventureMode_BountyTurnin_A2},
            {Act.A3,SNOQuest.x1_AdventureMode_BountyTurnin_A3},
            {Act.A4,SNOQuest.x1_AdventureMode_BountyTurnin_A4},
            {Act.A5,SNOQuest.x1_AdventureMode_BountyTurnin_A5},
        };

        public static bool IsActTurninInProgress(Act act)
        {
            return
                ZetaDia.ActInfo.AllQuests.Any(
                    q => q.Quest == ActBountyFinishingQuests[act] && q.State == QuestState.InProgress);
        }

        public static bool IsActTurninCompleted(Act act)
        {
            return
                ZetaDia.ActInfo.AllQuests.Any(
                    q => q.Quest == ActBountyFinishingQuests[act] && q.State == QuestState.Completed);
        }
    }
}
