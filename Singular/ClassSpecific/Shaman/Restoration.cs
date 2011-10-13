﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Singular.Helpers;

using Styx;
using Styx.WoWInternals.WoWObjects;

using TreeSharp;

namespace Singular.ClassSpecific.Shaman
{
    class Restoration
    {
        private const int RESTO_T12_ITEM_SET_ID = 1014;
        private const int ELE_T12_ITEM_SET_ID = 1016;

        private static int NumTier12Pieces
        {
            get
            {
                int count = StyxWoW.Me.Inventory.Equipped.Hands.ItemInfo.ItemSetId == RESTO_T12_ITEM_SET_ID ? 1 : 0;
                count += StyxWoW.Me.Inventory.Equipped.Legs.ItemInfo.ItemSetId == RESTO_T12_ITEM_SET_ID ? 1 : 0;
                count += StyxWoW.Me.Inventory.Equipped.Chest.ItemInfo.ItemSetId == RESTO_T12_ITEM_SET_ID ? 1 : 0;
                count += StyxWoW.Me.Inventory.Equipped.Shoulder.ItemInfo.ItemSetId == RESTO_T12_ITEM_SET_ID ? 1 : 0;
                count += StyxWoW.Me.Inventory.Equipped.Head.ItemInfo.ItemSetId == RESTO_T12_ITEM_SET_ID ? 1 : 0;
                return count;
            }
        }

        public static Composite CreateRestoShamanHealingBuffs()
        {
            return new PrioritySelector(
                // Keep WS up at all times. Period.
                Spell.BuffSelf("Water Shield"),

                new Decorator(
                    ret => !Item.HasWeapoinImbue(WoWInventorySlot.MainHand, "Earthliving"),
                    Spell.Cast("Earthliving Weapon"))
                );
        }
        public static Composite CreateRestoShamanHealing()
        {
            return new PrioritySelector(
                context => Managers.HealerManager.Instance.FirstUnit,
                Movement.CreateMoveToLosBehavior(ret => (WoWUnit)ret),

                Spell.WaitForCast(false),
                Totems.CreateSetTotems(),
                Spell.Buff("Earth Shield", ret => Group.Tank, ret => StyxWoW.Me.IsInRaid || StyxWoW.Me.IsInParty),

                // Pop NS if someone is in some trouble.
                Spell.BuffSelf("Nature's Swiftness", ret => ((WoWUnit)ret).HealthPercent < 15),
                Spell.Cast("Unleash Elements", ret => (WoWUnit)ret, ret => ((WoWUnit)ret).HealthPercent < 40),
                // GHW is highest priority. It should be fairly low health %. (High-end healers will have this set to 70ish
                Spell.Cast("Greater Healing Wave", ret => (WoWUnit)ret, ret => ((WoWUnit)ret).HealthPercent < 50),
                // Most (if not all) will leave this at 90. Its lower prio, high HPM, low HPS
                Spell.Cast("Healing Wave", ret => (WoWUnit)ret, ret => ((WoWUnit)ret).HealthPercent < 90),

                // Just pop RT on CD. Plain and simple. Calling GetBestRiptideTarget will see if we can spread RTs (T12 2pc)
                Spell.Cast("Riptide", ret => GetBestRiptideTarget((WoWPlayer)ret)),

                // CH/HR only in parties/raids
                new Decorator(
                    ret => StyxWoW.Me.IsInParty || StyxWoW.Me.IsInRaid,
                    new PrioritySelector(
                        // This seems a bit tricky, but its really not. This is just how we cache a somewhat expensive lookup.
                        // Set the context to the "best unit" for the cluster, so we don't have to do that check twice.
                        // Then just use the context when passing the unit to throw the heal on, and the target of the heal from the cluster count.
                        // Also ensure it will jump at least 3 times. (CH is pointless to cast if it won't jump 3 times!)
                        new PrioritySelector(
                            context => Clusters.GetBestUnitForCluster(ChainHealPlayers, ClusterType.Chained, 12f),
                            Spell.Cast(
                                "Chain Heal", ret => (WoWPlayer)ret,
                                ret => Clusters.GetClusterCount((WoWPlayer)ret, ChainHealPlayers, ClusterType.Chained, 12f) > 2)),

                        // Now we're going to do the same thing as above, but this time we're going to do it with healing rain.
                        new PrioritySelector(
                            context => Clusters.GetBestUnitForCluster(Unit.NearbyFriendlyPlayers.Cast<WoWUnit>(), ClusterType.Radius, 10f),
                            Spell.CastOnGround(
                                "Healing Rain", ret => ((WoWPlayer)ret).Location,
                                ret =>
                                Clusters.GetClusterCount((WoWPlayer)ret, Unit.NearbyFriendlyPlayers.Cast<WoWUnit>(), ClusterType.Radius, 10f) >
                                // If we're in a raid, check for 4 players. If we're just in a party, check for 3.
                                (StyxWoW.Me.IsInRaid ? 3 : 2))))),

                // Make sure we're in LOS of the target.
                Movement.CreateMoveToTargetBehavior(true, 38f, ret => (WoWUnit)ret)

                );
        }

        private static IEnumerable<WoWUnit> ChainHealPlayers
        {
            get
            {
                // TODO: Decide if we want to do this differently to ensure we take into account the T12 4pc bonus. (Not removing RT when using CH)
                return Unit.NearbyFriendlyPlayers.Where(u => u.HealthPercent < 90).Select(u => (WoWUnit)u);
            }
        }

        private static WoWPlayer GetBestRiptideTarget(WoWPlayer originalTarget)
        {
            if (!originalTarget.HasMyAura("Riptide"))
                return originalTarget;

            // Target already has RT. So lets find someone else to throw it on. Lowest health first preferably.
            return Unit.NearbyFriendlyPlayers.OrderBy(u => u.HealthPercent).Where(u => !u.HasMyAura("Riptide")).FirstOrDefault();
        }
    }
}