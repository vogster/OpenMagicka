using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay.Animation;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// The book's states (OM-83): shut, opening, open and still, or riffling forward or back. (The original's closing,
/// reached only by going back from the bottom page, which no page of the game does, is left out.)
/// </summary>
public enum TomeBookState : byte { Closed, Opening, Open, RiffleForward, RiffleBackward }

/// <summary>What a step of the book did, for its sounds.</summary>
[Flags]
public enum TomeBookEvents : byte { None = 0, Opening = 1 }

/// <summary>
/// The Tome's book on the logic thread (OM-83, docs/engine/ARCHITECTURE.md §20, "The book"), as the original's: its
/// camera (a bone of an unseen rig, turned to look along its +Z), the candle at the rig's light bone, and the poses of
/// the book and its two stacks of pages (the page model played by the turning sheet's clips), written into each frame's
/// <see cref="BookPicture"/>, with the mouse's way onto the right page from the same camera. It starts shut, the camera
/// still far off and the candle out; on the first step the camera starts flying in, the candle lit after 1.5 s. Asked
/// to open (after a second), the fly-in hurries six times (and stays six times fast for every later camera move, as the
/// original's), and once it is over the book opens as the camera settles over the right page. Open, it riffles its pages
/// (<see cref="TomeRiffle"/>); the input is locked while sheets turn or the book moves.
/// </summary>
public sealed class TomeBook
{
    public const string Rig = "UI/ToM/background", Book = "UI/ToM/tome", Stack = "UI/ToM/page", Sheet = "UI/ToM/page_flip";

    /// <summary>The camera's lens: its field of view upright (radians), and its near and far planes.</summary>
    public const float FieldOfView = 0.75f, Near = 2, Far = 200;

    /// <summary>When the candle lights in the fly-in, and from when the book may be asked to open (seconds of the book's time).</summary>
    public const float Lighting = 1.5f, Opens = 1;

    /// <summary>How much faster the camera goes once hurried, and its blend to rest as the book opens (the clip's seconds).</summary>
    public const float Hurry = 6, Settling = 0.75f;

    private readonly AnimatedModel rig, book, stack, sheet;
    private readonly AnimationClip open, close, openLeft, openRight, closeLeft, closeRight, wakeUp, rest, flipLeft, flipRight;
    private readonly int cameraBone;
    private readonly Vector3 light;
    private readonly BonePose[] poses = new BonePose[BookPicture.PageBones];
    private readonly Matrix4x4[] placed = new Matrix4x4[BookPicture.PageBones], skinned = new Matrix4x4[BookPicture.PageBones];
    private readonly AnimationClock camera = new(), cover = new(), left = new(), right = new();
    private Matrix4x4 view;
    private float time;
    private bool hurried;

    public TomeBook(AnimatedModel rig, AnimatedModel book, AnimatedModel stack, AnimatedModel sheet, Candle candle)
    {
        (this.rig, this.book, this.stack, this.sheet) = (rig ?? throw new ArgumentNullException(nameof(rig)),
            book ?? throw new ArgumentNullException(nameof(book)), stack ?? throw new ArgumentNullException(nameof(stack)),
            sheet ?? throw new ArgumentNullException(nameof(sheet)));
        Candle = candle ?? throw new ArgumentNullException(nameof(candle));
        if (book.Skeleton.Bones.Count > BookPicture.BookBones || stack.Skeleton.Bones.Count > BookPicture.PageBones
            || sheet.Skeleton.Bones.Count > BookPicture.PageBones || rig.Skeleton.Bones.Count > BookPicture.PageBones)
        {
            throw new InvalidDataException("the book's models have more bones than it draws");
        }
        AnimationClip Clip(AnimatedModel model, string name, string what) =>
            model.Clips.GetValueOrDefault(name) ?? throw new InvalidDataException($"{what} has no clip {name}");
        (open, close) = (Clip(book, "open", Book), Clip(book, "close", Book));
        (wakeUp, rest) = (Clip(rig, "wakeup", Rig), Clip(rig, "idle", Rig));
        (openLeft, openRight) = (Clip(sheet, "open_left", Sheet), Clip(sheet, "open_right", Sheet));
        (closeLeft, closeRight) = (Clip(sheet, "close_left", Sheet), Clip(sheet, "close_right", Sheet));
        (flipLeft, flipRight) = (Clip(sheet, "flip_left", Sheet), Clip(sheet, "flip_right", Sheet));
        cameraBone = rig.Skeleton.IndexOf("camera");
        var lightBone = rig.Skeleton.IndexOf("Light");
        if (cameraBone < 0 || lightBone < 0)
        {
            throw new InvalidDataException($"{Rig} has no camera or light bone");
        }
        light = rig.Skeleton.Bones[lightBone].BindPose.Translation;           // where it is bound, unturned: it never moves
        State = TomeBookState.Closed;
        Hold(cover, close);
        Hold(left, closeLeft);
        Hold(right, closeRight);
        camera.Start(wakeUp, false);
        camera.Stop();                                                        // far off and still till the menu's first step
        Candle.Aim(0, 0.2f, 6);
        Place();
    }

