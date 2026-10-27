using System.Numerics;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using Barrier = OpenMagicka.Gameplay.Spells.Effects.Barrier;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The solid barriers' and the mines' models, as the original's (docs/engine/ARCHITECTURE.md §17): skinned models
/// posed by their clips here and drawn as the entities' models are. A barrier rises out of the ground by a random
/// clip of its kind at twice its speed and sinks back by the first played backwards; its jet (a geyser's, a tesla's,
/// a volcano's) plays from its <c>effect0</c> bone while it strikes; its runes turn under it. A mine unfolds once.
/// </summary>
public sealed partial class SpellVisuals
{
    public const float BarrierClipSpeed = 2, MineClipSpeed = 3, MineScale = 2.1f, MineGlow = 2, RuneTurn = -0.25f;

    public const string EffectModels = "Models/Effects/";

    /// <summary>A frog's tongue: a skinned strip whose two joints are its ends.</summary>
    public const string TongueModel = "Models/Missiles/Tongue";

    private readonly Func<string, AnimatedModel?> models;
    private readonly Dictionary<Barrier, List<Posed>> barrierModels = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SpellMine, Posed> mineModels = new(ReferenceEqualityComparer.Instance);

    /// <summary>A barrier's jet, from its model's <c>effect0</c> bone.</summary>
    private sealed record Jet(Barrier Barrier);
    private float runeTurn;

    /// <summary>A skinned model at play: its mesh, bones and the clip it plays (backwards at a negative speed).</summary>
    public sealed class Posed(string asset, AnimatedModel model, AnimationClip clip, float speed, Matrix4x4 root)
    {
        public string Asset { get; } = asset;
        public AnimatedModel Model { get; } = model;
        public AnimationClip Clip { get; private set; } = clip;
        public float Speed { get; private set; } = speed;
        public float Time { get; private set; } = speed < 0 ? clip.Duration : 0;
        public Matrix4x4 Root { get; set; } = root;
        public Matrix4x4[] Placed { get; } = new Matrix4x4[model.Skeleton.Bones.Count];
        public Matrix4x4[] Skinned { get; } = new Matrix4x4[model.Skeleton.Bones.Count];

        /// <summary>Plays another clip from its start (its end, backwards).</summary>
        public void Play(AnimationClip clip, float speed) => (Clip, Speed, Time) = (clip, speed, speed < 0 ? clip.Duration : 0);

        /// <summary>Sinks back by a clip, played backwards from where it has risen to (a barrier breaking as it rises sinks from there).</summary>
        public void Sink(AnimationClip clip) => (Clip, Speed, Time) = (clip, -BarrierClipSpeed, Math.Min(Time, clip.Duration));

        /// <summary>A frame: its time runs on, held at the clip's ends; its bones posed by the clip's channels, the others at bind.</summary>
        public void Update(float dt)
        {
            Time = Math.Clamp(Time + dt * Speed, 0, Clip.Duration);
            var bones = Model.Skeleton.Bones;
            Span<BonePose> poses = stackalloc BonePose[bones.Count];
            for (var i = 0; i < bones.Count; i++)
            {
                poses[i] = Clip.Channels?.TryGetValue(bones[i].Name, out var keys) == true ? Skeleton.ChannelAt(keys, Time, Clip.Duration, false) : bones[i].BindPose;
            }
            Model.Skeleton.Place(poses, Root, Placed, Skinned);
        }
    }

    /// <summary>Every model the barriers, mines and armours are drawn as (and the clips they play), to read before play.</summary>
    public static IEnumerable<string> ModelAssets =>
    [
        .. new[] { "iceBarrier0_mesh", "iceBarrier_animation", "icebarrier_geyser", "icebarrier_tesla", "iceBarrier_Earth", "earthBarrier0_mesh",
            "earthBarrier_animation", "earthBarrier_volcano", "mine" }.Select(m => EffectModels + m),
        TongueModel,
    ];

