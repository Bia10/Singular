﻿//#define USE_ISFLEEING
#define USE_MECHANIC

using System;
using System.Collections.Generic;
using System.Linq;

using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;

using Styx;

using Styx.CommonBot;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

using Styx.TreeSharp;
using CommonBehaviors.Actions;
using Action = Styx.TreeSharp.Action;
using System.Drawing;
using System.Numerics;
using Styx.Common;
using Styx.CommonBot.Routines;

namespace Singular.ClassSpecific.Shaman
{
    internal static class Totems
    {
        private static LocalPlayer Me { get { return StyxWoW.Me; } }
        private static ShamanSettings ShamanSettings { get { return SingularSettings.Instance.Shaman(); } }

        private static bool ShouldWeDropPullTotems
        {
            get
            {
                if (Me.GotTarget())
                {
                    if (TalentManager.CurrentSpec == WoWSpec.ShamanRestoration && Me.IsInGroup())
                        return false;

                    if (TalentManager.CurrentSpec == WoWSpec.ShamanEnhancement)
                        return Me.CurrentTarget.Distance < Me.MeleeDistance(Me.CurrentTarget) + 10;

                    float distCheck = Totems.GetTotemRange(WoWTotem.Searing);
                    if (Me.CurrentTarget.IsMoving)
                        distCheck -= 3.5f;

                    if (Me.SpellDistance(Me.CurrentTarget) <= distCheck  )
                        return true;
                }

                return false;
            }
        }

        public static bool AllowDpsTotems
        {
            get
            {
                return SingularRoutine.IsAllowed(Styx.CommonBot.Routines.CapabilityFlags.PetUse)
                    && AllowTotems;
            }
        }

        public static bool AllowTotems
        {
            get
            {
                return SingularRoutine.IsAllowed(Styx.CommonBot.Routines.CapabilityFlags.PetSummoning);
            }
        }
        public static bool AllowElementalTotems
        {
            get
            {
                return !Common.talentPrimalElementalist || !PetManager.IsPetSummonAllowed;
            }
        }

        private static bool ShouldWeDropTotemsYet
        {
            get
            {
                if ( !Me.Combat )
                    return false;

                if ( TalentManager.CurrentSpec == WoWSpec.ShamanEnhancement )
                    return Me.GotTarget() && Me.CurrentTarget.Distance < Me.MeleeDistance(Me.CurrentTarget) + 10;

                if (SingularRoutine.CurrentWoWContext == WoWContext.Instances)
                {
                    if ( !Me.GotTarget() )
                        return false;

                    if ( Me.CurrentTarget.IsMoving )
                        return false;

                    if (Me.SpellDistance(Me.CurrentTarget) > Totems.GetTotemRange(WoWTotem.Searing))
                        return false;

                    return true;
                }

                return !Me.GotTarget() || Me.CurrentTarget.SpellDistance() < 40;
            }
        }

        public static Composite CreateTotemsBehavior()
        {
            if (SingularRoutine.CurrentWoWContext == WoWContext.Normal)
            {
                if (Dynamics.CompositeBuilder.CurrentBehaviorType == BehaviorType.Pull)
                    return CreateTotemsNormalPullBehavior();

                return CreateTotemsNormalBehavior();
            }

            if (SingularRoutine.CurrentWoWContext == WoWContext.Battlegrounds )
                return CreateTotemsPvPBehavior();

            return CreateTotemsInstanceBehavior();
        }

        public static Composite CreateTotemsNormalPullBehavior()
        {
            if (Dynamics.CompositeBuilder.CurrentBehaviorType != BehaviorType.Pull)
                return new ActionAlwaysFail();

            return new PrioritySelector(
                // check in case more sophisticated totem cast needed
                CreateTotemsNormalBehavior(),

                // otherwise drop the DPS totem
                Spell.BuffSelf("Searing Totem", ret => ShouldWeDropPullTotems && AllowDpsTotems && !Exist(WoWTotemType.Fire))
                );

        }

