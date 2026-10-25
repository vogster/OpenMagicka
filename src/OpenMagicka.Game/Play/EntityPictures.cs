using System.Numerics;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Props;

namespace OpenMagicka.Game.Play;

/// <summary>A model to draw this tick: its asset, its place, a skinned one's bones (in the world already) and its tint.</summary>
public readonly record struct DrawnModel(string Asset, Matrix4x4 World, Matrix4x4[]? Bones, Vector3 Tint);

/// <summary>
/// What of the world is drawn as models (logic thread): the characters posed by their bones (none yet:
/// at their root) with the items in their hands, the physics props, the items lying about, the books of
/// magick and the debris. What has no model (or none the looks know) is left out.
/// </summary>
public static class EntityPictures
{
    /// <summary>A frozen prop's pieces are drawn icy.</summary>
    public static readonly Vector3 FrozenTint = new(0.6f, 0.8f, 1);

    public static void Collect(GameWorld world, Func<object, ModelLook?> looks, List<DrawnModel> into)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(looks);
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        foreach (var entity in world.Entities.Entities)
        {
            switch (entity)
            {
                case Character character:
                    Add(character, looks, into);
                    break;
                case PhysicsProp prop when looks(prop.Template) is { } look:
                    into.Add(new DrawnModel(look.Asset, prop.Transform, null, look.Tint));
                    break;
                case WorldItem lying when looks(lying.Item.Template) is { } look:
                    into.Add(new DrawnModel(look.Asset, lying.Transform, null, look.Tint));
                    break;
                case Gib gib:
                    into.Add(new DrawnModel(gib.Model, gib.Transform, null, gib.IsFrozen ? FrozenTint : Vector3.One));
                    break;
                case BookOfMagick book:
                    into.Add(new DrawnModel(ModelLook.Book.Asset, Matrix4x4.CreateTranslation(book.Position), null, ModelLook.Book.Tint));
                    break;
            }
        }
    }

    private static void Add(Character character, Func<object, ModelLook?> looks, List<DrawnModel> into)
    {
        if (looks(character.Template) is { } look)
        {
            var bones = character.BoneTransforms;
            into.Add(new DrawnModel(look.Asset, character.RootMatrix(), bones.Length > 0 ? bones.ToArray() : null, look.Tint));
        }
        foreach (var slot in character.Equipment)
        {
            if (slot.Item is { IsVisible: true } item && looks(item.Template) is { } held)
            {
                into.Add(new DrawnModel(held.Asset, item.Transform, null, held.Tint));
            }
        }
    }
}
