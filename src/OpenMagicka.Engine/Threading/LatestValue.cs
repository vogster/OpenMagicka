namespace OpenMagicka.Engine.Threading;

/// <summary>
/// The most recent value one thread hands to another (input state from the render thread to
/// the logic thread): readers always see a whole value, never a torn one, and never wait.
/// </summary>
public sealed class LatestValue<T>(T initial)
{
    private readonly Lock gate = new();
    private T value = initial;

    public T Value
    {
        get
        {
            lock (gate)
            {
                return value;
            }
        }
        set
        {
            lock (gate)
            {
                this.value = value;
            }
        }
    }
}
