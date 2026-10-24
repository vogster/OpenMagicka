using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.AI.Fuzzy;

/// <summary>What an expression is weighed against: the character thinking, its target, and the way between them.</summary>
public readonly struct FuzzyArguments
{
    public FuzzyArguments(Character self, Entity target, float alertRadius)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(target);
        (Self, Target, AlertRadius) = (self, target, alertRadius);
        (SelfPosition, TargetPosition, SelfFacing) = (self.Position, target.Position, self.Body.Facing);
        TargetFacing = target is Character character ? character.Body.Facing : -Vector3.UnitZ;
        Delta = TargetPosition - SelfPosition;
        Distance = Delta.Length();
        DeltaNormalized = Delta / Distance;                                   // as the original: not a number when they stand together
    }

    /// <summary>Weighing an ability on oneself: no way between, and no distance.</summary>
    public static FuzzyArguments OnSelf(Character self, float alertRadius) => new(self, alertRadius);

    private FuzzyArguments(Character self, float alertRadius)
    {
        ArgumentNullException.ThrowIfNull(self);
        (Self, Target, AlertRadius) = (self, self, alertRadius);
        (SelfPosition, TargetPosition, SelfFacing, TargetFacing) = (self.Position, self.Position, self.Body.Facing, self.Body.Facing);
    }

    public Character Self { get; }

    public Entity Target { get; }

    /// <summary>The thinker's alert radius (its template's), which Danger looks within.</summary>
    public float AlertRadius { get; }

    public Vector3 SelfPosition { get; }

    public Vector3 TargetPosition { get; }

    public Vector3 SelfFacing { get; }

    /// <summary>The target's forward: a character's facing; anything else's unturned forward.</summary>
    public Vector3 TargetFacing { get; }

    public Vector3 Delta { get; }

    public float Distance { get; }

    public Vector3 DeltaNormalized { get; }
}
