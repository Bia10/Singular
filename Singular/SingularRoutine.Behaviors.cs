﻿//#define SHOW_BEHAVIOR_LOAD_DESCRIPTION
#define BOTS_NOT_CALLING_PULLBUFFS

using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;
using Styx;
using Styx.TreeSharp;
using Singular.ClassSpecific;
using System.Drawing;
using CommonBehaviors.Actions;
using Styx.Common;
using System;

using Action = Styx.TreeSharp.Action;
using Styx.CommonBot;
using Styx.WoWInternals.WoWObjects;
using Styx.Helpers;

namespace Singular
{
    partial class SingularRoutine
    {
        private Composite _combatBehavior;
        private Composite _combatBuffsBehavior;
        private Composite _healBehavior;
        private Composite _preCombatBuffsBehavior;
        private Composite _pullBehavior;
        private Composite _pullBuffsBehavior;
        private Composite _restBehavior;
        private Composite _lostControlBehavior;
        public override Composite CombatBehavior { get { return _combatBehavior; } }
        public override Composite CombatBuffBehavior { get { return _combatBuffsBehavior; } }
        public override Composite HealBehavior { get { return _healBehavior; } }
        public override Composite PreCombatBuffBehavior { get { return _preCombatBuffsBehavior; } }
        public override Composite PullBehavior { get { return _pullBehavior; } }
        public override Composite PullBuffBehavior { get { return _pullBuffsBehavior; } }
        public override Composite RestBehavior { get { return _restBehavior; } }

        public bool RebuildBehaviors(bool silent = false)
        {
            Logger.PrintStackTrace("RebuildBehaviors called.");

            InitBehaviors();

            // save single consistent copy for building behaves since CurrentWoWContext is dynamically evaluated
            WoWContext context = CurrentWoWContext;

            // DO NOT UPDATE: This will cause a recursive event
            // Update the current context. Handled in SingularRoutine.Context.cs
            //UpdateContext();

            // special behavior - reset KitingBehavior hook prior to calling class specific createion
            TreeHooks.Instance.ReplaceHook("KitingBehavior", new ActionAlwaysFail() );

            // If these fail, then the bot will be stopped. We want to make sure combat/pull ARE implemented for each class.
            if (!EnsureComposite(true, context, BehaviorType.Combat))
            {
                return false;
            }

            if (!EnsureComposite(true, context, BehaviorType.Pull))
            {
                return false;
            }

            // If there's no class-specific resting, just use the default, which just eats/drinks when low.
            EnsureComposite(false, context, BehaviorType.Rest);
            if ( TreeHooks.Instance.Hooks[BehaviorType.Rest.ToString()] == null)
                TreeHooks.Instance.ReplaceHook( BehaviorType.Rest.ToString(), Helpers.Rest.CreateDefaultRestBehaviour());


            // These are optional. If they're not implemented, we shouldn't stop because of it.
            EnsureComposite(false, context, BehaviorType.CombatBuffs);
            EnsureComposite(false, context, BehaviorType.Heal);
            EnsureComposite(false, context, BehaviorType.PullBuffs);
            EnsureComposite(false, context, BehaviorType.PreCombatBuffs);

            EnsureComposite(false, context, BehaviorType.LossOfControl);

#if SHOW_BEHAVIOR_LOAD_DESCRIPTION
            // display concise single line describing what behaviors we are loading
            if (!silent)
            {
                string sMsg = "";
                if (_healBehavior != null)
                    sMsg += (!string.IsNullOrEmpty(sMsg) ? "," : "") + " Heal";
                if (_pullBuffsBehavior != null)
                    sMsg += (!string.IsNullOrEmpty(sMsg) ? "," : "") + " PullBuffs";
                if (_pullBehavior != null)
                    sMsg += (!string.IsNullOrEmpty(sMsg) ? "," : "") + " Pull";
                if (_preCombatBuffsBehavior != null)
                    sMsg += (!string.IsNullOrEmpty(sMsg) ? "," : "") + " PreCombatBuffs";
                if (_combatBuffsBehavior != null)
                    sMsg += (!string.IsNullOrEmpty(sMsg) ? "," : "") + " CombatBuffs";
                if (_combatBehavior != null)
                    sMsg += (!string.IsNullOrEmpty(sMsg) ? "," : "") + " Combat";
                if (_restBehavior != null)
                    sMsg += (!string.IsNullOrEmpty(sMsg) ? "," : "") + " Rest";

                Logger.Write(Color.LightGreen, "Loaded{0} behaviors for {1}: {2}", Me.Specialization.ToString().CamelToSpaced(), context.ToString(), sMsg);
            }
#endif
            return true;
        }

