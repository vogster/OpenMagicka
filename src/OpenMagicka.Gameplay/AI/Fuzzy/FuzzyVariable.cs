using System.Globalization;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using OpenMagicka.Gameplay.Spells.Magicks;
using Barrier = OpenMagicka.Gameplay.Spells.Effects.Barrier;

namespace OpenMagicka.Gameplay.AI.Fuzzy;

/// <summary>Whose a variable asks about: the world's (no prefix), the thinker's (<c>s.</c>) or the target's (<c>t.</c>).</summary>
public enum FuzzyScope { Global, Self, Target }

public enum FuzzyVariableKind
{
    DistanceLinear, DistanceExponential, Behind, Danger, HasStatus, Gripped, Gripping, Entangled, Speed, Health,
    FriendlyDensity, EnemyDensity, Threat, LineOfSight, IsCharacter, IsEthereal, Resistance, Shielded, LineOfSightCollision,
    IsShield, IsBarrier, GripDamageAccumulation, GripDamageAccumulationNormalized, IsOnGround, Angle,
}

/// <summary>A variable leaf: <c>[scope.]Name(arguments)</c>, names and scopes in any case (ENTITIES_AI.md §7.5).</summary>
public sealed class FuzzyVariable : FuzzyExpression
{
    private static readonly Dictionary<string, FuzzyVariableKind> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DistanceLinear"] = FuzzyVariableKind.DistanceLinear, ["DistLin"] = FuzzyVariableKind.DistanceLinear,
        ["DistanceExponential"] = FuzzyVariableKind.DistanceExponential, ["DistExp"] = FuzzyVariableKind.DistanceExponential,
        ["Behind"] = FuzzyVariableKind.Behind, ["Danger"] = FuzzyVariableKind.Danger,
        ["HasStatus"] = FuzzyVariableKind.HasStatus, ["Status"] = FuzzyVariableKind.HasStatus,
        ["Gripped"] = FuzzyVariableKind.Gripped, ["Gripping"] = FuzzyVariableKind.Gripping, ["Entangled"] = FuzzyVariableKind.Entangled,
        ["Speed"] = FuzzyVariableKind.Speed, ["Health"] = FuzzyVariableKind.Health,
        ["FriendlyDensity"] = FuzzyVariableKind.FriendlyDensity, ["FDensity"] = FuzzyVariableKind.FriendlyDensity,
        ["EnemyDensity"] = FuzzyVariableKind.EnemyDensity, ["EDensity"] = FuzzyVariableKind.EnemyDensity,
        ["Threat"] = FuzzyVariableKind.Threat, ["LOS"] = FuzzyVariableKind.LineOfSight, ["LineOfSight"] = FuzzyVariableKind.LineOfSight,
        ["IsCharacter"] = FuzzyVariableKind.IsCharacter, ["Character"] = FuzzyVariableKind.IsCharacter, ["IsC"] = FuzzyVariableKind.IsCharacter,
        ["IsEthereal"] = FuzzyVariableKind.IsEthereal, ["Ethereal"] = FuzzyVariableKind.IsEthereal,
        ["Resistance"] = FuzzyVariableKind.Resistance, ["Res"] = FuzzyVariableKind.Resistance, ["Shielded"] = FuzzyVariableKind.Shielded,
        ["LOSC"] = FuzzyVariableKind.LineOfSightCollision, ["LineOfSightCollision"] = FuzzyVariableKind.LineOfSightCollision,
        ["IsShield"] = FuzzyVariableKind.IsShield, ["IsS"] = FuzzyVariableKind.IsShield, ["Shield"] = FuzzyVariableKind.IsShield,
        ["IsBarrier"] = FuzzyVariableKind.IsBarrier, ["IsB"] = FuzzyVariableKind.IsBarrier, ["Barrier"] = FuzzyVariableKind.IsBarrier,
        ["GripDamageAccumulation"] = FuzzyVariableKind.GripDamageAccumulation, ["GripDmgAcc"] = FuzzyVariableKind.GripDamageAccumulation,
        ["GripDamageAccumulationNormalized"] = FuzzyVariableKind.GripDamageAccumulationNormalized,
        ["GripDmgAccNrm"] = FuzzyVariableKind.GripDamageAccumulationNormalized,
        ["IsOnGround"] = FuzzyVariableKind.IsOnGround, ["Angle"] = FuzzyVariableKind.Angle,
    };

    private static readonly Dictionary<string, FuzzyScope> Scopes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Global"] = FuzzyScope.Global, ["G"] = FuzzyScope.Global, ["Self"] = FuzzyScope.Self, ["S"] = FuzzyScope.Self,
        ["Target"] = FuzzyScope.Target, ["T"] = FuzzyScope.Target,
    };

    private FuzzyVariable(FuzzyScope scope, FuzzyVariableKind kind) => (Scope, Kind) = (scope, kind);

    public FuzzyScope Scope { get; }

    public FuzzyVariableKind Kind { get; }

    public float A { get; private init; }

    public float B { get; private init; }

    public float C { get; private init; }

    public float D { get; private init; }

    public Elements Element { get; private init; }

    public StatusEffects Status { get; private init; }

    /// <summary>Whether a variable's name ends the text just before <paramref name="end"/> (a function's bracket opens there).</summary>
    internal static bool EndsWithName(string text, int end) =>
        Names.Keys.Any(name => end >= name.Length && string.Compare(text, end - name.Length, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0);

    /// <summary>
    /// Reads a leaf as the original: blanks out, split on , / ( ); the first piece may be scope.Name.
    /// Only Danger and Threat read an element (so Resistance's argument is ignored, as the original).
    /// </summary>
    internal static FuzzyVariable Read(string text)
    {
        try
        {
            return ReadParts(text);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or OverflowException)
        {
            throw new FormatException($"a fuzzy variable lacks its arguments: \"{text}\"", e);
        }
    }

    private static FuzzyVariable ReadParts(string text)
    {
        var parts = text.Replace(" ", "").Replace("\n", "").Replace("\t", "").Replace("\r", "")
            .Split([',', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new FormatException($"an empty fuzzy variable in \"{text}\"");
        }
        var head = parts[0].Split('.');
        var scope = FuzzyScope.Global;
        var name = head[0];
        if (head.Length > 1 && Scopes.TryGetValue(head[0], out var named))
        {
            (scope, name) = (named, head[1]);
        }
        if (!Names.TryGetValue(name, out var kind))
        {
            throw new FormatException($"no fuzzy variable \"{name}\" in \"{text}\"");
        }
        var args = parts.Skip(1).ToArray();
        if (!Meaningful(scope, kind))
        {
            throw new FormatException($"{kind} has no meaning in scope {scope}: \"{text}\"");      // the original throws when it weighs it
        }
        float Number(int i) => float.Parse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture);
        return kind switch
        {
            FuzzyVariableKind.DistanceLinear or FuzzyVariableKind.DistanceExponential => args.Length > 1
                ? new FuzzyVariable(scope, kind) { A = Number(0), B = Number(1) }
                : new FuzzyVariable(scope, kind) { A = -Number(0), B = Number(0) },
            FuzzyVariableKind.FriendlyDensity or FuzzyVariableKind.EnemyDensity => new FuzzyVariable(scope, kind) { A = args.Length > 0 ? Number(0) : 6 },
            FuzzyVariableKind.Danger or FuzzyVariableKind.Threat => new FuzzyVariable(scope, kind) { Element = FuzzyNames.Elements(args[0]) },
            FuzzyVariableKind.HasStatus => new FuzzyVariable(scope, kind) { Status = FuzzyNames.Status(args[0]) },
            FuzzyVariableKind.LineOfSightCollision => new FuzzyVariable(scope, kind)
            {
                A = args.Length > 0 ? Number(0) : 0,
                B = args.Length > 1 ? Number(1) : 0,
                C = args.Length > 2 ? Number(2) : 0,
                D = args.Length > 3 ? Number(3) : -1,
            },
            _ => new FuzzyVariable(scope, kind),
        };
    }

    /// <summary>The scopes the original throws at: a threat is the target's; statuses, grips, vines and speed are someone's.</summary>
    private static bool Meaningful(FuzzyScope scope, FuzzyVariableKind kind) => kind switch
    {
        FuzzyVariableKind.Threat => scope == FuzzyScope.Target,
        FuzzyVariableKind.HasStatus or FuzzyVariableKind.Gripped or FuzzyVariableKind.Gripping or FuzzyVariableKind.Entangled
            or FuzzyVariableKind.Speed => scope != FuzzyScope.Global,
        _ => true,
    };

    public override float Value(in FuzzyArguments args)
    {
        var self = args.Self;
        var other = args.Target as Character;
        var subject = Scope == FuzzyScope.Target ? other : Scope == FuzzyScope.Self ? self : null;
        return Kind switch
        {
            FuzzyVariableKind.DistanceLinear => FuzzyMath.DistanceLinear(args.Distance, A, B),
            FuzzyVariableKind.DistanceExponential => FuzzyMath.DistanceExponential(args.Distance, A, B),
            FuzzyVariableKind.Behind => FuzzyMath.Along(args.DeltaNormalized, args.TargetFacing),
            FuzzyVariableKind.Danger => FuzzyMath.Danger(self, args.AlertRadius, Element),
            FuzzyVariableKind.Threat => other is null ? 0 : FuzzyMath.Threat(self, other, Element),
            FuzzyVariableKind.HasStatus => Flag(subject is not null && (subject.Statuses.Active & Status) == Status),   // all of them, as the original
            FuzzyVariableKind.Gripped or FuzzyVariableKind.Gripping => 0,                                       // grips come with OM-33
            FuzzyVariableKind.Entangled => Flag(subject?.IsEntangled == true),
            FuzzyVariableKind.Speed => subject?.Body.Movement.Length() ?? 0,
            FuzzyVariableKind.Health => Scope == FuzzyScope.Self ? self.HitPoints / self.MaxHitPoints : Health(args.Target),
            FuzzyVariableKind.FriendlyDensity or FuzzyVariableKind.EnemyDensity => FuzzyMath.Density(self.Manager,
                Scope == FuzzyScope.Self ? self : args.Target, self.Factions, A, Kind == FuzzyVariableKind.FriendlyDensity),
            FuzzyVariableKind.LineOfSight => Flag(!Blocked(args)),
            FuzzyVariableKind.LineOfSightCollision => Flag(!WallBetween(args)),
            FuzzyVariableKind.IsCharacter => Scope == FuzzyScope.Target ? Flag(other is not null) : Flag(Scope == FuzzyScope.Self),
            FuzzyVariableKind.IsEthereal => Flag(Scope == FuzzyScope.Target ? other?.IsEthereal == true : self.IsEthereal),
            FuzzyVariableKind.Resistance => 1 - ResistanceToNothing(Scope == FuzzyScope.Target ? args.Target : self),
            FuzzyVariableKind.Shielded => Flag(subject?.SelfShield is not null),
            FuzzyVariableKind.IsShield => Flag(Scope == FuzzyScope.Target && args.Target is SpellShield),
            FuzzyVariableKind.IsBarrier => Flag(Scope == FuzzyScope.Target && args.Target is Barrier),
            FuzzyVariableKind.GripDamageAccumulation or FuzzyVariableKind.GripDamageAccumulationNormalized => 0,
            FuzzyVariableKind.IsOnGround => Flag(subject?.Body.IsTouchingGround == true),
            FuzzyVariableKind.Angle => Facing(args),
            _ => 0,
        };
    }

    public override string ToString() => $"{(Scope == FuzzyScope.Global ? "" : Scope == FuzzyScope.Self ? "s." : "t.")}{Kind}";

    private static float Flag(bool on) => on ? 1 : 0;

    private static float Health(Entity target) => target switch
    {
        Character c => c.HitPoints / c.MaxHitPoints,
        _ => 1,
    };

    /// <summary>
    /// As the original, the resistance is asked of no element at all: characters, shields and
    /// barriers then answer 1, missiles, mines and eggs 0.
    /// </summary>
    private static float ResistanceToNothing(Entity entity) => entity is Missile or SpellMine or ElementalEgg ? 0 : 1;

    /// <summary>
    /// Anything but the two standing on the way between them (the original's line of sight). Our
    /// running magicks are entities, the original's are not, so they do not block (Death aside, which is one there too).
    /// </summary>
    private static bool Blocked(in FuzzyArguments args)
    {
        if (args.Self.Manager is not { } manager)
        {
            return false;
        }
        var segment = new Segment(args.SelfPosition, args.Delta);
        foreach (var entity in manager.Query(args.SelfPosition + args.Delta * 0.5f, args.Distance * 0.5f))
        {
            if (!ReferenceEquals(entity, args.Self) && !ReferenceEquals(entity, args.Target) && (entity is not IMagickEffect || entity is Reaper)
                && entity.SegmentIntersect(segment, args.Self.Radius, out _))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The level between: from the thinker, put forward by (a, c) along its facing and up by b, toward the target (at most d long).</summary>
    private bool WallBetween(in FuzzyArguments args)
    {
        var from = args.SelfPosition + new System.Numerics.Vector3(args.SelfFacing.X * A, B, args.SelfFacing.Z * C);
        var delta = D <= 0 || !(args.Distance > 0) ? args.Delta : args.DeltaNormalized * MathF.Min(D, args.Distance);   // together: no way to go
        return args.Self.World.CastSegment(from, from + delta, levelOnly: true) is not null;
    }

    /// <summary>How squarely the thinker faces the target, on the flat: the cosine.</summary>
    private static float Facing(in FuzzyArguments args)
    {
        var (way, facing) = (args.Delta with { Y = 0 }, args.SelfFacing with { Y = 0 });
        return way.LengthSquared() > 1e-6f && facing.LengthSquared() > 1e-6f
            ? System.Numerics.Vector3.Dot(System.Numerics.Vector3.Normalize(way), System.Numerics.Vector3.Normalize(facing))
            : 0;
    }
}
