namespace OpenMagicka.Gameplay.AI.Fuzzy;

/// <summary>
/// A fuzzy expression from a character template: how much an ability is wanted against a target
/// (ENTITIES_AI.md §7.5). A tree of numbers, variables and the operators + − * / ^ and the unary !.
/// </summary>
public abstract class FuzzyExpression
{
    public abstract float Value(in FuzzyArguments args);

    /// <summary>
    /// Reads an expression as the original does (or, for comparison, with operators of one rank
    /// taken left to right); throws <see cref="FormatException"/> on bad syntax.
    /// </summary>
    public static FuzzyExpression Parse(string text, bool leftToRight = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        return FuzzyParser.Parse(text, leftToRight);
    }
}

public sealed class FuzzyConstant(float value) : FuzzyExpression
{
    public float Number { get; } = value;

    public override float Value(in FuzzyArguments args) => Number;

    public override string ToString() => Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The unary !: one minus its operand.</summary>
public sealed class FuzzyNot(FuzzyExpression operand) : FuzzyExpression
{
    public FuzzyExpression Operand { get; } = operand;

    public override float Value(in FuzzyArguments args) => 1 - Operand.Value(args);

    public override string ToString() => $"!{Operand}";
}

public sealed class FuzzyBinary(char op, FuzzyExpression left, FuzzyExpression right) : FuzzyExpression
{
    public char Operator { get; } = op is '+' or '-' or '*' or '/' or '^' ? op : throw new ArgumentOutOfRangeException(nameof(op));

    public FuzzyExpression Left { get; } = left;

    public FuzzyExpression Right { get; } = right;

    public override float Value(in FuzzyArguments args)
    {
        var (a, b) = (Left.Value(args), Right.Value(args));
        return Operator switch
        {
            '+' => a + b,
            '-' => a - b,
            '*' => a * b,
            '/' => a / b,
            _ => MathF.Pow(a, b),
        };
    }

    public override string ToString() => $"({Left} {Operator} {Right})";
}