        /// <summary>
        /// initialize all base behaviors.  replaceable portion which will vary by context is represented by a single
        /// HookExecutor that gets assigned elsewhere (typically EnsureComposite())
        /// </summary>
        private void InitBehaviors()
        {
            // we only do this one time
            if (_restBehavior != null)
                return;

            // note regarding behavior intros....
            // WAIT: Rest and PreCombatBuffs should wait on gcd/cast in progress (return RunStatus.Success)
            // SKIP: PullBuffs, CombatBuffs, and Heal should fall through if gcd/cast in progress (wrap in decorator)
            // HANDLE: Pull and Combat should wait or skip as needed in class specific manner required

            // loss of control behavior must be defined prior to any embedded references by other behaviors
            _lostControlBehavior = new Decorator(
                ret => Me.Fleeing || Me.Stunned,
                new PrioritySelector(
                    new Throttle(1, new Decorator(ret => Me.Fleeing, new Action(r => Logger.Write(Color.White, "FLEEING! (loss of control)")))),
                    new Throttle(1, new Decorator(ret => Me.Stunned, new Action(r => Logger.Write(Color.White, "STUNNED! (loss of control)")))),
                    new Throttle(1, new Decorator(ret => Me.Silenced, new Action(r => Logger.Write(Color.White, "SILENCED! (loss of control)")))),
                    new HookExecutor(BehaviorType.LossOfControl.ToString()),
                    new Decorator( 
                        ret => SingularSettings.Instance.UseRacials,
                        new PrioritySelector(
                            Spell.Cast( "Will of the Forsaken", on => Me, ret => Me.Race == WoWRace.Undead && Me.Fleeing ),
                            Spell.Cast( "Every Man for Himself", on => Me, ret => Me.Race == WoWRace.Human && (Me.Stunned || Me.Fleeing ))
                            )
                        ),
                    new ActionAlwaysSucceed()
                    )
                );

            _restBehavior = new Decorator(
                ret => AllowBehaviorUsage() && !SingularSettings.Instance.DisableNonCombatBehaviors,
                new LockSelector(
                    new Action(r => { _guidLastTarget = 0; return RunStatus.Failure; }),
                    Spell.WaitForGcdOrCastOrChannel(),
                    _lostControlBehavior,
                    new HookExecutor(BehaviorType.Rest.ToString())
                    )
                );

            _preCombatBuffsBehavior = new Decorator(
                ret => AllowBehaviorUsage() && !SingularSettings.Instance.DisableNonCombatBehaviors,
                new LockSelector(
                    Spell.WaitForGcdOrCastOrChannel(),
                    Item.CreateUseAlchemyBuffsBehavior(),
                    // Generic.CreateFlasksBehaviour(),
                    new HookExecutor(BehaviorType.PreCombatBuffs.ToString())
                    )
                );

            _pullBuffsBehavior = new LockSelector(
                new Decorator(
                    ret => !Spell.IsGlobalCooldown() && !Spell.IsCastingOrChannelling(),
                    new HookExecutor(BehaviorType.PullBuffs.ToString())
                    )
                );

            _combatBuffsBehavior = new PrioritySelector(
                new Decorator(
                    ret => AllowBehaviorUsage() && !Spell.IsGlobalCooldown() && !Spell.IsCastingOrChannelling(),
                    new LockSelector(
                        new Decorator(ret => !HotkeyDirector.IsCombatEnabled, new ActionAlwaysSucceed()),
                        Generic.CreateUseTrinketsBehaviour(),
                        Generic.CreatePotionAndHealthstoneBehavior(),
                        Generic.CreateRacialBehaviour(),
                        new HookExecutor( BehaviorType.CombatBuffs.ToString())
                        )
                    )
                );

            _healBehavior = new LockSelector(
                _lostControlBehavior,
                new Decorator(
                    ret => Kite.IsKitingActive(),
                    new HookExecutor("KitingBehavior")
                    ),
                new Decorator(
                    ret => !Spell.IsGlobalCooldown() && !Spell.IsCastingOrChannelling(),
                    new HookExecutor(BehaviorType.Heal.ToString())
                    )
                );

            _pullBehavior = new Decorator(
                ret => !Me.GotTarget || !Blacklist.Contains(Me.CurrentTargetGuid, BlacklistFlags.Combat ),
                new LockSelector(
                    new Decorator(
                        ret => !HotkeyDirector.IsCombatEnabled,
                        new ActionAlwaysSucceed()
                        ),
                    new Action( r => { MonitorQuestingPullDistance(); return RunStatus.Failure; } ),
    #if BOTS_NOT_CALLING_PULLBUFFS
                    _pullBuffsBehavior,
    #endif
                    CreateLogTargetChanges("<<< PULL >>>"),
                    new HookExecutor(BehaviorType.Pull.ToString())
                    )
                );

            _combatBehavior = new Decorator(
                ret => !Me.GotTarget || !Blacklist.Contains(Me.CurrentTargetGuid, BlacklistFlags.Combat),
                new LockSelector(
                    new Decorator(
                        ret => !HotkeyDirector.IsCombatEnabled,
                        new ActionAlwaysSucceed()
                        ),
                    CreateLogTargetChanges("<<< ADD >>>"),
                    new HookExecutor(BehaviorType.Combat.ToString())
                    )
                );
        }

