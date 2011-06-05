﻿using System.Linq;

using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;

using Styx;
using Styx.Combat.CombatRoutine;

using TreeSharp;
using Styx.Logic.Combat;

namespace Singular.ClassSpecific.Warlock
{
    public class Affliction
    {
        [Class(WoWClass.Warlock)]
        [Spec(TalentSpec.AfflictionWarlock)]
        [Context(WoWContext.All)]
        [Behavior(BehaviorType.Combat)]
        [Behavior(BehaviorType.Pull)]
        [Priority(500)]
        public static Composite CreateAfflictionCombat()
        {
            PetManager.WantedPet = "Succubus";

            return new PrioritySelector(
                Safers.EnsureTarget(),
                Movement.CreateMoveToLosBehavior(),
                Movement.CreateFaceTargetBehavior(),
                Waiters.WaitForCast(true),
                Helpers.Common.CreateAutoAttack(true),
                // Emergencies
                new Decorator(
                    ret => StyxWoW.Me.HealthPercent < 20,
                    new PrioritySelector(
                        //Spell.Buff("Fear", ret => !Me.CurrentTarget.HasAura("Fear")),
                        Spell.Cast("Howl of Terror", ret => StyxWoW.Me.CurrentTarget.Distance < 10 && StyxWoW.Me.CurrentTarget.IsPlayer),
                        Spell.Cast("Death Coil", ret => !StyxWoW.Me.CurrentTarget.HasAura("Howl of Terror") && !StyxWoW.Me.CurrentTarget.HasAura("Fear")),
                        Spell.BuffSelf("Soulburn", ret => StyxWoW.Me.CurrentSoulShards > 0),
                        Spell.Cast("Drain Life")
                        )),
                Spell.Cast("Life Tap", ret => StyxWoW.Me.ManaPercent < 10),
                Spell.Cast("Health Funnel", ret => StyxWoW.Me.GotAlivePet && StyxWoW.Me.Pet.HealthPercent < 30),
                // Finishing sequence
                Spell.Cast(
                    "Soul Swap",
                    ret =>
                    !StyxWoW.Me.HasAura("Soul Swap") && StyxWoW.Me.CurrentTarget.HealthPercent < 10 && StyxWoW.Me.CurrentTarget.HasAura("Unstable Affliction") &&
                    !Unit.IsBoss(StyxWoW.Me.CurrentTarget)),
                Spell.Cast("Drain Soul", ret => StyxWoW.Me.CurrentTarget.HealthPercent < 10),
                // Elites
                new Decorator(
                    ret => Unit.IsBoss(StyxWoW.Me.CurrentTarget),
                    new PrioritySelector(
                        Spell.BuffSelf("Demon Soul"),
                        Spell.Buff("Curse of Elements", ret => !StyxWoW.Me.CurrentTarget.HasAura("Curse of Elements")),
                        new Decorator(
                            ret => SpellManager.CanCast("Summon Infernal"),
                            new Action(
                                ret =>
                                    {
                                        SpellManager.Cast("Summon Infernal");
                                        LegacySpellManager.ClickRemoteLocation(StyxWoW.Me.CurrentTarget.Location);
                                    }))
                        )),
                // AoE
                new Decorator(
                    ret => Unit.NearbyUnfriendlyUnits.Count(u => u.Distance < 15) >= 5,
                    new PrioritySelector(
                        Spell.BuffSelf("Demon Soul"),
                        Spell.BuffSelf(
                            "Soulburn",
                            ret => !StyxWoW.Me.CurrentTarget.HasAura("Seed of Corruption") && StyxWoW.Me.CurrentSoulShards > 0 && TalentManager.GetCount(1, 15) == 1),
                        Spell.Buff("Seed of Corruption", ret => !StyxWoW.Me.CurrentTarget.HasAura("Seed of Corruption"))
                        )),
                // Standard Nuking
                Spell.Cast("Shadow Bolt", ret => StyxWoW.Me.HasAura("Shadow Trance")),
                Spell.Buff("Haunt"),
                Spell.Cast("Soul Swap", ret => StyxWoW.Me.HasAura("Soul Swap") && StyxWoW.Me.CurrentTarget.HealthPercent > 10),
                Spell.Buff("Bane of Doom", ret => Unit.IsBoss(StyxWoW.Me.CurrentTarget) && !StyxWoW.Me.CurrentTarget.HasAura("Bane of Doom")),
                Spell.Buff("Bane of Agony", ret => !StyxWoW.Me.CurrentTarget.HasAura("Bane of Agony") && !StyxWoW.Me.CurrentTarget.HasAura("Bane of Doom")),
                Spell.Buff("Corruption", ret => !StyxWoW.Me.CurrentTarget.HasAura("Corruption") && !StyxWoW.Me.CurrentTarget.HasAura("Seed of Corruption")),
                Spell.Buff("Unstable Affliction", ret => !StyxWoW.Me.CurrentTarget.HasAura("Unstable Affliction")),
                Spell.Cast("Drain Soul", ret => StyxWoW.Me.CurrentTarget.HealthPercent < 25),
                Spell.Cast("Shadowflame", ret => StyxWoW.Me.CurrentTarget.Distance < 5),
                Spell.BuffSelf("Demon Soul"),
                Spell.Buff("Curse of Weakness", ret => StyxWoW.Me.CurrentTarget.IsPlayer && !StyxWoW.Me.CurrentTarget.HasAura("Curse of Weakness")),
                Spell.Cast("Life Tap", ret => StyxWoW.Me.ManaPercent < 50 && StyxWoW.Me.HealthPercent > 70),
                Spell.Cast("Drain Life", ret => StyxWoW.Me.HealthPercent < 70),
                Spell.Cast("Health Funnel", ret => StyxWoW.Me.GotAlivePet && StyxWoW.Me.Pet.HealthPercent < 70),
                Spell.Cast("Shadow Bolt"),
                Movement.CreateMoveToTargetBehavior(true, 35f)
                );
        }
    }
}