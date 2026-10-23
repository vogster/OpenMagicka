using System.Numerics;

namespace OpenMagicka.Gameplay.Entities;

/// <summary>
/// The spatial index (docs/engine/ENTITIES_AI.md §1.6): 16 × 16 cells of 8 m on X and Z, indices
/// taken modulo 16, so the grid wraps and far entities share cells; queries filter by distance.
/// Rebuilt from scratch once a frame.
/// </summary>
internal sealed class EntityGrid
{
    public const int Cells = 16;

    public const float CellSize = 8;

    /// <summary>An entity spans at most this many cells either side of its centre.</summary>
    public const float MaxReachCells = 8;

    /// <summary>Positions and radii beyond this are left out: one runaway value must not stall the grid.</summary>
    public const float Limit = 1e7f;

    private readonly List<Entity>[] cells = [.. Enumerable.Range(0, Cells * Cells).Select(_ => new List<Entity>())];

    public void Rebuild(IReadOnlyList<Entity> entities)
    {
        foreach (var cell in cells)
        {
            cell.Clear();
        }
        foreach (var entity in entities)
        {
            Place(entity);
        }
    }

    /// <summary>Each cell the entity's circle covers, by columns: the rows reach the chord at the column's nearer edge (the full span in the centre's column).</summary>
    private void Place(Entity entity)
    {
        var position = entity.Position;
        if (!Within(position.X) || !Within(position.Z) || !Within(entity.Radius))
        {
            return;
        }
        var (x, z) = (position.X / CellSize, position.Z / CellSize);
        var r = MathF.Max(0, entity.Radius / CellSize);
        var half = MathF.Min(r, MaxReachCells);
        for (var column = (int)MathF.Floor(x - half); column <= (int)MathF.Floor(x + half); column++)
        {
            float edge = MathF.Abs(column - x) < MathF.Abs(column + 1 - x) ? column : column + 1;
            var chord = MathF.Sqrt(r * r - (edge - x) * (edge - x));
            var holdsCentre = column <= x && x < column + 1;                       // the original under-covers this column
            var extent = holdsCentre ? half : MathF.Min(float.IsNaN(chord) ? r : chord, half);
            for (var row = (int)MathF.Floor(z - extent); row <= (int)MathF.Floor(z + extent); row++)
            {
                cells[Cell(column, row)].Add(entity);
            }
        }
    }

    /// <summary>The entities in the cells a query of <paramref name="radius"/> around <paramref name="center"/> touches, each once.</summary>
    public List<Entity> Near(Vector3 center, float radius)
    {
        var (fromX, toX) = Span(center.X, radius);
        var (fromZ, toZ) = Span(center.Z, radius);
        var seen = new HashSet<Entity>(ReferenceEqualityComparer.Instance);
        var found = new List<Entity>();
        for (var column = fromX; column <= toX; column++)
        {
            for (var row = fromZ; row <= toZ; row++)
            {
                foreach (var entity in cells[Cell(column, row)])
                {
                    if (seen.Add(entity))
                    {
                        found.Add(entity);
                    }
                }
            }
        }
        return found;
    }

    /// <summary>The cells a query spans on one axis; all of them when it is wider than the grid.</summary>
    private static (int From, int To) Span(float center, float radius)
    {
        var (from, to) = (Math.Floor((center - (double)radius) / CellSize), Math.Floor((center + (double)radius) / CellSize));
        return to - from > Cells || !Within(center) ? (0, Cells - 1) : ((int)from, (int)to);
    }

    private static bool Within(float value) => float.IsFinite(value) && MathF.Abs(value) <= Limit;

    private static int Cell(int column, int row) => Wrap(column) * Cells + Wrap(row);

    private static int Wrap(int index) => ((index % Cells) + Cells) % Cells;
}
