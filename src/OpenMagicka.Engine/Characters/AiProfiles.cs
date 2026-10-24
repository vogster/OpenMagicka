using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay.AI.Fuzzy;
using AI = OpenMagicka.Gameplay.AI;
using Gameplay = OpenMagicka.Gameplay;

namespace OpenMagicka.Engine.Characters;

/// <summary>The content reader's AI weights and abilities as the gameplay core's mind (ENTITIES_AI.md §7).</summary>
public static class AiProfiles
{
    public static AI.AiProfile From(CharacterTemplateContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var w = content.Ai;
        return new AI.AiProfile(w.AlertRadius, w.GroupChase, w.GroupSeparation, w.GroupCohesion, w.GroupAlignment, w.GroupWander,
            w.FriendlyAvoidance, w.EnemyAvoidance, w.SightAvoidance, w.DangerAvoidance, w.AngerWeight, w.DistanceWeight, w.HealthWeight,
            w.Flocking, w.BreakFreeStrength, [.. content.Abilities.Select(Ability)]);
    }

    public static AI.AbilityTemplate Ability(AbilityContent a)
    {
        ArgumentNullException.ThrowIfNull(a);
        var (cooldown, target, animations) = (a.Cooldown, (AI.AbilityTarget)(byte)a.Target, a.Animations);
        var desire = a.Fuzzy is { } text ? FuzzyExpression.Parse(text) : null;
        return a switch
        {
            MeleeAbility m => new AI.MeleeAbility(cooldown, target, desire, animations, m.MinRange, m.MaxRange, m.Arc, m.Weapons, m.Rotate),
            RangedAbility r => new AI.RangedAbility(cooldown, target, desire, animations, r.MinRange, r.MaxRange, r.Elevation, MathF.PI, r.Accuracy, r.Weapons),
            CastSpellAbility c => new AI.CastSpellAbility(cooldown, target, desire, animations, c.MinRange, c.MaxRange, c.Arc, c.ChantSpeed, c.Power,
                (Gameplay.Spells.CastType)(int)c.CastType, [.. c.Elements.Select(e => (Gameplay.Spells.Elements)(int)e)]),
            BlockAbility b => new AI.BlockAbility(cooldown, target, desire, animations, b.Arc, b.Shield),
            DashAbility d => new AI.DashAbility(cooldown, target, desire, animations, d.MinRange, d.MaxRange, d.Arc,
                new System.Numerics.Vector3(d.Vector.X, d.Vector.Y, d.Vector.Z)),
            JumpAbility j => new AI.JumpAbility(cooldown, target, desire, animations, j.MinRange, j.MaxRange, j.Angle, j.Elevation),
            ElementStealAbility e => new AI.ElementStealAbility(cooldown, target, desire, animations, e.Range, e.Angle),
            SpecialAbilityAbility s => new AI.SpecialAbilityAbility(cooldown, target, desire, animations, s.MinRange, s.MaxRange, s.Angle, s.Weapon),
            RemoveStatusAbility => new AI.RemoveStatusAbility(cooldown, target, desire, animations),
            GripCharacterFromBehindAbility g => new AI.GripAbility(cooldown, target, desire, animations, g.MinRange, g.MaxRange, g.Angle, g.MaxWeight,
                null, FromBehind: true, Zombie: false),
            PickUpCharacterAbility p => new AI.GripAbility(cooldown, target, desire, animations, p.MinRange, p.MaxRange, p.Angle, p.MaxWeight,
                p.DropAnimation, FromBehind: false, Zombie: p.IsZombieGrip),
            ThrowGripAbility t => new AI.ThrowGripAbility(cooldown, target, desire, animations, t.MinRange, t.MaxRange, t.Elevation,
                [.. t.Damages.Select(ItemTemplates.Damage)]),
            DamageGripAbility => new AI.GripActionAbility(cooldown, target, desire, animations, Confuse: false),
            ConfuseGripAbility => new AI.GripActionAbility(cooldown, target, desire, animations, Confuse: true),
            _ => throw new InvalidDataException($"an unknown ability {a.GetType().Name}"),
        };
    }
}
