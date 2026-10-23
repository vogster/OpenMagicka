using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The per-frame values the original shared between all effects through an EffectPool, which
/// XNA 4.0 and FNA do not have (docs/engine/ARCHITECTURE.md §4.3): every effect gets them set
/// before it is applied. The derived matrices follow View and Projection, also after <c>with</c>.
/// </summary>
public sealed record GlobalEffectValues(Matrix View, Matrix Projection, Vector3 CameraPosition, Vector3 EyeOfTheBeholder, float Time)
{
    public Matrix ViewProjection => View * Projection;

    public Matrix InverseViewProjection => Matrix.Invert(ViewProjection);
}

/// <summary>Named effect parameters; implemented over an FNA effect, faked in tests.</summary>
public interface IEffectParameters
{
    bool Contains(string name);

    void SetValue(string name, Matrix value);

    void SetValue(string name, Vector3 value);

    void SetValue(string name, float value);

    void SetValue(string name, bool value);

    void SetValue(string name, Vector2 value);

    void SetValue(string name, Vector4 value);

    void SetValue(string name, Texture? value);
}

/// <summary>
/// Sets the global values on one effect: only on parameters that have a global's name and that
/// the effect marks shared. Same-named parameters that are not shared (the Time of
/// RenderDeferredEffect, the View of RadialBlur) belong to the material, as with the EffectPool.
/// Shared per-object parameters (World, CubeMapRotation, ProjectionMapMatrix) are not globals.
/// </summary>
public sealed class GlobalEffectBinding
{
    public const string View = "View";
    public const string Projection = "Projection";
    public const string ViewProjection = "ViewProjection";
    public const string InverseViewProjection = "InverseViewProjection";
    public const string CameraPosition = "CameraPosition";
    public const string EyeOfTheBeholder = "EyeOfTheBeholder";
    public const string Time = "Time";

    private static readonly string[] Globals = [View, Projection, ViewProjection, InverseViewProjection, CameraPosition, EyeOfTheBeholder, Time];

    private readonly IEffectParameters parameters;

    /// <param name="shared">The names of the parameters the effect marks shared.</param>
    public GlobalEffectBinding(IEffectParameters parameters, IEnumerable<string> shared)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(shared);
        this.parameters = parameters;
        var sharedNames = shared.ToHashSet(StringComparer.Ordinal);
        Bound = Globals.Where(name => sharedNames.Contains(name) && parameters.Contains(name)).ToArray();
    }

    /// <summary>The global parameters this effect gets.</summary>
    public IReadOnlyList<string> Bound { get; }

    public void Apply(GlobalEffectValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var name in Bound)
        {
            switch (name)
            {
                case View: parameters.SetValue(name, values.View); break;
                case Projection: parameters.SetValue(name, values.Projection); break;
                case ViewProjection: parameters.SetValue(name, values.ViewProjection); break;
                case InverseViewProjection: parameters.SetValue(name, values.InverseViewProjection); break;
                case CameraPosition: parameters.SetValue(name, values.CameraPosition); break;
                case EyeOfTheBeholder: parameters.SetValue(name, values.EyeOfTheBeholder); break;
                case Time: parameters.SetValue(name, values.Time); break;
            }
        }
    }
}

/// <summary>The parameters of an FNA effect, looked up once by name (FNA searches linearly).</summary>
public sealed class FnaEffectParameters(Effect effect) : IEffectParameters
{
    private readonly Dictionary<string, EffectParameter?> found = new(StringComparer.Ordinal);

    public bool Contains(string name) => Find(name) is not null;

    public void SetValue(string name, Matrix value) => Required(name).SetValue(value);

    public void SetValue(string name, Vector3 value) => Required(name).SetValue(value);

    public void SetValue(string name, float value) => Required(name).SetValue(value);

    public void SetValue(string name, bool value) => Required(name).SetValue(value);

    public void SetValue(string name, Vector2 value) => Required(name).SetValue(value);

    public void SetValue(string name, Vector4 value) => Required(name).SetValue(value);

    public void SetValue(string name, Texture? value) => Required(name).SetValue(value);

    private EffectParameter? Find(string name)
    {
        if (!found.TryGetValue(name, out var parameter))
        {
            found[name] = parameter = effect.Parameters[name];
        }
        return parameter;
    }

    private EffectParameter Required(string name) =>
        Find(name) ?? throw new KeyNotFoundException($"the effect has no parameter {name}");
}
