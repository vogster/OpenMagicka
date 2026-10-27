using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay.Animation;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// The candle that lights the Tome's book (OM-83, docs/engine/ARCHITECTURE.md §20, "The book"), as the original's: its
/// strength eases towards a target at a speed, with a bounded random wobble on it whose size eases too; the light's
/// three colours are its strength (×100: the effect divides by the squared distance) times warm ones.
/// </summary>
public sealed class Candle(Func<float> random)
{
    public static readonly Vector3 Ambient = new(0.3f, 0.2f, 0.2f), Diffuse = new(0.7f, 0.75f, 0.75f), Specular = new(1.3f, 1, 1);

    private readonly Func<float> random = random ?? throw new ArgumentNullException(nameof(random));
    private float speed = 24, amount = 1, intensity, wobble;
    private float targetSpeed = 6, targetAmount = 0.2f, targetIntensity;

    /// <summary>The candle from a seeded random (a uniform −1…1).</summary>
    public Candle(Random random)
        : this(() => (float)(random.NextDouble() * 2 - 1))
    {
    }

    /// <summary>The light's strength now: its intensity with the wobble, ×100.</summary>
    public float Strength => (intensity + wobble * amount) * 100;

    /// <summary>What the light goes towards: its intensity, the wobble's size, the speed it goes at.</summary>
    public void Aim(float intensity, float amount, float speed) => (targetIntensity, targetAmount, targetSpeed) = (intensity, amount, speed);

    public void Step(float dt)
    {
        if (!(dt > 0) || !float.IsFinite(dt))
        {
            return;
        }
        speed += (targetSpeed - speed) * dt * 5;
        amount += (targetAmount - amount) * dt * 5;
        intensity += (targetIntensity - intensity) * dt * speed;
        wobble = Math.Clamp(wobble + random() * dt * speed, -1, 1);
    }
}

/// <summary>The book's states (OM-83): open and still, or riffling forward or back (its closed and opening states come later).</summary>
public enum TomeBookState : byte { Open, RiffleForward, RiffleBackward }

/// <summary>
/// The Tome's book on the logic thread (OM-83, docs/engine/ARCHITECTURE.md §20, "The book"): its camera (a bone of an unseen
/// rig, turned to look along its +Z), the candle at the rig's light bone, and the poses of the book and its two stacks
/// of pages (the stacks are the page model played by the turning sheet's clips), written into each frame's
/// <see cref="BookPicture"/>, with the mouse's way onto the right page from the same camera. It riffles its pages
/// (<see cref="TomeRiffle"/>), the input locked till the sheets land. For now the book lies open with the camera at rest
/// over it (its fly-in and opening come later).
/// </summary>
public sealed class TomeBook
{
    public const string Rig = "UI/ToM/background", Book = "UI/ToM/tome", Stack = "UI/ToM/page", Sheet = "UI/ToM/page_flip";

    /// <summary>The camera's lens: its field of view upright (radians), and its near and far planes.</summary>
    public const float FieldOfView = 0.75f, Near = 2, Far = 200;

