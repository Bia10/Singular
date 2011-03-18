﻿using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Combat.CombatRoutine;

using TreeSharp;
using CommonBehaviors.Actions;
using Styx;
using System.Threading;

namespace Singular
{
    partial class SingularRoutine
    {
		[Class(WoWClass.Hunter)]
        [Spec(TalentSpec.BeastMasteryHunter)]
        [Spec(TalentSpec.SurvivalHunter)]
        [Spec(TalentSpec.MarksmanshipHunter)]
		[Spec(TalentSpec.Lowbie)]
        [Behavior(BehaviorType.PreCombatBuffs)]
        [Context(WoWContext.All)]
        public Composite CreateHunterBuffs()
        {
            WantedPet = "1";
			return new PrioritySelector(
				new Decorator(
					ctx => Me.CastingSpell != null && Me.CastingSpell.Name == "Revive " + WantedPet && Me.GotAlivePet,
					new Action(ctx => SpellManager.StopCasting())),

				CreateWaitForCast(),
				CreateSpellBuffOnSelf("Aspect of the Hawk"),
                CreateSpellBuffOnSelf("Track Hidden"),
                //new ActionLogMessage(false, "Checking for pet"),
				new Decorator(
					ret => !Me.GotAlivePet,
                    new Sequence(
					    new Action(ret => PetManager.CallPet(WantedPet)),
                        new Action(ret => Thread.Sleep(1000)),
                        new DecoratorContinue(
                            ret => !Me.GotAlivePet && SpellManager.CanCast("Revive Pet"),
                            new Sequence(
                                new Action(ret => SpellManager.Cast("Revive Pet")),
                                new Action(ret => StyxWoW.SleepForLagDuration()),
                                new WaitContinue(11,
                                    ret => !Me.IsCasting,
                                    new ActionAlwaysSucceed()))))),
                CreateSpellCast("Mend Pet", ret => (Me.Pet.HealthPercent < 70 || (Me.Pet.HappinessPercent < 90 && TalentManager.HasGlyph("Mend Pet"))) && !Me.Pet.HasAura("Mend Pet"))
					);
        }
        protected Composite CreateHunterBackPedal()
        {
            return
                new Decorator(
                    ret => Me.CurrentTarget.Distance <= 7 && Me.CurrentTarget.IsAlive &&
                           (Me.CurrentTarget.CurrentTarget == null || Me.CurrentTarget.CurrentTarget != Me),
                    new Action(ret =>
                    {
                        WoWPoint moveTo = WoWMathHelper.CalculatePointFrom(Me.Location, Me.CurrentTarget.Location, 10f);

                        if (Navigator.CanNavigateFully(Me.Location, moveTo))
                            Navigator.MoveTo(moveTo);
                    }));
        }
	
    }
}
