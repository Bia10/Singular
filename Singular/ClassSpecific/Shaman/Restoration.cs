﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;
using Styx;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

using Styx.TreeSharp;
using Styx.CommonBot;
using System.Drawing;

using CommonBehaviors.Actions;
using Action = Styx.TreeSharp.Action;


namespace Singular.ClassSpecific.Shaman
{
    class Restoration
    {
        private const int RESTO_T12_ITEM_SET_ID = 1014;
        private const int ELE_T12_ITEM_SET_ID = 1016;

        private static LocalPlayer Me { get { return StyxWoW.Me; } }
        private static ShamanSettings ShamanSettings { get { return SingularSettings.Instance.Shaman(); } }

        #region BUFFS

        [Behavior(BehaviorType.CombatBuffs | BehaviorType.PreCombatBuffs, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Normal )]
        public static Composite CreateRestoShamanHealingBuffsNormal()
        {
            return new PrioritySelector(

                Spell.BuffSelf("Water Shield", ret => Me.ManaPercent < 20),
                Spell.BuffSelf("Earth Shield", ret => Me.ManaPercent > 35),

                Common.CreateShamanImbueMainHandBehavior(Imbue.Earthliving, Imbue.Flametongue)
                );
        }

        [Behavior(BehaviorType.CombatBuffs | BehaviorType.PreCombatBuffs, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Battlegrounds)]
        public static Composite CreateRestoHealingBuffsBattlegrounds()
        {
            return new PrioritySelector(
                Spell.BuffSelf("Earth Shield", ret => Me.ManaPercent > 25),
                Spell.BuffSelf("Water Shield", ret => Me.ManaPercent < 15),

                Common.CreateShamanImbueMainHandBehavior(Imbue.Earthliving, Imbue.Flametongue)
                );
        }

        private static ulong guidLastEarthShield = 0;

        [Behavior(BehaviorType.CombatBuffs | BehaviorType.PreCombatBuffs, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Instances )]
        public static Composite CreateRestoHealingBuffsInstance()
        {
            return new Decorator(
                ret => !Spell.IsGlobalCooldown() && !Spell.IsCastingOrChannelling(),
                new PrioritySelector(

                    new Throttle( 2,
                        new PrioritySelector(
                            Spell.Buff("Earth Shield", on => GetBestEarthShieldTargetInstance()),
                            Spell.BuffSelf("Water Shield", ret => !Me.HasAura("Earth Shield"))
                            )
                        ),

                    Common.CreateShamanImbueMainHandBehavior(Imbue.Earthliving, Imbue.Flametongue)
                    )
                );
        }

        /// <summary>
        /// selects best Earth Shield target
        /// </summary>
        /// <returns></returns>
        private static WoWUnit GetBestEarthShieldTargetInstance()
        {
            WoWUnit target = null;

            if (IsValidEarthShieldTarget(RaFHelper.Leader))
                target = RaFHelper.Leader;
            else
            {
                target = Group.Tanks.FirstOrDefault(t => IsValidEarthShieldTarget(t));
                if (Me.Combat && target == null && !Unit.NearbyGroupMembers.Any(m => m.HasMyAura("Earth Shield")))
                {
                    target = Unit.NearbyGroupMembers.Where(u => IsValidEarthShieldTarget(u.ToUnit())).OrderByDescending(u => Unit.NearbyUnfriendlyUnits.Count(e => e.CurrentTargetGuid == u.Guid)).FirstOrDefault();
                }
            }

            guidLastEarthShield = target != null ? target.Guid : 0;
            return target;
        }

