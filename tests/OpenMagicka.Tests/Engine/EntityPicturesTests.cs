using OpenMagicka.Content;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Props;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Tests.Content;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>What of the world the game draws as models (OM-75), and the templates' looks.</summary>
public class EntityPicturesTests
{
    private static readonly ItemTemplate Sword = new("sword", Bounds: new ItemBounds(new(-0.1f, 0, -0.1f), new(0.1f, 1, 0.1f)));

    private static readonly PropTemplate Crate = new("crate", false, false, true, 100, 50, ResistanceTable.Neutral,
        new PropBox(new Numerics.Vector3(-0.5f, 0, -0.5f), Numerics.Vector3.One, Numerics.Quaternion.Identity), [], "", [], []);

    private static ModelLook? Looks(object template) => template switch
    {
        CharacterTemplate { Id: "goblin" } => new ModelLook("models/goblin", new(1, 0.5f, 0.5f)),
        ItemTemplate { Id: "sword" } => new ModelLook("models/sword", Numerics.Vector3.One),
        PropTemplate => new ModelLook("data/physicsentities/crate", Numerics.Vector3.One),
        _ => null,
    };

    [Fact]
    public void TheCharacters_TheirItems_TheProps_LyingItems_AndBooks_AreDrawn()
    {
        using var world = World();
        var goblin = Spawn(world, at: new Numerics.Vector3(1, 0.9f, 0));
        goblin.Equip(0, new Item(Sword), "hand");
        Spawn(world, Goblin with { Id = "ghost" }, at: new Numerics.Vector3(5, 0.9f, 0));                   // no look: left out
        world.Entities.Add(new PhysicsProp(world.Physics, Crate, Numerics.Matrix4x4.CreateTranslation(0, 0, 5)));
        world.Entities.Add(new WorldItem(world.Physics, new Item(Sword), Numerics.Matrix4x4.CreateTranslation(-3, 1, 0)));
        world.Entities.Add(new BookOfMagick(world.Physics, MagickType.Haste, new Numerics.Vector3(3, 0, 3), immovable: true));
        var drawn = new List<DrawnModel> { new("stale", default, null, default) };

        EntityPictures.Collect(world, Looks, drawn);

        Assert.Equal(["models/goblin", "models/sword", "data/physicsentities/crate", "models/sword", ModelLook.Book.Asset], drawn.Select(d => d.Asset));
        Assert.Equal(goblin.RootMatrix(), drawn[0].World);
        Assert.Null(drawn[0].Bones);                                                              // no skeleton: drawn at its root
        Assert.Equal(new Numerics.Vector3(1, 0.5f, 0.5f), drawn[0].Tint);
        Assert.Equal(new Numerics.Vector3(3, 0, 3), drawn[4].World.Translation);
    }

    [Fact]
    public void ABoxPlacedByATransform_IsBoundedWhereItGoes()
    {
        var box = new Xna.BoundingBox(new Xna.Vector3(-1, 0, -1), new Xna.Vector3(1, 2, 1));

        var moved = DeferredRenderer.Transformed(box, Xna.Matrix.CreateRotationY(Xna.MathHelper.PiOver4) * Xna.Matrix.CreateTranslation(10, 0, 0));

        Assert.Equal(10 - MathF.Sqrt(2), moved.Min.X, 3);
        Assert.Equal(2, moved.Max.Y, 3);
        Assert.Equal(box, DeferredRenderer.Transformed(box, Xna.Matrix.Identity));
    }

    [GameContentFact]
    public void TheGamesTemplates_KnowTheirModels()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var library = new TemplateLibrary(loader);
        var assets = new ContentScriptAssets(library);

        var wizard = library.Character("data/characters/wizard");
        var staff = wizard.Equipment!.First(e => e.Slot == 1).Item;
        var barrel = assets.Prop("data/physicsentities/barrel_explosive");

        Assert.StartsWith("models/", library.LookOf(wizard)!.Asset, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("models/", library.LookOf(staff)!.Asset, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("data/physicsentities/barrel_explosive", library.LookOf(barrel!)!.Asset);
        Assert.True(loader.Files.Exists(library.LookOf(wizard)!.Asset + ".xnb"));
        Assert.True(loader.Files.Exists(ModelLook.Book.Asset + ".xnb"));
    }
}