        private static bool AllowBehaviorUsage()
        {
            return !IsMounted && (!Me.IsOnTransport || Me.Transport.Entry == 56171);
        }

        /// <summary>
        /// Ensures we have a composite for the given BehaviorType.  
        /// </summary>
        /// <param name="error">true: report error if composite not found, false: allow null composite</param>
        /// <param name="type">BehaviorType that should be loaded</param>
        /// <returns>true: composite loaded and saved to hook, false: failure</returns>
        private bool EnsureComposite(bool error, WoWContext context, BehaviorType type)
        {
            int count = 0;
            Composite composite;

            Logger.WriteDebug("Creating " + type + " behavior.");

            composite = CompositeBuilder.GetComposite(Class, TalentManager.CurrentSpec, type, context, out count);

            // handle those composites we need to default if not found
            if (composite == null)
            {
                if ( type == BehaviorType.Rest)
                    composite = Helpers.Rest.CreateDefaultRestBehaviour();
            }

            TreeHooks.Instance.ReplaceHook(type.ToString(), composite);

            if ((composite == null || count <= 0) && error)
            {
                StopBot( string.Format( "Singular does not support {0} for this {1} {2} in {3} context!", type, StyxWoW.Me.Class, TalentManager.CurrentSpec, context));
                return false;
            }

            return composite != null;
        }

        private static Composite CreateLogTargetChanges(string sType)
        {
            return new Action(r =>
                {
                    if ((SingularSettings.Debug && (Me.CurrentTargetGuid != _guidLastTarget || _timerLastTarget.IsFinished )))
                    {
                        if (Me.CurrentTarget == null)
                        {
                            if (_guidLastTarget != 0)
                            {
                                Logger.WriteDebug(sType + " CurrentTarget now: (null)");
                            }
                        }
                        else
                        {
                            WoWUnit target = Me.CurrentTarget;
                            Logger.WriteDebug( sType + " CurrentTarget now: {0} h={1:F1}%, maxh={2}, d={3:F1} yds, box={4:F1}, player={5}, hostile={6}, faction={7}, loss={8}, facing={9}",
                                target.SafeName(),
                                target.HealthPercent,
                                target.MaxHealth,
                                target.Distance,
                                target.CombatReach,
                                target.IsPlayer.ToYN(),
                                target.IsHostile.ToYN(),
                                target.Faction,
                                target.InLineOfSpellSight.ToYN(),
                                Me.IsSafelyFacing(target).ToYN()
                                );
                        }

                        _guidLastTarget = Me.CurrentTargetGuid;
                        _timerLastTarget.Reset();
                    }

                    return RunStatus.Failure;
                });

        }

        private static void MonitorQuestingPullDistance()
        {
            if (SingularRoutine.IsQuesting && SingularSettings.Instance.PullDistanceOverride == CharacterSettings.Instance.PullDistance)
            {
                int newPullDistance = 0;
                switch (Me.Class)
                {
                    case WoWClass.DeathKnight:
                    case WoWClass.Monk:
                    case WoWClass.Paladin:
                    case WoWClass.Rogue:
                    case WoWClass.Warrior:
                        break;

                    default:
                        if (Me.Specialization == WoWSpec.None || Me.Specialization == WoWSpec.DruidFeral || Me.Specialization == WoWSpec.DruidGuardian || Me.Specialization == WoWSpec.ShamanEnhancement)
                            break;

                        newPullDistance = 40;
                        break;
                }

                if (newPullDistance != 0)
                {
                    Logger.Write(Color.White, "Quest Profile set Pull Distance to {0}, forcing to {1} for next Pull", CharacterSettings.Instance.PullDistance, newPullDistance);
                    CharacterSettings.Instance.PullDistance = newPullDistance;
                }
            }
        }

        #region Nested type: LockSelector

        /// <summary>
        /// This behavior wraps the child behaviors in a 'FrameLock' which can provide a big performance improvement 
        /// if the child behaviors makes multiple api calls that internally run off a frame in WoW in one CC pulse.
        /// </summary>
        private class LockSelector : PrioritySelector
        {
            public LockSelector(params Composite[] children) : base(children)
            {
            }

            public override RunStatus Tick(object context)
            {
                using (StyxWoW.Memory.AcquireFrame())
                {
                    return base.Tick(context);
                }
            }
        }

        #endregion
    }
}