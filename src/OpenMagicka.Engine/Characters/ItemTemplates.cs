using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay.Combat;
using Gameplay = OpenMagicka.Gameplay;

namespace OpenMagicka.Engine.Characters;

/// <summary>The content reader's item as the rules need it (docs/engine/ENTITIES_AI.md §8.1.1).</summary>
public static class ItemTemplates
{
    /// <param name="model">The item's model, whose bones give its points (§4.3) and whose vertices its bounds (§8.1.5); null puts the points at its origin.</param>
    /// <param name="projectile">The model of its missile, whose first mesh gives the missile's size (§8.1.4).</param>
    public static Gameplay.Items.ItemTemplate From(ItemContent content, Content.Xna.ModelContent? model = null, Content.Xna.ModelContent? projectile = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var template = new Gameplay.Items.ItemTemplate(
            content.Name,
            (Gameplay.Items.WeaponClass)content.WeaponClass,
            content.BlockValue,
            Resistances(content.Resistances),
            (Gameplay.Items.PassiveAbility)content.PassiveAbility,
            content.PassiveAbilityValue,
            content.CooldownTime,
            content.Melee.Range,
            content.Melee.MultiHit,
            content.Pickable,
            content.Bound,
            Point(model, "attach0"),
            Point(model, "attach1"),
            Events(content.Melee.Conditions),
            [.. content.Auras.Select(BuffTemplates.Aura)],
            Gameplay.Items.Guns.IsGun((Gameplay.Items.WeaponClass)content.WeaponClass) ? Gun(content.Ranged) : null,
            content.Ranged.RangedConditions.Count > 0 ? Ranged(content, projectile) : null,
            Bounds(model));
        template.Validate();
        return template;
    }

    /// <summary>The item's model, from the item's own asset.</summary>
    internal static Content.Xna.ModelContent? Model(ContentLoader loader, string itemAsset, ItemContent item) =>
        item.Model is { } model ? loader.LoadReference(itemAsset, model).Root as Content.Xna.ModelContent : null;

    /// <summary>The model of the item's missile, from the item's own asset.</summary>
    internal static Content.Xna.ModelContent? Projectile(ContentLoader loader, string itemAsset, ItemContent item) =>
        item.Ranged.ProjectileModel is { } model && item.Ranged.RangedConditions.Count > 0 && (Gameplay.Items.WeaponClass)item.WeaponClass != Gameplay.Items.WeaponClass.Heavy
            ? loader.LoadReference(itemAsset, model).Root as Content.Xna.ModelContent
            : null;

    /// <summary>
    /// The bounds of the first part of the first mesh: as the original, its vertex count read from the
    /// start of the buffer at its declaration's stride. Null without a model or a mesh to read.
    /// </summary>
    internal static Gameplay.Items.ItemBounds? Bounds(Content.Xna.ModelContent? model)
    {
        if (model is not { Meshes: [{ VertexBuffer: { } buffer, Parts: [var part, ..] }, ..] } || part.NumVertices <= 0)
        {
            return null;
        }
        if (part.VertexDeclarationIndex < 0 || part.VertexDeclarationIndex >= model.VertexDeclarations.Count)
        {
            throw new InvalidDataException($"an item's mesh part uses declaration {part.VertexDeclarationIndex} of {model.VertexDeclarations.Count}");
        }
        var declaration = model.VertexDeclarations[part.VertexDeclarationIndex];
        var position = declaration.Elements.FirstOrDefault(e => e.Usage == Content.Xna.VertexElementUsageXna31.Position && e.Format == Content.Xna.VertexElementFormatXna31.Vector3)
            ?? throw new InvalidDataException("an item's model has no positions");
        if ((long)(part.NumVertices - 1) * declaration.Stride + position.Offset + 12 > buffer.Data.Length)
        {
            throw new InvalidDataException($"an item's model has {part.NumVertices} vertices of {declaration.Stride} bytes in {buffer.Data.Length} bytes");
        }
        var (min, max) = (new System.Numerics.Vector3(float.MaxValue), new System.Numerics.Vector3(float.MinValue));
        for (var v = 0; v < part.NumVertices; v++)
        {
            var at = v * declaration.Stride + position.Offset;
            var point = new System.Numerics.Vector3(BitConverter.ToSingle(buffer.Data, at), BitConverter.ToSingle(buffer.Data, at + 4), BitConverter.ToSingle(buffer.Data, at + 8));
            (min, max) = (System.Numerics.Vector3.Min(min, point), System.Numerics.Vector3.Max(max, point));
        }
        return new Gameplay.Items.ItemBounds(min, max);
    }