        public static Composite CreateTotemsNormalBehavior()
        {
            // create Fire Totems behavior first, then wrap if needed
            PrioritySelector fireTotemBehavior = new PrioritySelector();
            fireTotemBehavior.AddChild(
                Spell.Buff(WoWTotem.FireElemental.ToSpellId(),
                    req => Common.StressfulSituation
                        && Totems.AllowElementalTotems
                        && !Exist(WoWTotem.EarthElemental)
                        && !Spell.CanCastHack("Earth Elemental Totem")

                    )
                );

            if (TalentManager.CurrentSpec == WoWSpec.ShamanEnhancement)
            {
                fireTotemBehavior.AddChild(
                    Spell.Cast("Magma Totem", on => Me.CurrentTarget ?? Me, ret => IsMagmaTotemNeeded())
                    );
            }

            fireTotemBehavior.AddChild(
                Spell.BuffSelf("Searing Totem", ret => IsSearingTotemNeeded() )
                );

            if (TalentManager.CurrentSpec == WoWSpec.ShamanRestoration)
            {
                fireTotemBehavior = new PrioritySelector(
                    new Decorator(
                        ret => StyxWoW.Me.Combat && StyxWoW.Me.GotTarget() && !Unit.NearbyGroupMembers.Any(),
                        fireTotemBehavior
                        )
                    );
            }

            // now
            return new PrioritySelector(

                new Throttle(1,
                    new Action(r =>
                    {
                        bool ccMechanic = Me.HasAuraWithMechanic(WoWSpellMechanic.Fleeing | WoWSpellMechanic.Polymorphed | WoWSpellMechanic.Asleep);
                        bool ccEffect = Me.HasAuraWithEffect(WoWApplyAuraType.ModFear | WoWApplyAuraType.ModPacify | WoWApplyAuraType.ModPacifySilence);
                        bool ccAttrib = Me.Fleeing;
                        if (ccMechanic || ccEffect || ccAttrib)
                            Logger.WriteDebug(Color.Pink, "... FEAR CHECKED OUT --  Mechanic={0}  Effect={1}  Attrib={2}", ccMechanic, ccEffect, ccAttrib);
                        return RunStatus.Failure;
                    })
                    ),


                Spell.BuffSelf(WoWTotem.Tremor.ToSpellId(),
                    ret => Unit.GroupMembers.Any(f => f.Fleeing && f.Distance < Totems.GetTotemRange(WoWTotem.Tremor))
                        && !Exist(WoWTotem.StoneBulwark, WoWTotem.EarthElemental)),

                new Decorator(
                    req => PetManager.NeedsPetSupport
                        && Totems.ExistInRange(Me.Location, WoWTotem.EarthElemental)
                        && !Me.HasAura("Reinforce")
                        && !Spell.DoubleCastContains(Me, "Reinforce"),
                    new Sequence(
                        PetManager.CastAction("Reinforce", on => Me),
                        new Action(r => Spell.UpdateDoubleCast("Reinforce", Me))
                        )
                    ),

                new Decorator(
                    req => PetManager.NeedsPetSupport
                        && Totems.ExistInRange(Me.Location, WoWTotem.FireElemental)
                        && !Me.HasAura("Empower")
                        && !Spell.DoubleCastContains(Me, "Empower"),
                    new Sequence(
                        PetManager.CastAction("Empower", on => Me),
                        new Action(r => Spell.UpdateDoubleCast("Empower", Me))
                        )
                    ),

                new Decorator(
                    ret => ShouldWeDropTotemsYet,

                    new PrioritySelector(

                        // check for stress - enemy player or elite within 8 levels nearby
                // .. dont use NearbyUnitsInCombatWithMe since it checks .Tagged and we only care if we are being attacked
                        ctx => Common.StressfulSituation,

                        // earth totems
                        Spell.BuffSelf(WoWTotem.EarthElemental.ToSpellId(),
                            ret => ((bool)ret || Group.Tanks.Any(t => t.IsDead && t.Distance < 40))
                                && Totems.AllowElementalTotems
                                && !Exist(WoWTotem.StoneBulwark)
                            ),

                        Spell.BuffSelf(WoWTotem.StoneBulwark.ToSpellId(),
                            ret => Me.Combat && Me.HealthPercent < ShamanSettings.StoneBulwarkTotemPercent && !Exist(WoWTotem.EarthElemental)),

                        new PrioritySelector(
                            ctx => Unit.NearbyUnfriendlyUnits.Any(u => u.IsTargetingMeOrPet && u.IsPlayer && u.Combat),

                            Spell.BuffSelf(WoWTotem.Earthgrab.ToSpellId(),
                                ret => (bool)ret && !Exist(WoWTotemType.Earth)),

                            Spell.BuffSelf(WoWTotem.Earthbind.ToSpellId(),
                                ret => (bool)ret && !Exist(WoWTotemType.Earth))
                            ),

                        // fire totems
                        fireTotemBehavior,

                        // water totems
                        Spell.BuffSelf("Mana Tide Totem",
                            ret =>
                            {
                                if (TalentManager.CurrentSpec != WoWSpec.ShamanRestoration)
                                    return false;

                                // Logger.WriteDebug("Mana Tide Totem Check:  current mana {0:F1}%", Me.ManaPercent);
                                if (Me.ManaPercent > ShamanSettings.ManaTideTotemPercent )
                                    return false;
                                if (Exist(WoWTotem.HealingTide, WoWTotem.HealingStream))
                                    return false;
                                return true;
                            }),

        /* Healing...: handle within Helaing logic
                        Spell.Cast("Healing Tide Totem", ret => ((bool)ret) && StyxWoW.Me.HealthPercent < 50
                            && !Exist(WoWTotem.ManaTide)),

                        Spell.Cast("Healing Stream Totem", ret => ((bool)ret) && StyxWoW.Me.HealthPercent < 80
                            && !Exist( WoWTotemType.Water)),
        */

                        // air totems
                        Spell.Cast("Stormlash Totem", ret => PartyBuff.WeHaveBloodlust && !Me.HasAura("Stormlash Totem")),

                        new Decorator(
                            ret => !Exist(WoWTotemType.Air),
                            new PrioritySelector(
                                Spell.Cast("Grounding Totem",
                                    ret => Unit.NearbyUnfriendlyUnits.Any(u => u.SpellDistance() < 40 && u.IsTargetingMeOrPet && u.IsCasting)),

                                Spell.Cast("Capacitor Totem",
                                    ret => ((bool)ret)
                                        && Unit.NearbyUnfriendlyUnits.Any(u => u.SpellDistance() < GetTotemRange(WoWTotem.Capacitor))),

                                Spell.BuffSelf("Windwalk Totem",
                                    ret => Unit.HasAuraWithMechanic(StyxWoW.Me, WoWSpellMechanic.Rooted, WoWSpellMechanic.Snared))
                                )
                            )
                        )
                    )
                );

        }