    /// <summary>A barrier's model as it stands: scaled by the barrier, along its way, its origin on the ground.</summary>
    public static Matrix4x4 BarrierRoot(Barrier barrier)
    {
        ArgumentNullException.ThrowIfNull(barrier);
        var flat = barrier.Facing with { Y = 0 };
        var forward = flat.LengthSquared() > 1e-12f ? Vector3.Normalize(flat) : -Vector3.UnitZ;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var turn = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, -forward.X, -forward.Y, -forward.Z, 0, 0, 0, 0, 1);
        return Matrix4x4.CreateScale(barrier.Scale) * turn with { Translation = barrier.Ground + Vector3.UnitY * (Barrier.Lift * (1 - barrier.Scale)) };
    }

    /// <summary>
    /// A solid barrier's models, as the original picks them: its ice (a geyser's with a sprayed element but no Arcane or
    /// Life, a tesla's with Lightning, a shell over rock with Earth) and its rock (a volcano's with a sprayed element but no
    /// Arcane or Life; the plain rock is always the first, as the original's pick); each with the file of clips it rises by.
    /// </summary>
    public static IEnumerable<(string Mesh, string Clips)> BarrierModels(Spell spell)
    {
        var sprayed = (spell.Element & (Elements.Water | Elements.Cold | Elements.Fire | Elements.Lightning | Elements.Steam | Elements.Poison)) != 0
            && (spell.Element & ElementSets.Beams) == 0;
        if (spell.Ice > 0)
        {
            yield return spell.Earth > 0 ? (EffectModels + "iceBarrier_Earth", EffectModels + "iceBarrier_Earth")
                : sprayed && spell.Lightning > 0 ? (EffectModels + "icebarrier_tesla", EffectModels + "icebarrier_tesla")
                : sprayed ? (EffectModels + "icebarrier_geyser", EffectModels + "icebarrier_geyser")
                : (EffectModels + "iceBarrier0_mesh", EffectModels + "iceBarrier_animation");
        }
        if (spell.Earth > 0)
        {
            var volcano = (spell.Element & (Elements.Water | Elements.Cold | Elements.Fire | Elements.Steam | Elements.Poison)) != 0
                && (spell.Element & ElementSets.Beams) == 0;
            yield return volcano ? (EffectModels + "earthBarrier_volcano", EffectModels + "earthBarrier_volcano") : (EffectModels + "earthBarrier0_mesh", EffectModels + "earthBarrier_animation");
        }
    }

    private void AgeModels(float dt)
    {
        runeTurn = (runeTurn + RuneTurn * dt) % MathF.Tau;
        foreach (var barrier in world.Entities.Entities.OfType<Barrier>().Where(b => b.IsSolid && !barrierModels.ContainsKey(b)))
        {
            var posed = BarrierModels(barrier.Spell).Select(m => Raise(m.Mesh, m.Clips, BarrierRoot(barrier))).OfType<Posed>().ToList();
            posed.ForEach(p => p.Update(0));                                  // posed before its jet takes its bone
            barrierModels[barrier] = posed;
            Spout(barrier, posed);
        }
        foreach (var (barrier, posed) in barrierModels.ToList())
        {
            if (barrier.Manager is null || barrier.IsRemovable)
            {
                barrierModels.Remove(barrier);
                continue;
            }
            if (barrier.IsDead && posed.Count > 0 && posed[0].Speed > 0)
            {
                foreach (var model in posed)
                {
                    model.Sink(SinkClip(model));
                }
            }
            foreach (var model in posed)
            {
                model.Root = BarrierRoot(barrier);
                model.Update(dt);
            }
        }
        foreach (var mine in world.Entities.Entities.OfType<SpellMine>().Where(m => !mineModels.ContainsKey(m)))
        {
            var root = Matrix4x4.CreateScale(MineScale) * Matrix4x4.CreateRotationY(random.NextSingle() * MathF.Tau)
                * Matrix4x4.CreateTranslation(mine.Position - Vector3.UnitY * SpellMine.Lift);
            if (models(EffectModels + "mine") is { } mineModel && mineModel.Clips.GetValueOrDefault("Take 001") is { } unfold)
            {
                mineModels[mine] = new Posed(EffectModels + "mine", mineModel, unfold, MineClipSpeed, root);
            }
        }
        foreach (var (mine, posed) in mineModels.ToList())
        {
            if (mine.IsDead || mine.Manager is null)
            {
                mineModels.Remove(mine);
                continue;
            }
            posed.Update(dt);
        }
    }

    /// <summary>
    /// A geyser's, tesla's or volcano's jet: of its Lightning, else of the strongest of its Water, Cold, Fire and Steam,
    /// from the <c>effect0</c> bone turned about (of the ice when it has Ice), for as long as the barrier strikes.
    /// </summary>
    private void Spout(Barrier barrier, IReadOnlyList<Posed> posed)
    {
        var jetting = posed.FirstOrDefault(p => p.Model.Skeleton.IndexOf("effect0") >= 0);
        if (jetting is null || JetElement(barrier.Spell) is not { } element)
        {
            return;
        }
        var effect = element == Elements.Lightning ? "barrier_ice_tesla" : $"barrier_geyser_{element.ToString().ToLowerInvariant()}";
        var bone = jetting.Model.Skeleton.IndexOf("effect0");
        effects.Follow(new Jet(barrier), effect, () => barrier.Manager is null || barrier.IsDead || !barrier.IsStriking
            ? null
            : Matrix4x4.CreateRotationY(MathF.PI) * jetting.Placed[bone]);
    }

    /// <summary>A jet's element: Lightning when it has it, else the strongest of Water, Cold, Fire and Steam (the first of equals).</summary>
    public static Elements? JetElement(Spell spell)
    {
        if (spell.Lightning > 0)
        {
            return Elements.Lightning;
        }
        var (best, most) = ((Elements?)null, 0f);
        foreach (var element in new[] { Elements.Water, Elements.Cold, Elements.Fire, Elements.Steam })
        {
            if (spell[element] > most)
            {
                (best, most) = (element, spell[element]);
            }
        }
        return best;
    }

    /// <summary>
    /// The clip a barrier's model sinks by: the first of its plain kind's file (ice or rock), as the original's; the clip it
    /// rose by when that one moves none of its bones.
    /// </summary>
    private AnimationClip SinkClip(Posed model)
    {
        var plain = models(model.Asset.Contains("ice", StringComparison.OrdinalIgnoreCase) ? EffectModels + "iceBarrier_animation" : EffectModels + "earthBarrier_animation");
        var first = plain?.Clips.Values.OrderBy(c => c.Name, StringComparer.Ordinal).FirstOrDefault();
        return first?.Channels is { } channels && model.Model.Skeleton.Bones.Any(b => channels.ContainsKey(b.Name)) ? first : model.Clip;
    }

    /// <summary>A model rising by a random clip of its clips' file; none when the game lacks either.</summary>
    private Posed? Raise(string mesh, string clips, Matrix4x4 root)
    {
        if (models(mesh) is not { } model || models(clips) is not { Clips.Count: > 0 } source)
        {
            return null;
        }
        var all = source.Clips.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        return new Posed(mesh, model, all[random.Next(all.Count)], BarrierClipSpeed, root);
    }

    /// <summary>The barriers' and mines' models this tick, after the entities' (a mine in its spell's colour).</summary>
    public void WriteModels(List<DrawnModel> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        foreach (var (barrier, posed) in barrierModels)
        {
            foreach (var model in posed)
            {
                into.Add(new DrawnModel(model.Asset, model.Root, [.. model.Skinned], Vector3.One));
            }
            var runes = (barrier.Spell.Element & Elements.Life) != 0 ? "runesLife" : (barrier.Spell.Element & Elements.Arcane) != 0 ? "runesArcane" : null;
            if (runes is not null)
            {
                into.Add(new DrawnModel(EffectModels + runes, Matrix4x4.CreateRotationY(runeTurn) * Matrix4x4.CreateTranslation(barrier.Ground), null, Vector3.One));
            }
        }
        foreach (var (mine, posed) in mineModels)
        {
            into.Add(new DrawnModel(posed.Asset, posed.Root, [.. posed.Skinned], SpellColours.Of(mine.Spell), Emissive: MineGlow));
        }
        WriteArmours(into);
        WriteTongues(into);
    }

    /// <summary>
    /// The frogs' tongues, as the original draws them: the strip's <c>joint1</c> at the mouth and its <c>joint2</c> at the
    /// tip, both turned as the tongue was shot; its other bones (it has none) at the origin.
    /// </summary>
    private void WriteTongues(List<DrawnModel> into)
    {
        if (models(TongueModel) is not { } model)
        {
            return;
        }
        var bones = model.Skeleton.Bones;
        var (start, end) = (model.Skeleton.IndexOf("joint1"), model.Skeleton.IndexOf("joint2"));
        foreach (var tongue in world.Entities.Entities.OfType<FrogTongue>())
        {
            if (tongue.IsDead)
            {
                continue;
            }
            var skinned = new Matrix4x4[bones.Count];
            Array.Fill(skinned, Matrix4x4.Identity);
            if (start >= 0)
            {
                skinned[start] = bones[start].InverseBindPose * (tongue.Orientation with { Translation = tongue.Start });
            }
            if (end >= 0)
            {
                skinned[end] = bones[end].InverseBindPose * (tongue.Orientation with { Translation = tongue.End });
            }
            into.Add(new DrawnModel(TongueModel, Matrix4x4.CreateTranslation(tongue.Start), skinned, Vector3.One));
        }
    }
}
