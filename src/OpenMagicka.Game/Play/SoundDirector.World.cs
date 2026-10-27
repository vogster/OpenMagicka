using OpenMagicka.Engine.Audio;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using Barrier = OpenMagicka.Gameplay.Spells.Effects.Barrier;
using SoundBanks = OpenMagicka.Content.Magicka.SoundBanks;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The sounds the original's code plays of the world's state (docs/engine/ARCHITECTURE.md §8), read here each tick:
/// a character's own sounds while it lives, its burning, dripping and frozen loops (started as the status comes,
/// following it; steam hisses as the wet dry, the frozen thaw and the burning are put out by water), its shattering;
/// a held item's sounds (paused, not played, for an item that says so); a shield's hum (its health a variable), a
/// barrier's loops, a mine's placing. A player's element is the <see cref="Gameplay.Animation.ConjureCue"/>'s.
/// </summary>
public sealed partial class SoundDirector
{
    private static readonly (StatusEffects Status, string Cue)[] StatusLoops =
        [(StatusEffects.Burning, "spell_fire_onfire"), (StatusEffects.Wet, "spell_water_drip"), (StatusEffects.Frozen, "spell_ice_frozen")];

    private readonly Dictionary<Entity, Heard> heard = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Item, HeldSounds> held = new(ReferenceEqualityComparer.Instance);
    private readonly List<Entity> goneEntities = [];
    private readonly List<Item> goneItems = [];
    private int tick;

    /// <summary>What an entity sounds of now, and when it was last seen.</summary>
    private sealed class Heard
    {
        public List<ISoundCue> Own { get; } = [];

        public ISoundCue?[] Statuses { get; } = new ISoundCue?[StatusLoops.Length];

        /// <summary>Which statuses it had last tick (their loops start as they come).</summary>
        public bool[] Had { get; } = new bool[StatusLoops.Length];

        public bool Dead { get; set; }

        public bool Shattered { get; set; }

        /// <summary>A beam's loop where it hits, while it is cut short.</summary>
        public ISoundCue? Loop { get; set; }

        public int Seen { get; set; }

        /// <summary>Where it was last seen (a shattered one is gone the tick it shatters).</summary>
        public SoundPlace At { get; set; }
    }

    /// <summary>A held item's cues by its sounds, and those its banks lack (not asked for again).</summary>
    private sealed class HeldSounds(int count)
    {
        public ISoundCue?[] Cues { get; } = new ISoundCue?[count];

        public bool[] Missing { get; } = new bool[count];

        public int Seen { get; set; }
    }

    /// <summary>The UI cue of an element going into a player's queue (Ice, Steam and Poison sound as Cold, Fire and Water).</summary>
    public static string? ElementCue(Elements element) => element switch
    {
        Elements.Ice => "ui_element_cold",
        Elements.Steam => "ui_element_fire",
        Elements.Poison => "ui_element_water",
        Elements.Earth or Elements.Water or Elements.Cold or Elements.Fire or Elements.Lightning or Elements.Arcane or Elements.Life or Elements.Shield
            => "ui_element_" + element.ToString().ToLowerInvariant(),
        _ => null,
    };

    /// <summary>A barrier's loops: Earth and Ice as one, then each other element's.</summary>
    public static IEnumerable<string> BarrierCues(Elements elements)
    {
        var (earth, ice) = (elements.HasFlag(Elements.Earth), elements.HasFlag(Elements.Ice));
        if (earth || ice)
        {
            yield return earth && ice ? "spell_ice_earth_barrier" : earth ? "spell_earth_barrier" : "spell_ice_barrier";
        }
        foreach (var other in new[] { Elements.Water, Elements.Cold, Elements.Fire, Elements.Lightning, Elements.Arcane, Elements.Life, Elements.Steam, Elements.Poison })
        {
            if (elements.HasFlag(other))
            {
                yield return $"spell_{other.ToString().ToLowerInvariant()}_barrier";
            }
        }
    }

    private void StepWorld()
    {
        tick++;
        foreach (var entity in world.Entities.Entities)
        {
            if (entity is not (Character or SpellShield or Barrier or SpellMine or Beam))
            {
                continue;
            }
            var first = !heard.TryGetValue(entity, out var state);
            state ??= heard[entity] = new Heard();
            state.Seen = tick;
            var at = state.At = SoundPlace.At(entity.Position, Forward(entity));
            switch (entity)
            {
                case Character character:
                    StepCharacter(character, state, first, at);
                    break;
                case SpellShield shield:
                    var health = ("Health", shield.HitPoints / SpellShield.MaxHitPoints);
                    if (first)
                    {
                        Keep(state, Sounds.Play(SoundBanks.Spells, "spell_shield", at, health));
                    }
                    Follow(state, at, health);
                    break;
                case Barrier barrier:
                    if (first)
                    {
                        foreach (var cue in BarrierCues(barrier.Spell.Element))
                        {
                            Keep(state, Sounds.Play(SoundBanks.Spells, cue, at));
                        }
                    }
                    Follow(state, at);
                    break;
                case SpellMine when first:
                    Sounds.Play(SoundBanks.Spells, "spell_mine01", at);
                    break;
                case Beam beam:
                    StepBeam(beam, state, first, at);
                    break;
            }
        }
        LetGoOfTheGone();
    }