        /// <summary>
        /// selects best Earth Shield target
        /// </summary>
        /// <returns></returns>
        private static WoWUnit GetBestEarthShieldTargetBattlegrounds()
        {
#if SUPPORT_MOST_IN_NEED_EARTHSHIELD_LOGIC
            var target = Unit.NearbyGroupMembers
                .Where(u => IsValidEarthShieldTarget(u.ToUnit()))
                .Select(u => new { Unit = u, Health = u.HealthPercent, EnemyCount = Unit.NearbyUnfriendlyUnits.Count(e => e.CurrentTargetGuid == u.Guid)})
                .OrderByDescending(v => v.EnemyCount)
                .ThenBy(v => v.Health )
                .FirstOrDefault();

            if (target == null)
            {
                guidLastEarthShield = Me.Guid;
            }
            else if (guidLastEarthShield != target.Unit.Guid)
            {
                guidLastEarthShield = target.Unit.Guid;
                Logger.WriteDebug("Best Earth Shield Target appears to be: {0} @ {1:F0}% with {2} attackers", target.Unit.SafeName(), target.Health, target.EnemyCount);
            }

            return target == null ? Me : target.Unit;
#else
            return Me;
#endif
        }

        private static bool IsValidEarthShieldTarget(WoWUnit unit)
        {
            if ( unit == null || !unit.IsValid || !unit.IsAlive || !Unit.GroupMembers.Any( g => g.Guid == unit.Guid ) || unit.Distance > 99 )
                return false;

            return unit.HasMyAura("Earth Shield") || !unit.HasAnyAura( "Earth Shield", "Water Shield", "Lightning Shield");
        }

        #endregion

        #region NORMAL 

        [Behavior(BehaviorType.Rest, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Normal)]
        public static Composite CreateRestoShamanRest()
        {
            return new PrioritySelector(
                Spell.WaitForCastOrChannel(),
                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(

                        CreateRestoShamanHealingBuffsNormal(),
                        new Decorator(
                            ret => !StyxWoW.Me.HasAura("Drink") && !StyxWoW.Me.HasAura("Food"),
                            CreateRestoShamanHealingOnlyBehavior(true, false)
                            ),
                        Singular.Helpers.Rest.CreateDefaultRestBehaviour(null, "Ancestral Spirit"),
                        CreateRestoShamanHealingOnlyBehavior(false, true)
                        )
                    )
                );
        }

        [Behavior(BehaviorType.Pull, WoWClass.Shaman, WoWSpec.ShamanRestoration)]
        public static Composite CreateRestoShamanPullBehavior()
        {
            return
                new PrioritySelector(
                    Helpers.Common.CreateDismount("Pulling"),
                    Spell.WaitForCastOrChannel(),
                    CreateRestoShamanHealingOnlyBehavior(false, true),
                    new Decorator(
                        ret => !Unit.NearbyFriendlyPlayers.Any(u => u.IsInMyPartyOrRaid),

                        new PrioritySelector(
                            Safers.EnsureTarget(),
                            Movement.CreateMoveToLosBehavior(),
                            Movement.CreateFaceTargetBehavior(),
                            Helpers.Common.CreateDismount("Pulling"),

                            new Decorator(
                                ret => Me.GotTarget && Me.CurrentTarget.Distance < 35,
                                Movement.CreateEnsureMovementStoppedBehavior()
                                ),

                            Spell.WaitForCastOrChannel(),

                            new Decorator(
                                ret => !Spell.IsGlobalCooldown(),
                                new PrioritySelector(

                                    new Decorator(
                                        ret => StyxWoW.Me.CurrentTarget.DistanceSqr < 40 * 40,
                                        Totems.CreateTotemsNormalBehavior()),

                            // grinding or questing, if target meets these cast Flame Shock if possible
                            // 1. mob is less than 12 yds, so no benefit from delay in Lightning Bolt missile arrival
                            // 2. area has another player competing for mobs (we want to tag the mob quickly)
                                    new Decorator(
                                        ret => StyxWoW.Me.CurrentTarget.Distance < 12
                                            || ObjectManager.GetObjectsOfType<WoWPlayer>(false, false).Any(p => p.Location.DistanceSqr(StyxWoW.Me.CurrentTarget.Location) <= 40 * 40),
                                        new PrioritySelector(
                                            Spell.Buff("Flame Shock", true),
                                            Spell.Cast("Earth Shock", ret => !SpellManager.HasSpell("Flame Shock"))
                                            )
                                        ),

                                    Spell.Cast("Lightning Bolt", mov => false, on => Me.CurrentTarget, ret => !StyxWoW.Me.IsMoving || StyxWoW.Me.HasAura("Spiritwalker's Grace") || TalentManager.HasGlyph("Unleashed Lightning")),
                                    Spell.Cast("Flame Shock"),
                                    Spell.Cast("Unleash Weapon", ret => Common.IsImbuedForDPS(StyxWoW.Me.Inventory.Equipped.MainHand))
                                    )
                                ),

                            Movement.CreateMoveToTargetBehavior(true, 35)
                            )
                        )
                    );
        }