    /// <summary>
    /// A named bone's transform with each parent's multiplied in before it, as the original (the
    /// last bone of the name wins); null without one.
    /// </summary>
    private static System.Numerics.Matrix4x4? Point(Content.Xna.ModelContent? model, string name)
    {
        if (model is null)
        {
            return null;
        }
        System.Numerics.Matrix4x4? point = null;
        for (var i = 0; i < model.Bones.Count; i++)
        {
            if (!string.Equals(model.Bones[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var matrix = XnaConversions.Matrix(model.Bones[i].Transform);
            var steps = 0;
            for (var parent = model.Bones[i].Parent; parent is { } p; parent = model.Bones[p].Parent)
            {
                if (++steps > model.Bones.Count)
                {
                    throw new InvalidDataException($"the bones above {name} go round in a circle");
                }
                matrix = XnaConversions.Matrix(model.Bones[p].Transform) * matrix;
            }
            point = matrix;
        }
        return point;
    }

    /// <summary>What the item throws (§8.1.4): the projectile model's first mesh sizes it, except for Heavy guns.</summary>
    private static Gameplay.Items.RangedTemplate Ranged(ItemContent content, Content.Xna.ModelContent? projectile)
    {
        var ranged = content.Ranged;
        var radius = projectile is { Meshes.Count: > 0 } model && (Gameplay.Items.WeaponClass)content.WeaponClass != Gameplay.Items.WeaponClass.Heavy
            ? model.Meshes[0].Bounds.Radius
            : Gameplay.Items.Ranged.DefaultRadius;
        return new Gameplay.Items.RangedTemplate(ranged.Range, Events(ranged.RangedConditions), ranged.Elevation * MathF.PI / 180, ranged.Homing, ranged.Danger,
            ranged.Facing, radius > 0 ? radius : Gameplay.Items.Ranged.DefaultRadius);
    }

    /// <summary>A gun's firing, from the item's ranged part (§8.1.3).</summary>
    private static Gameplay.Items.GunTemplate Gun(ItemRanged ranged) => new(
        ranged.GunRange, ranged.GunClip, ranged.GunRate, ranged.GunAccuracy, ranged.TracerVelocity, Events(ranged.GunConditions),
        ranged.GunSound, ranged.GunMuzzleEffect, ranged.GunShellsEffect, Sprite(ranged.TracerSprite), Sprite(ranged.NonTracerSprite));

    /// <summary>
    /// A tracer sprite: "" is none (−1), "N" is N, and "X/N" adds 64, 128, 192 or 256 for B, C, D or E
    /// (any other sheet adds nothing), as the original; it draws the sprite as a byte.
    /// </summary>
    internal static int Sprite(string text)
    {
        if (text.Length == 0)
        {
            return -1;
        }
        var parts = text.Split('/');
        var sheet = parts.Length > 1 ? parts[0].ToUpperInvariant() switch
        {
            "B" => 64,
            "C" => 128,
            "D" => 192,
            "E" => 256,
            _ => 0,
        } : 0;
        return float.TryParse(parts[parts.Length > 1 ? 1 : 0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var index)
            ? sheet + (int)index
            : throw new InvalidDataException($"bad tracer sprite '{text}'");
    }

    /// <summary>A source's event groups; the events that wait for later systems are left out (§8.2).</summary>
    internal static IReadOnlyList<Gameplay.Items.EventGroup> Events(IReadOnlyList<EventCollectionContent> groups) =>
        [.. groups.Select(g => new Gameplay.Items.EventGroup(
            new Gameplay.Items.EventCondition((Gameplay.Items.EventTrigger)g.Condition, g.Hitpoints, (Gameplay.Spells.Elements)(int)g.Elements, g.Threshold, g.Time, g.Repeat),
            [.. g.Events.Select(Event).OfType<Gameplay.Items.ItemEvent>()]))];

    private static Gameplay.Items.ItemEvent? Event(EventContent e) => e switch
    {
        DamageEvent d => new Gameplay.Items.DamageEvent(Damage(d.Damage), d.VelocityBased),
        DamageOwnerEvent d => new Gameplay.Items.DamageOwnerEvent(Damage(d.Damage), d.VelocityBased),
        SoundEvent s => new Gameplay.Items.SoundEvent(s.Cue, (int)s.Banks, s.Magnitude),
        EffectEvent f => new Gameplay.Items.EffectEvent(f.Effect, f.Follow, f.WorldAlign),
        RemoveEvent r => new Gameplay.Items.RemoveEvent(r.Bounce),
        SplashEvent s => new Gameplay.Items.SplashEvent(Damage(s.Damage), s.Radius),
        _ => null,
    };

    internal static Gameplay.Combat.DamageEntry Damage(DamageContent d) =>
        new((Gameplay.Combat.AttackProperties)(int)d.Attack, (Gameplay.Spells.Elements)(int)d.Elements, d.Amount, d.Magnitude);

    /// <summary>Overrides on top of the neutral table, as a template's.</summary>
    internal static ResistanceTable Resistances(IReadOnlyList<ResistanceContent> overrides) =>
        overrides.Aggregate(ResistanceTable.Neutral, (table, r) =>
            table.With((Gameplay.Spells.Elements)(int)r.Elements, r.Multiplier, r.Modifier, r.StatusResistance));
}
