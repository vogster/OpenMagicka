using Microsoft.Xna.Framework.Audio;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Audio;

/// <summary>An XACT cue. One not placed before it played stays where XACT put it (FNA allows no later placing).</summary>
internal sealed class XactCue(Cue cue) : ISoundCue
{
    private readonly AudioListener listener = new();
    private readonly AudioEmitter emitter = new();
    private bool placed, played;

    public string Name => cue.Name;

    public bool IsPlaying => !cue.IsDisposed && cue.IsPlaying;

    public bool IsPaused => !cue.IsDisposed && cue.IsPaused;

    public bool IsStopping => !cue.IsDisposed && cue.IsStopping;

    public bool IsStopped => cue.IsDisposed || cue.IsStopped;

    public void Play()
    {
        if (!cue.IsDisposed && !played)
        {
            played = true;
            cue.Play();
        }
    }

    public void Stop(bool immediate)
    {
        if (!cue.IsDisposed)
        {
            cue.Stop(immediate ? AudioStopOptions.Immediate : AudioStopOptions.AsAuthored);
        }
    }

    public void Pause()
    {
        if (!cue.IsDisposed && cue.IsPlaying)
        {
            cue.Pause();
        }
    }

    public void Resume()
    {
        if (!cue.IsDisposed && cue.IsPaused)
        {
            cue.Resume();
        }
    }

    public void SetVariable(string name, float value)
    {
        if (cue.IsDisposed)
        {
            return;
        }
        try
        {
            cue.SetVariable(name, value);
        }
        catch (IndexOutOfRangeException)
        {
        }
    }

    public void Apply3D(SoundPlace listener, SoundPlace emitter)
    {
        if (cue.IsDisposed || (played && !placed))
        {
            return;
        }
        Set(this.listener, listener);
        Set(this.emitter, emitter);
        cue.Apply3D(this.listener, this.emitter);
        placed = true;
    }

    private static void Set(AudioListener to, SoundPlace place) => (to.Position, to.Forward, to.Up) = (ToXna(place.Position), ToXna(place.Forward), ToXna(place.Up));

    private static void Set(AudioEmitter to, SoundPlace place) => (to.Position, to.Forward, to.Up) = (ToXna(place.Position), ToXna(place.Forward), ToXna(place.Up));

    private static Xna.Vector3 ToXna(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public void Dispose() => cue.Dispose();
}