        [Behavior(BehaviorType.Heal, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Normal)]
        public static Composite CreateRestoShamanHealBehaviorNormal()
        {
            return
                new PrioritySelector(
                    Spell.WaitForCastOrChannel(),
                    CreateRestoShamanHealingOnlyBehavior());
        }

        [Behavior(BehaviorType.Combat, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Normal)]
        public static Composite CreateRestoShamanCombatBehaviorNormal()
        {
            return
                new PrioritySelector(
                    Spell.WaitForCastOrChannel(),
                    CreateRestoShamanHealingOnlyBehavior(),
                    new Decorator(
                        ret => !Unit.NearbyFriendlyPlayers.Any(u => u.IsInMyPartyOrRaid),
                        new PrioritySelector(
                            Safers.EnsureTarget(),
                            Movement.CreateMoveToLosBehavior(),
                            Movement.CreateFaceTargetBehavior(),

                            new Decorator(
                                ret => Me.GotTarget && Me.CurrentTarget.Distance < 35,
                                Movement.CreateEnsureMovementStoppedBehavior()
                                ),

                            Spell.WaitForCastOrChannel(),

                            new Decorator(
                                ret => !Spell.IsGlobalCooldown(),
                                new PrioritySelector(

                                    Helpers.Common.CreateInterruptBehavior(),

                                    Totems.CreateTotemsNormalBehavior(),

                                    Spell.Cast("Elemental Blast"),
                                    Spell.Buff("Flame Shock", true),

                                    Spell.Cast("Lava Burst"),
                                    Spell.Cast("Earth Shock",
                                        ret => StyxWoW.Me.HasAura("Lightning Shield", 5) &&
                                               StyxWoW.Me.CurrentTarget.GetAuraTimeLeft("Flame Shock", true).TotalSeconds > 3),

                                    Spell.Cast("Chain Lightning", ret => Spell.UseAOE && Spell.UseAOE && Unit.UnfriendlyUnitsNearTarget(10f).Count() >= 2 && !Unit.UnfriendlyUnitsNearTarget(10f).Any(u => u.IsCrowdControlled())),
                                    Spell.Cast("Lightning Bolt")
                                    )
                                ),

                            Movement.CreateMoveToTargetBehavior(true, 35)
                            // Movement.CreateMoveToRangeAndStopBehavior(ret => Me.CurrentTarget, ret => NormalPullDistance)
                            )
                        
                        )
                    );
        }

        #endregion

        #region INSTANCES

