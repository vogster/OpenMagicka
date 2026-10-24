using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>
/// What a magick does when cast (docs/gameplay/MAGICKS.md): its caster plays <see cref="Animation"/>,
/// whose <c>CastSpell</c> action runs <see cref="Execute"/>. One instance serves every caster.
/// </summary>
public abstract class Magick
{
    /// <summary>The cast clips the original's magicks use.</summary>
    public const string SelfClip = "cast_magick_self", DirectClip = "cast_magick_direct", SweepClip = "cast_magick_sweep", GlobalClip = "cast_magick_global";

    protected Magick(MagickType type, string animation)
    {
        (Type, Animation) = (type, animation);
    }

    public MagickType Type { get; }

    /// <summary>The cast clip.</summary>
    public string Animation { get; }

    /// <summary>Casts it; false when it could not (most say so with <see cref="Fail"/>, as the original).</summary>
    public abstract bool Execute(Character caster);

    /// <summary>The magick could not work: the caster hears <see cref="MagickEffects.FailSound"/>.</summary>
    protected static bool Fail(Character caster)
    {
        Sound(caster, MagickEffects.FailSound);
        return false;
    }

    /// <summary>A spell-bank sound at the character.</summary>
    protected static void Sound(Character character, string cue) =>
        character.RaiseCue(new SoundCue(cue, MagickEffects.SpellsBank, character.Position, character.Body.Facing));
}

/// <summary>A magick's effect running in the level, which Nullify ends at once.</summary>
public interface IMagickEffect
{
    void End();
}

/// <summary>The magicks that work so far, and which are instant (docs/gameplay/MAGICKS.md §2).</summary>
public static class MagickEffects
{
    public const string FailSound = "magick_fail";

    /// <summary>The original's Spells and Additional sound banks.</summary>
    public const int SpellsBank = 0x10, AdditionalBank = 0x200;

    private static readonly Dictionary<MagickType, Magick> Known = new Magick[]
    {
        new Haste(),
        new Invisibility(),
        new Levitate(),
        new Teleport(),
        new Revive(),
        new ChainLightning(),
        new Thunderbolt(),
        new CrashToDesktop(),
        new Conflagration(),
        new MeteorShower(),
        new Napalm(),
        new PerformanceEnchantment(),
        new Amalgameddon(),
        new JudgementSpray(),
        new Wave(),
        new Rain(),
        new Blizzard(),
        new Thunderstorm(),
        new TimeWarp(),
        new TractorPull(),
        new ProppMagick(),
        new Grease(),
        new Tornado(),
        new Vortex(),
        new Fear(),
        new Charm(),
        new Confuse(),
        new Nullify(),
        new Corporealize(),
    }.ToDictionary(m => m.Type);

    /// <summary>The magick's effect; null for one that does nothing yet.</summary>
    public static Magick? For(MagickType type) => Known.GetValueOrDefault(type);

    /// <summary>Instant magicks are cast at once even while frozen, flying or knocked down.</summary>
    public static bool IsInstant(MagickType type) =>
        type is MagickType.Revive or MagickType.Haste or MagickType.Invisibility or MagickType.Teleport or MagickType.Napalm or MagickType.ChainLightning;
}