    /// <summary>The book from the game's models; none, with a word why, when one is missing or does not read.</summary>
    public static TomeBook? Load(ContentLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        try
        {
            AnimatedModel Model(string asset) => AnimatedModels.From(loader.LoadUncached(asset), asset);
            return new TomeBook(Model(Rig), Model(Book), Model(Stack), Model(Sheet), new Candle(new Random()));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"OpenMagicka: the Tome's book is not shown ({e.Message}); the page lies flat");
            return null;
        }
    }

    public Candle Candle { get; }

    public TomeRiffle Riffle { get; } = new();

    public TomeBookState State { get; private set; }

    /// <summary>The book lies open, still: pages may be turned to.</summary>
    public bool IsOpen => State == TomeBookState.Open;

    /// <summary>The book is shut or opening: its page takes no mouse (shut, it shows bare paper).</summary>
    public bool IsShut => State is TomeBookState.Closed or TomeBookState.Opening;

    /// <summary>The menu takes no choice or going back (A, B and their keys): sheets turn or the book moves.</summary>
    public bool IsLocked => !Riffle.IsOver || cover.IsPlaying;

    /// <summary>The camera moves (a fly-in, its settling).</summary>
    public bool IsCameraMoving => camera.IsPlaying || camera.IsCrossFading;

    /// <summary>
    /// The book asked to open (a click or A on the shut book): taken once, after its first second; the camera hurries,
    /// six times fast from then on, and the book opens when the fly-in is over.
    /// </summary>
    public bool AskToOpen()
    {
        if (State != TomeBookState.Closed || hurried || time <= Opens)
        {
            return false;
        }
        hurried = true;
        camera.ClipSpeed *= Hurry;
        return true;
    }

    /// <summary>The book open at once, the camera at rest over it (no fly-in, no sound): for a test or a start without one.</summary>
    public void Settle()
    {
        (hurried, State) = (true, TomeBookState.Open);
        Hold(camera, rest);
        Hold(cover, open);
        Hold(left, openLeft);
        Hold(right, openRight);
        Candle.Aim(1, 0.2f, 6);
        Place();
    }

    /// <summary>The pages riffle forward (right to left, to a page put on) or back, as many as a page's count (at most ten sheets).</summary>
    public void Turn(bool forward, int pages)
    {
        Riffle.Start(forward ? flipLeft : flipRight, pages);
        State = Riffle.IsOver ? TomeBookState.Open : forward ? TomeBookState.RiffleForward : TomeBookState.RiffleBackward;
    }

    /// <summary>
    /// A step of the book's time: the fly-in's candle and start, the opening once the hurried fly-in is over, the book's
    /// and stacks' clocks, the riffle (whatever the state, as the original's), the camera (its pose taken before it
    /// moves on, a frame behind, as the original's), the candle.
    /// </summary>
    public TomeBookEvents Update(float dt)
    {
        if (!(dt >= 0) || !float.IsFinite(dt))
        {
            return TomeBookEvents.None;
        }
        var events = TomeBookEvents.None;
        time += dt;
        if (ReferenceEquals(camera.Clip, wakeUp) && !camera.IsCrossFading)
        {
            Candle.Aim(time > Lighting || hurried ? 1 : 0, 0.2f, 6);         // out, its wobble alone flickering, till then
            if (!camera.IsPlaying && !camera.HasFinished)
            {
                camera.Start(wakeUp, false);                                  // the fly-in starts on the first step
            }
            if (hurried && camera.HasFinished && State == TomeBookState.Closed)
            {
                State = TomeBookState.Opening;
                cover.Start(open, false);
                left.Start(openLeft, false);
                right.Start(openRight, false);
                camera.CrossFade(rest, Settling, false);
                events |= TomeBookEvents.Opening;
            }
        }
        cover.Update(dt);
        left.Update(dt);
        right.Update(dt);
        if (State == TomeBookState.Opening && !cover.IsPlaying)
        {
            State = TomeBookState.Open;
        }
        Riffle.Update(dt);
        if (State is TomeBookState.RiffleForward or TomeBookState.RiffleBackward && Riffle.IsOver)
        {
            State = TomeBookState.Open;
        }
        Place();
        camera.Update(dt);
        Candle.Step(dt);
        return events;
    }

    /// <summary>The camera's place in the world this frame: its bone's pose turned half round its own up.</summary>
    public Matrix4x4 CameraWorld { get; private set; }

    /// <summary>The camera's view and lens on a screen of an aspect (width / height).</summary>
    public (Matrix4x4 View, Matrix4x4 Projection, Vector3 Position) Camera(float aspect)
    {
        var shape = float.IsFinite(aspect) && aspect > 0 ? aspect : 1;
        return (view, Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, shape, Near, Far), CameraWorld.Translation);
    }

    /// <summary>A screen point on the right page, in page pixels; none off it, or while the book is shut.</summary>
    public Xna.Vector2? ToPage(Xna.Point mouse, Xna.Point screen)
    {
        if (screen.X <= 0 || screen.Y <= 0 || IsShut)
        {
            return null;
        }
        var (view, projection, position) = Camera(screen.X / (float)screen.Y);
        return BookPicking.ToPage(view * projection, position, new Vector2(mouse.X, mouse.Y), new Vector2(screen.X, screen.Y)) is { } page
            ? new Xna.Vector2(page.X, page.Y)
            : null;
    }

    /// <summary>The frame's book: camera, candle and poses.</summary>
    public void Write(BookPicture picture, float aspect)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var (view, projection, position) = Camera(aspect);
        picture.Shown = true;
        picture.Room = true;
        picture.View = ToXna(view);
        picture.Projection = ToXna(projection);
        picture.Camera = ToXna(position);
        picture.Light = ToXna(light);
        var strength = Candle.Strength;
        picture.Ambient = ToXna(Candle.Ambient * strength);
        picture.Diffuse = ToXna(Candle.Diffuse * strength);
        picture.Specular = ToXna(Candle.Specular * strength);
        Palette(book, cover, picture.Book);
        Palette(stack, left, picture.Left);
        Palette(stack, right, picture.Right);
        picture.SheetCount = Riffle.Count;
        for (var i = 0; i < picture.SheetCount; i++)
        {
            Palette(sheet, Riffle[i], picture.Sheets[i]);
        }
    }

    /// <summary>A clock at its clip's end, finished (a step just past it: one landing on it plays on).</summary>
    private static void Hold(AnimationClock clock, AnimationClip clip)
    {
        clock.Start(clip, false);
        clock.Update((clip.Duration + 1e-3f) / Math.Max(clock.ClipSpeed, 1e-3f));
    }

    /// <summary>The camera's place and view from its clock as it stands.</summary>
    private void Place()
    {
        rig.Skeleton.Sample(camera, poses);
        rig.Skeleton.Place(poses, Matrix4x4.Identity, placed, skinned);
        var world = Matrix4x4.CreateRotationY(MathF.PI) * placed[cameraBone];
        if (Matrix4x4.Invert(world, out var turned))
        {
            (CameraWorld, view) = (world, turned);                            // a bone that does not turn back keeps the last view
        }
    }

    /// <summary>A model's bones as a clock poses them.</summary>
    private void Palette(AnimatedModel model, AnimationClock posing, Xna.Matrix[] palette)
    {
        model.Skeleton.Sample(posing, poses);
        model.Skeleton.Place(poses, Matrix4x4.Identity, placed, skinned);
        for (var i = 0; i < palette.Length; i++)
        {
            palette[i] = i < model.Skeleton.Bones.Count ? ToXna(skinned[i]) : Xna.Matrix.Identity;
        }
    }

    private static Xna.Vector3 ToXna(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Xna.Matrix ToXna(Matrix4x4 m) => new(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);
}