        [Behavior(BehaviorType.Rest, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Instances)]
        public static Composite CreateRestoShamanRestInstances()
        {
            return new PrioritySelector(
                Spell.WaitForCastOrChannel(),
                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(
                        CreateRestoHealingBuffsInstance(),
                        new Decorator(
                            ret => !StyxWoW.Me.HasAura("Drink") && !StyxWoW.Me.HasAura("Food"),
                            CreateRestoShamanHealingOnlyBehavior(true, false)
                            ),
                        Singular.Helpers.Rest.CreateDefaultRestBehaviour(null, "Ancestral Spirit"),
                        CreateRestoShamanHealingOnlyBehavior(false, true)
                        )
                    )
                );
        }

        [Behavior(BehaviorType.Heal, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Instances)]
        public static Composite CreateRestoShamanHealBehaviorInstance()
        {
            return new PrioritySelector(
                Spell.WaitForCastOrChannel(),
                CreateRestoShamanHealingOnlyBehavior()
                );
        }

        [Behavior(BehaviorType.Combat, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Instances )]
        public static Composite CreateRestoShamanCombatBehaviorInstances()
        {
            return new PrioritySelector(
                Spell.WaitForCastOrChannel(),

                CreateRestoShamanHealingOnlyBehavior(),

                // no healing needed, then move within heal range of tank
                Movement.CreateMoveToRangeAndStopBehavior(ret => BestTankToMoveNear, ret => 38f),

                new Decorator(
                    ret => !Unit.NearbyFriendlyPlayers.Any(u => u.IsInMyPartyOrRaid) || TalentManager.HasGlyph("Telluric Currents"),
                    new PrioritySelector(
                        Safers.EnsureTarget(),
                        Movement.CreateMoveToLosBehavior(),
                        Movement.CreateFaceTargetBehavior(),
                        Spell.WaitForCast(),

                        new Decorator(
                            ret => !Spell.GcdActive,
                            new PrioritySelector(
                                Helpers.Common.CreateInterruptBehavior(),
                                Spell.Cast("Lightning Bolt")
                                )
                            )
                        )
                    )
                );

        }

        private static WoWUnit BestTankToMoveNear
        {
            get
            {
                if (!SingularSettings.Instance.StayNearTank)
                    return null;

                if (RaFHelper.Leader != null && RaFHelper.Leader.IsValid && RaFHelper.Leader.IsAlive && RaFHelper.Leader.Distance < 100)
                    return RaFHelper.Leader;

                return Group.Tanks.Where(t => t.IsAlive && t.Distance < SingularSettings.Instance.MaxHealTargetRange).OrderBy(t => t.Distance).FirstOrDefault();
            }
        }

        #endregion

        #region BATTLEGROUNDS

        [Behavior(BehaviorType.Rest, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Battlegrounds )]
        public static Composite CreateRestoShamanRestPvp()
        {
            return new PrioritySelector(
                Spell.WaitForCastOrChannel(),
                Spell.WaitForCast(),
                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(
                        new Decorator(
                            ret => !StyxWoW.Me.HasAura("Drink") && !StyxWoW.Me.HasAura("Food"),
                            CreateRestoShamanHealingOnlyBehavior(true, false)
                            ),
                        Singular.Helpers.Rest.CreateDefaultRestBehaviour(null, "Ancestral Spirit"),
                        CreateRestoShamanHealingOnlyBehavior(false, true)
                        )
                    )
                );
        }

        [Behavior(BehaviorType.Combat, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Battlegrounds)]
        public static Composite CreateRestoShamanCombatBehaviorPvp()
        {
            return new PrioritySelector(
                Spell.WaitForCastOrChannel(),
                CreateRestoShamanHealingOnlyBehavior(false, true)
                );
        }

        [Behavior(BehaviorType.Heal, WoWClass.Shaman, WoWSpec.ShamanRestoration, WoWContext.Battlegrounds)]
        public static Composite CreateRestoShamanHealBehaviorPvp()
        {
            return new PrioritySelector(
                Spell.WaitForCastOrChannel(),
                CreateRestoShamanHealingOnlyBehavior(false, true)
                );
        }

        #endregion


        public static Composite CreateRestoShamanHealingOnlyBehavior()
        {
            return CreateRestoShamanHealingOnlyBehavior(false, true);
        }

        public static Composite CreateRestoShamanHealingOnlyBehavior(bool selfOnly)
        {
            return CreateRestoShamanHealingOnlyBehavior(selfOnly, true);
        }

        private static ulong guidLastHealTarget = 0;

        public static Composite CreateRestoShamanHealingOnlyBehavior(bool selfOnly, bool moveInRange)
        {
            HealerManager.NeedHealTargeting = true;
            PrioritizedBehaviorList behavs = new PrioritizedBehaviorList();

            if (SpellManager.HasSpell("Earthliving Weapon"))
            {
                behavs.AddBehavior(HealthToPriority(ShamanSettings.Heal.AncestralSwiftness),
                    "Unleash Elements",
                    "Unleash Elements",
                    Spell.Buff("Unleash Elements",
                        ret => (WoWUnit)ret,
                        ret => (Me.IsMoving || ((WoWUnit)ret).GetPredictedHealthPercent() < ShamanSettings.Heal.AncestralSwiftness)
                            && Common.IsImbuedForHealing(Me.Inventory.Equipped.MainHand)
                            ));
            }

            behavs.AddBehavior(HealthToPriority( ShamanSettings.Heal.AncestralSwiftness),
                String.Format("Ancestral Swiftness @ {0}%", ShamanSettings.Heal.AncestralSwiftness), 
                "Ancestral Swiftness",
                new Decorator(
                    ret => ((WoWUnit)ret).GetPredictedHealthPercent() < ShamanSettings.Heal.AncestralSwiftness,
                    new Sequence(
                        Spell.BuffSelf("Ancestral Swiftness"),
                        Spell.Cast("Greater Healing Wave", ret => (WoWUnit)ret)
                        )
                    )
                );

            if ( SingularRoutine.CurrentWoWContext == WoWContext.Instances )
                behavs.AddBehavior( 9999, "Earth Shield", "Earth Shield", Spell.Buff("Earth Shield", on => GetBestEarthShieldTargetInstance()));

            int dispelPriority = (SingularSettings.Instance.DispelDebuffs == DispelStyle.HighPriority) ? 9999 : -9999;
            behavs.AddBehavior( dispelPriority, "Purify Spirit", null, Dispelling.CreateDispelBehavior());

            behavs.AddBehavior(HealthToPriority(ShamanSettings.Heal.GreaterHealingWave), "Greater Healing Wave", "Greater Healing Wave",
                Spell.Cast("Greater Healing Wave", ret => (WoWUnit)ret, ret => ((WoWUnit)ret).GetPredictedHealthPercent() < ShamanSettings.Heal.GreaterHealingWave));

            behavs.AddBehavior(HealthToPriority(ShamanSettings.Heal.HealingWave), "Healing Wave", "Healing Wave",
                Spell.Cast("Healing Wave", ret => (WoWUnit)ret, ret => ((WoWUnit)ret).GetPredictedHealthPercent() < ShamanSettings.Heal.HealingWave));

            behavs.AddBehavior(HealthToPriority(ShamanSettings.Heal.HealingSurge), "Healing Surge", "Healing Surge", 
                Spell.Cast("Healing Surge", ret => (WoWUnit)ret, ret => ((WoWUnit)ret).GetPredictedHealthPercent() < ShamanSettings.Heal.HealingSurge));

            behavs.AddBehavior(HealthToPriority( ShamanSettings.Heal.ChainHeal), "Chain Heal", "Chain Heal", 
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    new PrioritySelector(
                        new PrioritySelector(
                            context => Clusters.GetBestUnitForCluster(ChainHealPlayers, ClusterType.Chained, ChainHealHopRange ),
                            Spell.Cast(
                                "Chain Heal", ret => (WoWPlayer)ret,
                                ret => Clusters.GetClusterCount((WoWPlayer)ret, ChainHealPlayers, ClusterType.Chained, ChainHealHopRange) >= 3)
                            )
                        )
                    )
                );

            behavs.AddBehavior(HealthToPriority( ShamanSettings.Heal.HealingRain), "Healing Rain", "Healing Rain",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    new PrioritySelector(
                        context => Clusters.GetBestUnitForCluster(Unit.NearbyFriendlyPlayers.Cast<WoWUnit>(), ClusterType.Radius, 10f),
                        Spell.CastOnGround(
                            "Healing Rain", 
                            ret => ((WoWPlayer)ret).Location,
                            ret => (StyxWoW.Me.GroupInfo.IsInRaid ? 3 : 2) < Clusters.GetClusterCount((WoWPlayer)ret, Unit.NearbyFriendlyPlayers.Cast<WoWUnit>(), ClusterType.Radius, 10f))
                        )
                    )
                );

            behavs.AddBehavior(HealthToPriority( ShamanSettings.Heal.SpiritLinkTotem), "Spirit Link Totem", "Spirit Link Totem",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    Spell.Cast(
                        "Spirit Link Totem", ret => (WoWPlayer)ret,
                        ret => Unit.NearbyFriendlyPlayers.Count(
                            p => p.GetPredictedHealthPercent() < ShamanSettings.Heal.SpiritLinkTotem && p.Distance <= Totems.GetTotemRange(WoWTotem.SpiritLink)) >= (Me.GroupInfo.IsInRaid ? 3 : 2)
                        )
                    )
                );

            behavs.AddBehavior(HealthToPriority( ShamanSettings.Heal.HealingTideTotemPercent), "Healing Tide Totem", "Healing Tide Totem",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    Spell.Cast(
                        "Healing Tide Totem",
                        ret => Unit.NearbyFriendlyPlayers.Count(p => p.GetPredictedHealthPercent() < ShamanSettings.Heal.HealingTideTotemPercent && p.Distance <= Totems.GetTotemRange(WoWTotem.HealingTide)) >= (Me.GroupInfo.IsInRaid ? 3 : 2)
                        )
                    )
                );

            behavs.AddBehavior(HealthToPriority( ShamanSettings.Heal.HealingStreamTotem), "Healing Stream Totem", "Healing Stream Totem",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    Spell.Cast(
                        "Healing Stream Totem",
                        ret => Unit.NearbyFriendlyPlayers.Count(p => p.GetPredictedHealthPercent() < ShamanSettings.Heal.HealingStreamTotem && p.Distance <= Totems.GetTotemRange(WoWTotem.HealingTide)) >= (Me.GroupInfo.IsInRaid ? 3 : 2)
                            && !Totems.Exist( WoWTotemType.Water)
                        )
                    )
                );

            behavs.AddBehavior(HealthToPriority( ShamanSettings.Heal.Ascendance), "Ascendance", "Ascendance",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    Spell.BuffSelf(
                        "Ascendance",
                        ret => Unit.NearbyFriendlyPlayers.Count(p => p.GetPredictedHealthPercent() < ShamanSettings.Heal.Ascendance) >= (Me.GroupInfo.IsInRaid ? 3 : 2)
                        )
                    )
                );


            behavs.OrderBehaviors();
            behavs.ListBehaviors();


            return new PrioritySelector(
                ctx => selfOnly ? StyxWoW.Me : HealerManager.Instance.FirstUnit,
//                ctx => selfOnly ? StyxWoW.Me : GetHealTarget(),

                Spell.WaitForCastOrChannel(),

                new Decorator(
                    ret => Me.IsCasting,
                    new ActionAlwaysSucceed()),

                new Decorator(
                    ret => ret != null && ((WoWUnit)ret).GetPredictedHealthPercent() <= SingularSettings.Instance.IgnoreHealTargetsAboveHealth,

                    new PrioritySelector(

                        new Sequence(
                            new Decorator(
                                ret => guidLastHealTarget != ((WoWUnit)ret).Guid,
                                new Action(ret =>
                                {
                                    guidLastHealTarget = ((WoWUnit)ret).Guid;
                                    Logger.WriteDebug(Color.LightGreen, "Heal Target - {0} {1:F1}% @ {2:F1} yds", ((WoWUnit)ret).SafeName(), ((WoWUnit)ret).GetPredictedHealthPercent(), ((WoWUnit)ret).Distance);
                                })),
                            new Action(ret => { return RunStatus.Failure; })
                            ),

/*
                        new Sequence(
                            new Action(ret => Logger.WriteDebug(Color.LightGreen, "-- past spellcast")),
                            new Action(ret => { return RunStatus.Failure; })
                            ),
*/
                        new Decorator(
                            ret => !Spell.GcdActive,

                            new PrioritySelector(
    /*
                                new Sequence(
                                    new Action(ret => Logger.WriteDebug(Color.LightGreen, "-- past gcd")),
                                    new Action(ret => { return RunStatus.Failure; })
                                    ),
    */
                            Totems.CreateTotemsBehavior(),

/*
                            Spell.Cast("Earth Shield",
                                ret => (WoWUnit)ret,
                                ret => ret is WoWPlayer && Group.Tanks.Contains((WoWPlayer)ret) && Group.Tanks.All(t => !t.HasMyAura("Earth Shield"))),
*/
                            // cast Riptide if we need Tidal Waves -- skip if Ancestral Swiftness is 
                            Spell.Cast("Riptide",
                                ret => GetBestRiptideTarget((WoWPlayer)ret),
                                ret => SpellManager.HasSpell("Tidal Waves")
//                                    && !Me.HasAnyAura("Tidal Waves", "Ancestral Swiftness")),
                                    && !Me.ActiveAuras.Any(a => a.Key == "Tidal Waves" || a.Key == "Ancestral Swiftness")),

                            behavs.GenerateBehaviorTree(),

                            // roll Riptide because we can
                            Spell.Cast("Riptide",
                                ret => GetBestRiptideTarget(null))

    #if false                  
                            ,
                            new Sequence(
                                new Action(ret => Logger.WriteDebug(Color.LightGreen, "No Action - stunned:{0} silenced:{1}"
                                    , Me.Stunned || Me.IsStunned() 
                                    , Me.Silenced 
                                    )),
                                new Action(ret => { return RunStatus.Failure; })
                                )
                            ,

                            new Decorator(
                                ret => StyxWoW.Me.Combat && StyxWoW.Me.GotTarget && !Unit.NearbyFriendlyPlayers.Any(u => u.IsInMyPartyOrRaid),
                                new PrioritySelector(
                                    Movement.CreateMoveToLosBehavior(),
                                    Movement.CreateFaceTargetBehavior(),
                                    Helpers.Common.CreateInterruptBehavior(),

                                    Spell.Cast("Earth Shock"),
                                    Spell.Cast("Lightning Bolt"),
                                    Movement.CreateMoveToTargetBehavior(true, 35f)
                                    ))
    #endif
                                )
                            ),

                        new Decorator(
                            ret => moveInRange,
                            Movement.CreateMoveToRangeAndStopBehavior(ret => (WoWUnit)ret, ret => 38f))
                        )
                    )
                );
        }

        private static WoWPlayer GetHealTarget()
        {
            List<WoWPlayer> targets = 
                (from p in Unit.GroupMembers
                where !(p.IsDead || p.IsGhost)
                    && p.IsHorde == p.IsHorde
                    && !p.IsHostile
                    && p.GetPredictedHealthPercent() <= SingularSettings.Instance.IgnoreHealTargetsAboveHealth
                    && ((p.Distance < 40 && p.InLineOfSpellSight) || (MovementManager.IsClassMovementAllowed && p.Distance < 80))
                orderby p.GetPredictedHealthPercent()
                select p)
                .ToList();

            Logger.WriteDebug(" ");
            foreach (WoWPlayer p in targets)
            {
                Logger.WriteDebug(Color.LightGreen, "  HTrg @ {0:F1} yds - {1}-{2} {3:F1}%", p.Distance, p.Class.ToString(), p.SafeName(), p.GetPredictedHealthPercent());
            }

            return targets.FirstOrDefault();
        }

        private static float ChainHealHopRange
        {
            get
            {
                return TalentManager.Glyphs.Contains("Chaining") ? 25f : 12.5f;
            }
        }

        private static IEnumerable<WoWUnit> ChainHealPlayers
        {
            get
            {
                // TODO: Decide if we want to do this differently to ensure we take into account the T12 4pc bonus. (Not removing RT when using CH)
                return Unit.NearbyFriendlyPlayers.Where(u => u.GetPredictedHealthPercent() < ShamanSettings.Heal.ChainHeal).Select(u => (WoWUnit)u);
            }
        }

        private static WoWPlayer GetBestRiptideTarget(WoWPlayer originalTarget)
        {
            if (originalTarget != null && originalTarget.IsValid && originalTarget.IsAlive && !originalTarget.HasMyAura("Riptide") && originalTarget.InLineOfSpellSight)
                return originalTarget;

            // cant RT target, so lets check tanks
            WoWPlayer ripTarget = null;

            if (!SpellManager.HasSpell("Earth Shield"))
            {
                ripTarget = Group.Tanks.Where(u => u.IsAlive && u.DistanceSqr < 40 * 40 && !u.HasMyAura("Riptide") && u.InLineOfSpellSight).OrderBy(u => u.GetPredictedHealthPercent()).FirstOrDefault();
                if (ripTarget != null && (ripTarget.Combat || ripTarget.GetPredictedHealthPercent() <= SingularSettings.Instance.IgnoreHealTargetsAboveHealth))
                    return ripTarget;
            }

            // cant RT target, so lets find someone else to throw it on. Lowest health first preferably for now.
            ripTarget = Unit.GroupMembers.Where(u => u.IsAlive && u.DistanceSqr < 40 * 40 && !u.HasMyAura("Riptide") && u.InLineOfSpellSight).OrderBy(u => u.GetPredictedHealthPercent()).FirstOrDefault();
            if (ripTarget != null && ripTarget.IsAlive && ripTarget.GetPredictedHealthPercent() <= SingularSettings.Instance.IgnoreHealTargetsAboveHealth)
                return ripTarget;

            return null;
        }

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

        private static int HealthToPriority(int nHealth)
        {
            return nHealth == 0 ? 0 : 1000 - nHealth;
        }

        class PrioritizedBehaviorList
        {
            class PrioritizedBehavior
            {
                public int Priority { get; set; }
                public string Name { get; set; }
                public Composite behavior { get; set; }

                public PrioritizedBehavior(int p, string s, Composite bt)
                {
                    Priority = p;
                    Name = s;
                    behavior = bt;
                }
            }

            List<PrioritizedBehavior> blist = new List<PrioritizedBehavior>();

            public void AddBehavior(int pri, string behavName, string spellName, Composite bt)
            {
                if (pri <= 0)
                    Logger.WriteDebug("Skipping Behavior [{0}] configured for Priority {1}", behavName, pri);
                else if (!String.IsNullOrEmpty(spellName) && !SpellManager.HasSpell(spellName))
                    Logger.WriteDebug("Skipping Behavior [{0}] since spell '{1}' is not known by this character", behavName, spellName);
                else
                    blist.Add(new PrioritizedBehavior(pri, behavName, bt));
            }

            public void OrderBehaviors()
            {
                blist = blist.OrderByDescending(b => b.Priority).ToList();
            }

            public Composite GenerateBehaviorTree()
            {
                return new PrioritySelector(blist.Select(b => b.behavior).ToArray());
            }

            public void ListBehaviors()
            {
                foreach (PrioritizedBehavior hs in blist)
                {
                    Logger.WriteDebug(Color.GreenYellow, "   Priority {0} for Behavior [{1}]", hs.Priority, hs.Name);
                }
            }
        }
        
    }
}
