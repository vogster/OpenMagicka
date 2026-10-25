using System.Runtime.InteropServices;
using System.Text;
using OpenMagicka.Game.Install;
using static SDL3.SDL;

namespace OpenMagicka.Launcher;

/// <summary>
/// SDL's own boxes and folder dialog, before the game's window is up (OM-53): SDL's video is started for them and
/// stopped after. The folder dialog answers through a callback (on some systems from another thread), so the events are
/// pumped until it has.
/// </summary>
internal sealed unsafe class SdlQuestions : IInstallQuestions, IDisposable
{
    public const string Title = "OpenMagicka";

    private readonly bool started = SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_VIDEO);

    public int Ask(string message, IReadOnlyList<string> buttons)
    {
        var texts = new List<IntPtr>();
        try
        {
            IntPtr Utf8(string text)
            {
                var pointer = Marshal.StringToCoTaskMemUTF8(text);
                texts.Add(pointer);
                return pointer;
            }
            var data = new SDL_MessageBoxButtonData[buttons.Count];
            for (var i = 0; i < buttons.Count; i++)
            {
                data[i] = new SDL_MessageBoxButtonData
                {
                    buttonID = i,
                    text = (byte*)Utf8(buttons[i]),
                    flags = i == 0 ? SDL_MessageBoxButtonFlags.SDL_MESSAGEBOX_BUTTON_RETURNKEY_DEFAULT
                        : i == buttons.Count - 1 ? SDL_MessageBoxButtonFlags.SDL_MESSAGEBOX_BUTTON_ESCAPEKEY_DEFAULT : 0,
                };
            }
            fixed (SDL_MessageBoxButtonData* pointer = data)
            {
                var box = new SDL_MessageBoxData
                {
                    flags = SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION,
                    title = (byte*)Utf8(Title),
                    message = (byte*)Utf8(message),
                    numbuttons = data.Length,
                    buttons = pointer,
                };
                return SDL_ShowMessageBox(ref box, out var chosen) ? chosen : -1;
            }
        }
        finally
        {
            texts.ForEach(Marshal.FreeCoTaskMem);
        }
    }

    public FolderAnswer PickFolder(string? start)
    {
        using var answered = new ManualResetEventSlim();
        var answer = FolderAnswer.Cancelled;
        SDL_DialogFileCallback callback = (_, files, _) =>
        {
            answer = files == IntPtr.Zero ? FolderAnswer.Failed(SDL_GetError() is { Length: > 0 } error ? error : "unknown error")
                : Marshal.ReadIntPtr(files) is var first && first != IntPtr.Zero ? new FolderAnswer(Marshal.PtrToStringUTF8(first))
                : FolderAnswer.Cancelled;
            answered.Set();
        };
        SDL_ShowOpenFolderDialog(callback, IntPtr.Zero, IntPtr.Zero, start ?? DefaultStart(), false);
        while (!answered.Wait(10))
        {
            SDL_PumpEvents();
        }
        GC.KeepAlive(callback);
        return answer;
    }

    public void Tell(string message) => SDL_ShowSimpleMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, Title, message, IntPtr.Zero);

    /// <summary>Where games usually are: Program Files on Windows, the home folder elsewhere.</summary>
    private static string DefaultStart() => OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public void Dispose()
    {
        if (started)
        {
            SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_VIDEO);
        }
    }
}