        public static Composite CreateTotemsPvPBehavior()
        {
            return CreateTotemsNormalBehavior();
        }

        public static Composite CreateTotemsInstanceBehavior()
        {
            // create Fire Totems behavior first, then wrap if needed
            PrioritySelector fireTotemBehavior = new PrioritySelector();

            fireTotemBehavior.AddChild(
                    Spell.Buff(
                       "Fire Elemental Totem",
                       req => Me.CurrentTarget.IsBoss()
                           && AllowElementalTotems
                       )
                    );

            if (TalentManager.CurrentSpec == WoWSpec.ShamanEnhancement)
            {
                fireTotemBehavior.AddChild(
                    Spell.Cast("Magma Totem", on => Me.CurrentTarget ?? Me, ret => IsMagmaTotemNeeded())
                    );
            }

            if (TalentManager.CurrentSpec == WoWSpec.ShamanRestoration)
                fireTotemBehavior = new PrioritySelector(
                    new Decorator(
                        ret => StyxWoW.Me.Combat && StyxWoW.Me.GotTarget() && !HealerManager.Instance.TargetList.Any(m => m.IsAlive),
                        fireTotemBehavior
                        )
                    );
            else
                fireTotemBehavior.AddChild(
                    Spell.Cast("Searing Totem", ret => IsSearingTotemNeeded())
                    );


            // now
            return new PrioritySelector(

                Spell.BuffSelf(WoWTotem.Tremor.ToSpellId(),
                    ret => Unit.GroupMembers.Any(f => f.Fleeing && f.Distance < Totems.GetTotemRange(WoWTotem.Tremor))
                        && !Exist(WoWTotem.StoneBulwark, WoWTotem.EarthElemental)),

                new Decorator(
                    req => Totems.ExistInRange(Me.Location, WoWTotem.EarthElemental)
                        && !Me.HasAura("Reinforce")
                        && !Spell.DoubleCastContains(Me, "Reinforce"),
                    new Sequence(
                        PetManager.CastAction("Reinforce", on => Me),
                        new Action(r => Spell.UpdateDoubleCast("Reinforce", Me))
                        )
                    ),

                new Decorator(
                    req => Totems.ExistInRange(Me.Location, WoWTotem.FireElemental)
                        && !Me.HasAura("Empower")
                        && !Spell.DoubleCastContains(Me, "Empower"),
                    new Sequence(
                        PetManager.CastAction("Empower", on => Me),
                        new Action(r => Spell.UpdateDoubleCast("Empower", Me))
                        )
                    ),

                new Decorator(
                    ret => ShouldWeDropTotemsYet,

                    new PrioritySelector(

                        // earth totems
                        Spell.Cast(WoWTotem.EarthElemental.ToSpellId(),
                            on => Me.CurrentTarget ?? Me,
                            req => {
                                if (!Totems.AllowElementalTotems)
                                    return false;

                                if (Exist(WoWTotem.StoneBulwark))
                                    return false;

                                if (Spell.IsSpellOnCooldown("Earth Elemental Totem"))
                                    return false;

                                // no living tanks in range
                                IEnumerable<WoWUnit> tanks = Group.Tanks.Where(u => u.DistanceSqr < 65 * 65);
                                if (!tanks.Any(t => t.IsAlive) || tanks.Any( t => t.IsDead))
                                {
                                    // we are okay if another Earth Elemental active
                                    if ( ObjectManager.GetObjectsOfType<WoWUnit>(false, false).Any( o => o.Entry == 15352))
                                        return false;

                                    // we are okay if nothing in combat with our group
                                    if (!Unit.NearbyUnitsInCombatWithUsOrOurStuff.Any())
                                        return false;
                                }

                                // check we can cast it before messaging
                                if (!Spell.CanCastHack("Earth Elemental Totem"))
                                    return false;

                                Logger.Write(LogColor.Hilite, "^Earth Elemental Totem: tank is dead or not nearby");
                                return true;
                            }),

                        // Stone Bulwark handled in CombatBuffs with Astral Shift

                        // fire totems
                        fireTotemBehavior,

                        // water totems
                        Spell.BuffSelf("Mana Tide Totem",
                            ret =>
                            {
                                if (TalentManager.CurrentSpec != WoWSpec.ShamanRestoration)
                                    return false;

                                // Logger.WriteDebug("Mana Tide Totem Check:  current mana {0:F1}%", Me.ManaPercent);
                                if (Me.ManaPercent > ShamanSettings.ManaTideTotemPercent)
                                    return false;
                                if (Exist(WoWTotem.HealingTide, WoWTotem.HealingStream))
                                    return false;
                                return true;
                            }),


        /* Healing...: handle within Helaing logic
                        Spell.Cast("Healing Tide Totem", ret => ((bool)ret) && StyxWoW.Me.HealthPercent < 50
                            && !Exist(WoWTotem.ManaTide)),

                        Spell.Cast("Healing Stream Totem", ret => ((bool)ret) && StyxWoW.Me.HealthPercent < 80
                            && !Exist( WoWTotemType.Water)),
        */

                        // air totems
                        Spell.Cast("Stormlash Totem", ret => PartyBuff.WeHaveBloodlust && !Me.HasAura("Stormlash Totem")),

                        new Decorator(
                            ret => !Exist(WoWTotemType.Air),
                            new PrioritySelector(
                                Spell.Cast("Grounding Totem",
                                    on => Unit.NearbyUnfriendlyUnits.FirstOrDefault(u => u.SpellDistance() < 40 && u.IsTargetingMeOrPet && u.IsCasting)),

                                Spell.Cast("Capacitor Totem",
                                    on => Unit.NearbyUnfriendlyUnits.FirstOrDefault(u => u.SpellDistance() < GetTotemRange(WoWTotem.Capacitor) && u.IsTargetingMeOrPet)),

                                Spell.BuffSelf("Windwalk Totem",
                                    ret => Unit.HasAuraWithMechanic(StyxWoW.Me, WoWSpellMechanic.Rooted, WoWSpellMechanic.Snared))
                                )
                            )
                        )
                    )
                );

        }

