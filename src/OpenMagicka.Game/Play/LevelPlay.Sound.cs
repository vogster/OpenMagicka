using OpenMagicka.Engine.Audio;
using OpenMagicka.Engine.Scripting;

namespace OpenMagicka.Game.Play;

/// <summary>A level's sound (OM-51): its director takes the scripts' sound requests and steps after the camera.</summary>
public sealed partial class LevelPlay
{
    /// <summary>The level's sounds; none while the level plays silent.</summary>
    public SoundDirector? Sound { get; private set; }

    private bool ownsSounds;

    /// <summary>The level is heard through the game's sound (which it then owns, unless the game keeps it: the menu's).</summary>
    public void Hear(SoundSystem sounds, bool owned = true)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        if (Sound is not null)
        {
            throw new InvalidOperationException("the level is heard already");
        }
        Sound = new SoundDirector(sounds, World, Level);
        ownsSounds = owned;
        Level.Game.Sounds = Sound;
    }

    private bool TakeSound(ScriptRequest request) => Sound?.Take(request) ?? false;

    private void StepSound(float dt) => Sound?.Update(dt, Camera);

    private void DisposeSound()
    {
        if (Sound is { } sound)
        {
            sound.Dispose();
            if (ownsSounds)
            {
                sound.Sounds.Dispose();
            }
        }
    }
}