    private void LetGoOfTheGone()
    {
        goneEntities.Clear();
        foreach (var (entity, state) in heard)
        {
            if (state.Seen != tick)
            {
                StopAll(state);
                goneEntities.Add(entity);
                if (entity is Character { IsOverkilled: true } && !state.Shattered)
                {
                    Sounds.Play(SoundBanks.Misc, "misc_gib", state.At);
                }
            }
        }
        goneEntities.ForEach(e => heard.Remove(e));
        goneItems.Clear();
        foreach (var (item, sounds) in held)
        {
            if (sounds.Seen != tick)
            {
                foreach (var cue in sounds.Cues)
                {
                    cue?.Stop(immediate: false);
                }
                goneItems.Add(item);
            }
        }
        goneItems.ForEach(i => held.Remove(i));
    }

    private void StepCharacter(Character character, Heard state, bool first, SoundPlace at)
    {
        if (character.IsOverkilled && !state.Shattered)
        {
            state.Shattered = true;
            Sounds.Play(SoundBanks.Misc, "misc_gib", at);
        }
        if (character.IsDead)
        {
            if (!state.Dead)
            {
                state.Dead = true;
                StopAll(state);
            }
            return;
        }
        if (first || state.Dead)
        {
            state.Dead = false;                                               // alive, or risen again
            foreach (var sound in character.Template.Sounds)
            {
                Keep(state, Sounds.Play((SoundBanks)sound.Banks, sound.Cue, at));
            }
        }
        StepStatuses(character, state, at);
        for (var slot = 0; slot < Character.Slots; slot++)
        {
            if (character.ItemIn(slot) is { Template.Sounds.Count: > 0, IsAttached: true } item)
            {
                Hold(item, SoundPlace.At(item.Position, Forward(character)));
            }
        }
    }

    private void StepStatuses(Character character, Heard state, SoundPlace at)
    {
        for (var i = 0; i < StatusLoops.Length; i++)
        {
            var (status, cue) = StatusLoops[i];
            var has = character.Statuses.Has(status);
            if (has && !state.Had[i])
            {
                state.Statuses[i] = Sounds.Play(SoundBanks.Spells, cue, at);
            }
            else if (has && state.Statuses[i] is { } loop)
            {
                Sounds.Place(loop, at);
            }
            else if (!has && state.Had[i])
            {
                state.Statuses[i]?.Stop(immediate: false);
                state.Statuses[i] = null;
                if (status != StatusEffects.Burning || character.Statuses.Has(StatusEffects.Wet))
                {
                    Sounds.Play(SoundBanks.Spells, "spell_steam_hit", at);    // dried, thawed, or put out by water
                }
            }
            state.Had[i] = has;
        }
    }

    /// <summary>A held item's sounds go on: each one ended (or paused, for an item that does not pause them) is started again where the item is.</summary>
    private void Hold(Item item, SoundPlace at)
    {
        var sounds = item.Template.Sounds;
        var state = held.TryGetValue(item, out var known) ? known : held[item] = new HeldSounds(sounds.Count);
        state.Seen = tick;
        for (var i = 0; i < sounds.Count; i++)
        {
            var cue = state.Cues[i];
            if (state.Missing[i])
            {
                continue;
            }
            if (item.Template.PauseSounds)
            {
                cue?.Pause();                                                 // the original pauses them rather than play them
                continue;
            }
            if (cue is { IsPaused: true })
            {
                cue.Resume();
            }
            else if (cue is null or { IsStopped: true })
            {
                state.Cues[i] = Sounds.Play((SoundBanks)sounds[i].Banks, sounds[i].Cue, at);
                state.Missing[i] = state.Cues[i] is null;
            }
        }
    }

    private static void Keep(Heard state, ISoundCue? cue)
    {
        if (cue is not null)
        {
            state.Own.Add(cue);
        }
    }

    private void Follow(Heard state, SoundPlace at, params ReadOnlySpan<(string Name, float Value)> variables)
    {
        foreach (var cue in state.Own)
        {
            Sounds.Place(cue, at);
            foreach (var (name, value) in variables)
            {
                cue.SetVariable(name, value);
            }
        }
    }

    /// <summary>
    /// A beam as the original's sounds it: at its start its stage-one cue and its extra elements' sprays (at magnitude 1),
    /// and while something cuts it short, its stage-two loop where it hits; a reflection is a beam of its own.
    /// </summary>
    private void StepBeam(Beam beam, Heard state, bool first, SoundPlace at)
    {
        var kind = beam.IsArcane ? "arcane" : "life";
        if (first)
        {
            Keep(state, Sounds.Play(SoundBanks.Spells, $"spell_{kind}_ray_stage1", at));
            foreach (var spray in SpellSounds.ExtraSprays(beam.ExtraElements))
            {
                Keep(state, Sounds.Play(SoundBanks.Spells, spray, at, ("Magnitude", 1)));
            }
        }
        if (beam.IsHitting && !beam.IsDead)
        {
            var end = SoundPlace.At(beam.Position + beam.Direction * beam.Length, beam.Direction);
            if (state.Loop is { IsStopped: false } loop)
            {
                Sounds.Place(loop, end);
            }
            else
            {
                state.Loop = Sounds.Play(SoundBanks.Spells, $"spell_{kind}_ray_stage2", end);
            }
        }
        else if (state.Loop is { } loop)
        {
            loop.Stop(immediate: false);
            state.Loop = null;
        }
    }

    private static void StopAll(Heard state)
    {
        state.Loop?.Stop(immediate: false);
        state.Loop = null;
        foreach (var cue in state.Own)
        {
            cue.Stop(immediate: false);
        }
        foreach (var cue in state.Statuses)
        {
            cue?.Stop(immediate: false);
        }
        state.Own.Clear();
        Array.Clear(state.Statuses);
        Array.Clear(state.Had);
    }

    /// <summary>A restart: everything heard is forgotten (its cues stopped already), to be heard anew.</summary>
    private void ForgetTheWorld()
    {
        heard.Clear();
        held.Clear();
    }
}
