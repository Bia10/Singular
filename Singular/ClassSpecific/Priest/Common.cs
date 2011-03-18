﻿#region Revision Info

// This file is part of Singular - A community driven Honorbuddy CC
// $Author$
// $Date$
// $HeadURL$
// $LastChangedBy$
// $LastChangedDate$
// $LastChangedRevision$
// $Revision$

#endregion

using System.Linq;

using Styx.Combat.CombatRoutine;
using Styx.WoWInternals.WoWObjects;

using TreeSharp;

namespace Singular
{
    partial class SingularRoutine
    {
        [Class(WoWClass.Priest)]
        [Spec(TalentSpec.DisciplineHealingPriest)]
        [Spec(TalentSpec.DisciplinePriest)]
        [Spec(TalentSpec.HolyPriest)]
        [Spec(TalentSpec.ShadowPriest)]
        [Spec(TalentSpec.Lowbie)]
        [Behavior(BehaviorType.PreCombatBuffs)]
        [Context(WoWContext.All)]
        public Composite CreatePriestPreCombatBuffs()
        {
            return new PrioritySelector(
                CreateSpellBuffOnSelf("Power Word: Fortitude", ret => CanCastFortitudeOn(Me)),
                CreateSpellBuffOnSelf("Inner Fire"),
                CreateSpellBuffOnSelf("Fear Ward"),
                CreateSpellCast(
                    "Power Word: Fortitude",
                    ret => NearbyFriendlyPlayers.Any(u => !u.Dead && !u.IsGhost && u.IsInMyPartyOrRaid && CanCastFortitudeOn(u)),
                    ret => Me),
                CreateSpellBuffOnSelf("Shadowform"),
                CreateSpellBuffOnSelf("Vampiric Embrace")
                );
        }

        [Class(WoWClass.Priest)]
        [Spec(TalentSpec.DisciplineHealingPriest)]
        [Spec(TalentSpec.DisciplinePriest)]
        [Spec(TalentSpec.HolyPriest)]
        [Spec(TalentSpec.ShadowPriest)]
        [Spec(TalentSpec.Lowbie)]
        [Behavior(BehaviorType.CombatBuffs)]
        [Context(WoWContext.All)]
        public Composite CreatePriestCombatBuffs()
        {
            return new PrioritySelector(
                CreateUsePotionAndHealthstone(10, 10)
                );
        }

        public bool CanCastFortitudeOn(WoWUnit unit)
        {
            return !unit.HasAura("Blood Pact") &&
                   !unit.HasAura("Power Word: Fortitude") &&
                   !unit.HasAura("Qiraji Fortitude") &&
                   !unit.HasAura("Commanding Shout");
        }
    }
}