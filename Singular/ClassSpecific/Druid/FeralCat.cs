﻿using Singular.Dynamics;
using Singular.Helpers;
using Singular.Lists;
using Singular.Managers;

using Styx;
using Styx.Combat.CombatRoutine;

using TreeSharp;

namespace Singular.ClassSpecific.Druid
{
    public static class FeralCat
    {
        [Spec(TalentSpec.FeralDruid)]
        [Behavior(BehaviorType.Combat)]
        [Class(WoWClass.Druid)]
        [Priority(500)]
        [Context(WoWContext.Normal | WoWContext.Instances)]
        public static Composite CreateFeralCatCombat()
        {
            return new PrioritySelector(
                new Decorator(
                    ret => StyxWoW.Me.Shapeshift != ShapeshiftForm.Cat,
                    Spell.Cast("Cat Form")),

                // Ensure we're facing the target. Kthx.
                Movement.CreateFaceTargetBehavior(),
                Spell.Buff("Faerie Fire (Feral)"),

                new Decorator(
                    ret => BossList.BossIds.Contains(StyxWoW.Me.CurrentTarget.Entry),
                    new PrioritySelector(
                        Spell.Buff("Mangle (Cat)"),
                        Spell.Cast("Tiger's Fury"),
                        Spell.Cast("Berserk"),
                        Spell.Buff("Rip", ret => StyxWoW.Me.ComboPoints == 5),
                        Spell.Buff("Rake"),
                        Spell.Cast("Savage Roar"),
                        Spell.Cast("Shred", ret => StyxWoW.Me.CurrentTarget.MeIsSafelyBehind),
                        Spell.Cast("Mangle", ret => !StyxWoW.Me.CurrentTarget.MeIsSafelyBehind),
                        // Here's how this works. If the mob is a boss, try and get behind it. If we *can't*
                        // get behind it, we should try to move to it. Its really that simple.
                        Movement.CreateMoveBehindTargetBehavior(4f),
                        Movement.CreateMoveToTargetBehavior(true, 4f))),

                // TODO: Split all this into multiple combat rotation funcs.
                // TODO: Add targeting helpers (NearbyEnemyUnits)
                // TODO: Add simple wrappers for elites, bosses, etc

                // For normal 'around town' grinding, this is all we really need.
                Spell.Cast("Ferocious Bite", ret => StyxWoW.Me.ComboPoints >= 4),
                Spell.Cast("Berserk"),
                Spell.Buff("Rake"),
                Spell.Cast("Mangle (Cat)"),
                // Since we can't get 'behind' mobs, just do this, kaythanks
                Movement.CreateMoveToTargetBehavior(true, 4f)
                );
        }
    }
}
