﻿using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Styx;
using Styx.Combat.CombatRoutine;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Singular.ClassSpecific.Hunter
{
    public static class Survival
    {
        [Class(WoWClass.Hunter)]
        [Spec(TalentSpec.SurvivalHunter)]
        [Behavior(BehaviorType.Combat)]
        [Behavior(BehaviorType.Pull)]
        public static Composite CreateSurvivalCombat()
        {
            PetManager.WantedPet = "1";
            return new PrioritySelector(
                new Decorator(
                    ret => !StyxWoW.Me.GotAlivePet,
                    new Action(ret => PetManager.CallPet(PetManager.WantedPet))),
                Safers.EnsureTarget(),
                Movement.CreateMoveToLosBehavior(),
                Common.CreateHunterBackPedal(),
                Movement.CreateFaceTargetBehavior(),
                // Always keep it up on our target!
                Spell.Buff("Hunter's Mark"),
                // Heal pet when below 70
                Spell.Cast("Mend Pet", ret => StyxWoW.Me.Pet.HealthPercent < 70 && !StyxWoW.Me.Pet.HasAura("Mend Pet")),
                Spell.Cast(
                    "Concussive Shot",
                    ret => StyxWoW.Me.CurrentTarget.CurrentTarget == null || StyxWoW.Me.CurrentTarget.CurrentTarget == StyxWoW.Me),
                //Rapid fire on elite 
                Spell.Buff("Rapid Fire", ret => StyxWoW.Me.CurrentTarget.Elite),
                //Cast when mob Hp below 20
                Spell.Cast("Kill Shot", ret => StyxWoW.Me.CurrentTarget.HealthPercent < 19),
                new Decorator(
                    ret => !StyxWoW.Me.HasAura("Lock and Load"),
                    new PrioritySelector(
                // The extra here 'flips' the explosive usage.
                        Spell.Cast("Kill Command", ret => StyxWoW.Me.FocusPercent == 100)
                        // Note: LastSpellCast needs to be added
                        //Spell.Cast("Explosive Shot", ret => LastSpellCast != "Explosive Shot"),
                        //Spell.Cast("Steady Shot", ret => LastSpellCast != "Steady Shot")
                        )),
                // Refresh when it wears off.
                Spell.Buff("Serpent Sting", ret => !StyxWoW.Me.CurrentTarget.HasAura("Serpent Sting")),
                // Whenever it's not on CD
                Spell.Cast("Explosive Shot"),
                // Whenever its not on CD
                Spell.Cast("Black Arrow"),
                // Main DPS filler
                Spell.Cast("Steady Shot"),
                Spell.Cast("Arcane Shot"),
                Movement.CreateMoveToTargetBehavior(true, 35f)
                );
        }
    }
}