using System.Numerics;

namespace OpenMagicka.Physics;

/// <summary>
/// World-wide physics parameters. The iteration counts are tuning of the current engine and may
/// change with it.
/// </summary>
/// <param name="Gravity">The original uses twice the Earth's gravity.</param>
/// <param name="SubSteps">Solver sub-steps per <see cref="PhysicsWorld.Step"/>, 1 to <see cref="MaxIterations"/>.</param>
/// <param name="SolverIterations">Velocity iterations per sub-step, 1 to <see cref="MaxIterations"/>.</param>
/// <param name="RelaxationIterations">Position relaxation iterations per sub-step, 0 to <see cref="MaxIterations"/>.</param>
public sealed record PhysicsSettings(Vector3 Gravity, int SubSteps, int SolverIterations, int RelaxationIterations)
{
    public const int MaxIterations = 64;

    public static PhysicsSettings Default { get; } = new(new Vector3(0, -2 * 9.82f, 0), SubSteps: 1, SolverIterations: 6, RelaxationIterations: 4);

    internal void Validate()
    {
        Vectors.ThrowIfNotFinite(Gravity, nameof(Gravity));
        ArgumentOutOfRangeException.ThrowIfLessThan(SubSteps, 1, nameof(SubSteps));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(SubSteps, MaxIterations, nameof(SubSteps));
        ArgumentOutOfRangeException.ThrowIfLessThan(SolverIterations, 1, nameof(SolverIterations));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(SolverIterations, MaxIterations, nameof(SolverIterations));
        ArgumentOutOfRangeException.ThrowIfNegative(RelaxationIterations, nameof(RelaxationIterations));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(RelaxationIterations, MaxIterations, nameof(RelaxationIterations));
    }
}