    private readonly AnimatedModel rig, book, stack, sheet;
    private readonly AnimationClip open, openLeft, openRight, rest, flipLeft, flipRight;
    private readonly int cameraBone;
    private readonly Vector3 light;
    private readonly BonePose[] poses = new BonePose[BookPicture.PageBones];
    private readonly Matrix4x4[] placed = new Matrix4x4[BookPicture.PageBones], skinned = new Matrix4x4[BookPicture.PageBones];
    private readonly AnimationClock clock = new();

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
        (open, rest) = (Clip(book, "open", Book), Clip(rig, "idle", Rig));
        (openLeft, openRight) = (Clip(sheet, "open_left", Sheet), Clip(sheet, "open_right", Sheet));
        (flipLeft, flipRight) = (Clip(sheet, "flip_left", Sheet), Clip(sheet, "flip_right", Sheet));
        cameraBone = rig.Skeleton.IndexOf("camera");
        var lightBone = rig.Skeleton.IndexOf("Light");
        if (cameraBone < 0 || lightBone < 0)
        {
            throw new InvalidDataException($"{Rig} has no camera or light bone");
        }
        light = rig.Skeleton.Bones[lightBone].BindPose.Translation;           // where it is bound, unturned: it never moves
        Pose(rig, rest, 0);
        CameraWorld = Matrix4x4.CreateRotationY(MathF.PI) * placed[cameraBone];
        if (!Matrix4x4.Invert(CameraWorld, out view))
        {
            throw new InvalidDataException($"{Rig}'s camera bone does not turn back");
        }
        Candle.Aim(1, 0.2f, 6);
    }

    private readonly Matrix4x4 view;

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

    /// <summary>The menu takes no choice or going back (A, B and their keys): a riffle runs.</summary>
    public bool IsLocked => !Riffle.IsOver;

    /// <summary>The pages riffle forward (right to left, to a page put on) or back, as many as a page's count (at most ten sheets).</summary>
    public void Turn(bool forward, int pages)
    {
        Riffle.Start(forward ? flipLeft : flipRight, pages);
        State = Riffle.IsOver ? TomeBookState.Open : forward ? TomeBookState.RiffleForward : TomeBookState.RiffleBackward;
    }

    public void Update(float dt)
    {
        Candle.Step(dt);
        Riffle.Update(dt);                                                    // on, whatever the state, as the original's
        if (State is TomeBookState.RiffleForward or TomeBookState.RiffleBackward && Riffle.IsOver)
        {
            State = TomeBookState.Open;
        }
    }

    /// <summary>The camera's place in the world: its bone's pose at rest, turned half round its own up.</summary>
    public Matrix4x4 CameraWorld { get; }

    /// <summary>The camera's view and lens on a screen of an aspect (width / height).</summary>
    public (Matrix4x4 View, Matrix4x4 Projection, Vector3 Position) Camera(float aspect)
    {
        var shape = float.IsFinite(aspect) && aspect > 0 ? aspect : 1;
        return (view, Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, shape, Near, Far), CameraWorld.Translation);
    }

    /// <summary>A screen point on the right page, in page pixels; none off it.</summary>
    public Xna.Vector2? ToPage(Xna.Point mouse, Xna.Point screen)
    {
        if (screen.X <= 0 || screen.Y <= 0)
        {
            return null;
        }
        var (view, projection, position) = Camera(screen.X / (float)screen.Y);
        return BookPicking.ToPage(view * projection, position, new Vector2(mouse.X, mouse.Y), new Vector2(screen.X, screen.Y)) is { } page
            ? new Xna.Vector2(page.X, page.Y)
            : null;
    }

    /// <summary>The frame's book: camera, candle and poses (open, the stacks laid down).</summary>
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
        Palette(book, open, open.Duration, picture.Book);
        Palette(stack, openLeft, openLeft.Duration, picture.Left);
        Palette(stack, openRight, openRight.Duration, picture.Right);
        picture.SheetCount = Riffle.Count;
        for (var i = 0; i < picture.SheetCount; i++)
        {
            Palette(sheet, Riffle[i], picture.Sheets[i]);
        }
    }

    /// <summary>A model's bones placed for a clip at a time (held at its end), from the world's origin.</summary>
    private void Pose(AnimatedModel model, AnimationClip clip, float time)
    {
        clock.Start(clip, false);
        clock.Update(Math.Clamp(time, 0, clip.Duration));
        model.Skeleton.Sample(clock, poses);
        model.Skeleton.Place(poses, Matrix4x4.Identity, placed, skinned);
    }

    private void Palette(AnimatedModel model, AnimationClip clip, float time, Xna.Matrix[] palette)
    {
        Pose(model, clip, time);
        Copy(model, palette);
    }

    /// <summary>A model's bones as a clock poses them.</summary>
    private void Palette(AnimatedModel model, AnimationClock posing, Xna.Matrix[] palette)
    {
        model.Skeleton.Sample(posing, poses);
        model.Skeleton.Place(poses, Matrix4x4.Identity, placed, skinned);
        Copy(model, palette);
    }

    private void Copy(AnimatedModel model, Xna.Matrix[] palette)
    {
        for (var i = 0; i < palette.Length; i++)
        {
            palette[i] = i < model.Skeleton.Bones.Count ? ToXna(skinned[i]) : Xna.Matrix.Identity;
        }
    }

    private static Xna.Vector3 ToXna(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Xna.Matrix ToXna(Matrix4x4 m) => new(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);
}

/// <summary>
/// The mouse onto the Tome's right page (OM-83, docs/engine/ARCHITECTURE.md §20, "The book"), as the original's: a ray from the
/// camera through the point is met with a fixed quad where the page lies (its outer edge a little higher than the
/// spine), and the hit is turned into page pixels by the original's map (allowing for the sheet curling down at the
/// spine). The left page is met first by nothing a page draws, so a ray only counts on the right.
/// </summary>
public static class BookPicking
{
    /// <summary>The right page's quad: its outer edge at x −5.3 (y 0.5), its spine at x 0 (y 0.4), its top at z 2.75.</summary>
    public static readonly Vector3 OuterTop = new(-5.3f, 0.5f, 2.75f), SpineTop = new(0, 0.4f, 2.75f),
        OuterBottom = new(-5.3f, 0.5f, -2.75f), SpineBottom = new(0, 0.4f, -2.75f);

    /// <summary>A screen point (pixels from the top left) on the right page, in page pixels; none off it.</summary>
    public static Vector2? ToPage(Matrix4x4 viewProjection, Vector3 camera, Vector2 point, Vector2 screen)
    {
        if (!(screen.X > 0) || !(screen.Y > 0) || !Matrix4x4.Invert(viewProjection, out var unproject))
        {
            return null;
        }
        var far = Vector4.Transform(new Vector4(point.X / screen.X * 2 - 1, 1 - point.Y / screen.Y * 2, 1, 1), unproject);
        if (far.W == 0)
        {
            return null;
        }
        var direction = new Vector3(far.X, far.Y, far.Z) / far.W - camera;
        var hit = Meet(camera, direction, OuterTop, SpineTop, OuterBottom) ?? Meet(camera, direction, SpineTop, SpineBottom, OuterBottom);
        return hit is { } at ? FromWorld(at) : null;
    }

    /// <summary>A point on the right page (world x, z) in page pixels, by the original's map.</summary>
    public static Vector2 FromWorld(Vector3 at) => new((0.03f - at.X / 5.3f) * 0.97f * 1024, (2.75f - at.Z) / 5.5f * 1024);

    /// <summary>Where a ray meets a triangle from either side (Möller–Trumbore); none when it misses or lies behind.</summary>
    private static Vector3? Meet(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        var (ab, ac) = (b - a, c - a);
        var p = Vector3.Cross(direction, ac);
        var det = Vector3.Dot(ab, p);
        if (MathF.Abs(det) < 1e-9f)
        {
            return null;
        }
        var t = origin - a;
        var u = Vector3.Dot(t, p) / det;
        var q = Vector3.Cross(t, ab);
        var v = Vector3.Dot(direction, q) / det;
        var along = Vector3.Dot(ac, q) / det;
        return u >= 0 && v >= 0 && u + v <= 1 && along >= 0 ? origin + direction * along : null;
    }
}