        private static bool IsSearingTotemNeeded()
        {
            if (Me.GotTarget() && TotemIsKnown(WoWTotem.Searing) && AllowDpsTotems)
            {
                WoWTotemInfo ti = GetTotem(WoWTotemType.Fire);
                if (!Exist(ti))
                {
                    Logger.WriteDebug("IsSearingTotemNeeded: no Fire Totem so setting Searing Totem");
                    return true;
                }

                if (ti.WoWTotem == WoWTotem.Searing)
                {
                    /*
                    if (ti.Unit != null && ti.Expires < (DateTime.UtcNow + TimeSpan.FromSeconds(2)))
                    {
                        Logger.WriteDebug("IsSearingTotemNeeded: Searing Totem expired! expires={0}, current={1}, refreshing", ti.Expires.ToString("MM/dd/yyyy hh:mm:ss.fff"), DateTime.UtcNow.ToString("MM/dd/yyyy hh:mm:ss.fff"));
                        return true;
                    }
                    */
                    float currDist = ti.Unit.SpellDistance(Me.CurrentTarget);
                    if (currDist > GetTotemRange(WoWTotem.Searing))
                    {
                        Logger.WriteDebug("IsSearingTotemNeeded: Searing Totem is {0:F1} yds from CurrentTarget, refreshing", currDist);
                        return true;
                    }
                }

                if (ti.WoWTotem == WoWTotem.Magma)
                {
                    int count = Unit.NearbyUnitsInCombatWithUsOrOurStuff.Count(u => u.DistanceSqr < 15 * 15);
                    if (count < 3)
                    {
                        Logger.WriteDebug("IsSearingTotemNeeded: only {0} mobs nearby, replacing Magma Totem with Searing Totem", count);
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsMagmaTotemNeeded()
        {
            if (Spell.UseAOE && Me.GotTarget() && TotemIsKnown(WoWTotem.Magma) && AllowDpsTotems)
            {
                WoWTotemInfo ti = GetTotem(WoWTotemType.Fire);

                if (ti.WoWTotem == WoWTotem.FireElemental)
                    return false;

                // for existing magma return no cast needed if it has enough mobs
                float rangeCheck = rangeCheck = Totems.GetTotemRange(WoWTotem.Magma);
                if (ti.WoWTotem == WoWTotem.Magma)
                {
                    int existcount = Unit.NearbyUnitsInCombatWithUsOrOurStuff.Count(u => ti.Unit.SpellDistance(u) < rangeCheck);
                    if (existcount >= 3)
                        return false;
                }

                // so no good magma in range, so now check if we want one to be
                int nearcount = Unit.NearbyUnitsInCombatWithUsOrOurStuff.Count(u => u.SpellDistance() < rangeCheck);
                if (nearcount < 3)
                    return false;

                if (ti.WoWTotem == WoWTotem.Magma)
                    Logger.WriteDebug("IsMagmaTotemNeeded: existing Magma out of range, resetting since {0} mobs near me", nearcount);
                else
                    Logger.WriteDebug("IsMagmaTotemNeeded: found {0} mobs near me, setting Magma", nearcount);

                return true;
            }

            return false;
        }

        /// <summary>
        ///   Recalls any currently 'out' totems. This will use Totemic Recall if its known, otherwise it will destroy each totem one by one.
        /// </summary>
        /// <remarks>
        ///   Created 3/26/2011.
        /// </remarks>
        public static Composite CreateRecallTotems()
        {
            return new Throttle( 2, new Action(r => RecallTotems() ? RunStatus.Success : RunStatus.Failure));
        }

        public static bool RecallTotems()
        {
            if (Totems.NeedToRecallTotems)
            {
                if (SpellManager.HasSpell("Totemic Recall"))
                {
                    Spell.LogCast("Totemic Recall", Me);
                    return Spell.CastPrimative("Totemic Recall");
                }

                List<WoWTotemInfo> totems = StyxWoW.Me.Totems;
                foreach (WoWTotemInfo t in totems)
                {
                    if (t != null && t.Unit != null)
                    {
                        DestroyTotem(t.Type);
                    }
                }

                return true;
            }

            return false;
        }

        /// <summary>
        ///   Destroys the totem described by type.
        /// </summary>
        /// <remarks>
        ///   Created 3/26/2011.
        /// </remarks>
        /// <param name = "type">The type.</param>
        public static void DestroyTotem(WoWTotemType type)
        {
            if (type == WoWTotemType.None)
            {
                return;
            }

            Logger.Write("^Destroy Totem Type: {0}", type.ToString());
            Lua.DoString("DestroyTotem({0})", (int)type);
        }


        private static readonly Dictionary<WoWTotemType, WoWTotem> LastSetTotems = new Dictionary<WoWTotemType, WoWTotem>();


        #region Helper stuff

        public static bool NeedToRecallTotems
        {
            get
            {
                if (!Common.HasTalent(ShamanTalents.TotemMastery))
                    return false; // Having Totem Mastery is the only situation in which we'd need to recall totems.

                int cntTotemsInRange = 0;
                int cntTotems = 0;
                int cntTotemsAggro = 0;
                bool aggroAllowed = SingularRoutine.IsAllowed(Styx.CommonBot.Routines.CapabilityFlags.PetUse);

                foreach (var totem in Me.Totems)
                {
                    if ( totem.Unit == null)
                        continue;

                    cntTotems++;
                    if (!aggroAllowed && totemsThatAggro.Contains(totem.WoWTotem))
                    {
                        Logger.Write(LogColor.Hilite, "^Recalling Totems! {0} would aggro", totem.WoWTotem);
                        cntTotemsAggro++;
                    }

                    if (Me.Location.Distance(totem.Unit.Location) < GetTotemRange(totem.WoWTotem))
                        cntTotemsInRange++;
                }

                bool wantToRecall = false;
                if (!aggroAllowed && cntTotemsAggro > 0)
                {
                    wantToRecall = true;
                }
                else if (cntTotems > 0 && cntTotemsInRange == 0)
                {
                    wantToRecall = !Unit.NearbyFriendlyPlayers.Any(f => f.Combat);
                    if (wantToRecall)
                    {
                        Logger.Write(LogColor.Hilite, "^Recalling Totems! (out of range)");
                    }
                }

                return wantToRecall;
            }
        }

        public static bool TotemIsKnown(WoWTotem totem)
        {
            return SpellManager.HasSpell(totem.ToSpellId());
        }


        #region Totem Existance

        public static bool IsRealTotem(WoWTotem ti)
        {
            return ti != WoWTotem.None
                && ti != WoWTotem.DummyAir
                && ti != WoWTotem.DummyEarth
                && ti != WoWTotem.DummyFire
                && ti != WoWTotem.DummyWater;
        }

        /// <summary>
        /// check if a specific totem (ie Mana Tide Totem) exists
        /// </summary>
        /// <param name="wtcheck"></param>
        /// <returns></returns>
        public static bool Exist(WoWTotem wtcheck)
        {
            WoWTotemInfo tiexist = GetTotem(wtcheck);
            return tiexist != null;
        }

        /// <summary>
        /// check if a WoWTotemInfo object references a real totem (other than None or Dummies)
        /// </summary>
        /// <param name="ti"></param>
        /// <returns></returns>
        public static bool Exist(WoWTotemInfo ti)
        {
            return IsRealTotem(ti.WoWTotem);
        }

        /// <summary>
        /// check if a type of totem (ie Air Totem) exists
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static bool Exist(WoWTotemType type)
        {
            WoWTotem wt = GetTotem(type).WoWTotem;
            return IsRealTotem(wt);
        }

        /// <summary>
        /// check if any of several specific totems exist
        /// </summary>
        /// <param name="wt"></param>
        /// <returns></returns>
        public static bool Exist(params WoWTotem[] wt)
        {
            return wt.Any(t => Exist(t));
        }

        /// <summary>
        /// check if a specific totem exists within its max range of a given location
        /// </summary>
        /// <param name="pt"></param>
        /// <param name="tt"></param>
        /// <returns></returns>
        public static bool ExistInRange(Vector3 pt, WoWTotem tt)
        {
            if ( !Exist(tt))
                return false;

            WoWTotemInfo ti = GetTotem(tt);
            return ti.Unit != null && ti.Unit.Location.Distance(pt) < GetTotemRange(tt);
        }

        /// <summary>
        /// check if any of several totems exist in range
        /// </summary>
        /// <param name="pt"></param>
        /// <param name="awt"></param>
        /// <returns></returns>
        public static bool ExistInRange(Vector3 pt, params WoWTotem[] awt)
        {
            return awt.Any(t => ExistInRange(pt, t));
        }

        /// <summary>
        /// check if type of totem (ie Air Totem) exists in range
        /// </summary>
        /// <param name="pt"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static bool ExistInRange(Vector3 pt, WoWTotemType type)
        {
            WoWTotemInfo ti = GetTotem(type);
            return Exist(ti) && ti.Unit != null && ti.Unit.Location.Distance(pt) < GetTotemRange(ti.WoWTotem);
        }

        #endregion

        /// <summary>
        /// gets reference to array element in Me.Totems[] corresponding to WoWTotemType of wt.  Return is always non-null and does not indicate totem existance
        /// </summary>
        /// <param name="wt">WoWTotem of slot to reference</param>
        /// <returns>WoWTotemInfo reference</returns>
        public static WoWTotemInfo GetTotem(WoWTotem wt)
        {
            WoWTotemInfo ti = GetTotem(wt.ToType());
            if (ti.WoWTotem != wt)
                return null;
            return ti;
        }

        /// <summary>
        /// gets reference to array element in Me.Totems[] corresponding to type.  Return is always non-null and does not indicate totem existance
        /// </summary>
        /// <param name="type">WoWTotemType of slot to reference</param>
        /// <returns>WoWTotemInfo reference</returns>
        public static WoWTotemInfo GetTotem(WoWTotemType type)
        {
            return Me.Totems[(int)type - 1];
        }

        /// <summary>
        /// check if all totems are within range of Shaman's location
        /// </summary>
        public static int TotemsInRange
        {
            get
            {
                return TotemsInRangeOf(StyxWoW.Me);
            }
        }

        /// <summary>
        /// check if all totems are within range of a WoWUnit
        /// </summary>
        /// <param name="unit"></param>
        /// <returns></returns>
        public static int TotemsInRangeOf(WoWUnit unit)
        {
            return StyxWoW.Me.Totems.Where(t => t.Unit != null).Count(t => unit.Location.Distance(t.Unit.Location) < GetTotemRange(t.WoWTotem));
        }

        /// <summary>
        ///   Finds the max range of a specific totem, where you'll still receive the buff.
        /// </summary>
        /// <remarks>
        ///   Created 3/26/2011.
        /// </remarks>
        /// <param name = "totem">The totem.</param>
        /// <returns>The calculated totem range.</returns>
        public static float GetTotemRange(WoWTotem totem)
        {
            switch (totem)
            {
                case WoWTotem.Tremor:
                    return 30f;

                case WoWTotem.Searing:
                    if (TalentManager.CurrentSpec == WoWSpec.ShamanElemental )
                        return 35f;
                    return 20f;

                case WoWTotem.Earthbind:
                    return 10f;

                case WoWTotem.Grounding:
                case WoWTotem.Magma:
                    return 8f;

                case WoWTotem.EarthElemental:
                case WoWTotem.FireElemental:
                    // Not really sure about these 3.
                    return 20f;

                case WoWTotem.ManaTide:
                    // Again... not sure :S
                    return 40f;


                case WoWTotem.Earthgrab:
                    return 10f;

                case WoWTotem.StoneBulwark:
                    // No idea, unlike former glyphed stoneclaw it has a 5 sec pluse shield component so range is more important
                    return 40f;

                case WoWTotem.HealingStream:
                    return 40f;

                case WoWTotem.HealingTide:
                    return 40f;

                case WoWTotem.Cloudburst:
                    return 40f;

                case WoWTotem.Capacitor:
                    return 8f;

                case WoWTotem.Windwalk:
                    return 40f;

                case WoWTotem.SpiritLink:
                    return 40f;
            }

            return 0f;
        }


        public static int ToSpellId(this WoWTotem totem)
        {
            return (int)(((long)totem) & ((1L << 32) - 1));
        }

        public static WoWTotemType ToType(this WoWTotem totem)
        {
            return (WoWTotemType)((long)totem >> 32);
        }


        static WoWTotem[] totemsWeDontRecall = new WoWTotem[]
        {
            WoWTotem.FireElemental ,
            WoWTotem.EarthElemental  ,
            WoWTotem.HealingTide ,
            WoWTotem.ManaTide
        };

        static WoWTotem[] totemsThatAggro = new WoWTotem[]
        {
            WoWTotem.FireElemental,
            WoWTotem.Searing,
            WoWTotem.Magma,
            // DummyFire,
            WoWTotem.EarthElemental,
            WoWTotem.Earthbind,
            // Tremor,
            WoWTotem.Earthgrab,
            // StoneBulwark,
            // DummyEarth,
            // HealingStream,
            // ManaTide,
            // HealingTide,
            // DummyWater,
            // Grounding,
            // SpiritLink,
            WoWTotem.Capacitor,
            // Windwalk,
            // DummyAir,
            // Stormlash
        };

        #endregion

    }

}