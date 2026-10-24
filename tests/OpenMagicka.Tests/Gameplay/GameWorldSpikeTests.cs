using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Characters;
using OpenMagicka.Engine.Physics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using GameElements = OpenMagicka.Gameplay.Spells.Elements;
using AttackProperties = OpenMagicka.Gameplay.Combat.AttackProperties;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Characters in a full game world on the game's levels (skipped without MAGICKA_DIR).</summary>
public class GameWorldSpikeTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void EveryCharacterTemplate_MakesACharacter()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        using var world = CharacterTests.World();
        var templates = loader.Files.ListFiles("Data/Characters", recursive: true)
            .Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
            .Select(p => loader.LoadUncached(p[..^4]).Root)
            .OfType<CharacterTemplateContent>()
            .Select(CharacterTemplates.From)
            .ToList();
        var failures = new List<string>();
        foreach (var template in templates)
        {
            try
            {
                world.Physics.Remove(new Character(template, world.Physics, new System.Numerics.Vector3(0, 10, 0)).Body.Body);
            }
            catch (ArgumentException e)
            {
                failures.Add($"{template.Id}: {e.Message}");
            }
        }
        output.WriteLine($"{templates.Count} templates: {templates.Count(t => t.Undying)} undying ({templates.Count(t => t.Undying && t.UndieHitPoints <= 0)} with no revival HP), {templates.Count(t => t.Flies)} flying, {templates.Count(t => !t.CanBeKnockedDown)} that never fall");
        Assert.Equal(338, templates.Count);
        Assert.Empty(failures);
    }

    /// <summary>Actions of the kinds that work now, in the animations whose clips load.</summary>
    private const int MadeActions = 10521;

    [GameContentFact]
    public void EveryItem_MakesATemplate_AndEveryCharacterIsEquipped()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var items = loader.Files.ListFiles("Data/Items", recursive: true)
            .Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
            .Select(p => ItemTemplates.From((ItemContent)loader.LoadUncached(p[..^4]).Root!))
            .ToList();
        var (equipped, slots, staffs, tipped) = (0, 0, 0, 0);
        var far = new List<string>();
        foreach (var path in loader.Files.ListFiles("Data/Characters", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            if (loader.LoadUncached(path[..^4]).Root is not CharacterTemplateContent content)
            {
                continue;
            }
            using var world = CharacterTests.World();
            var character = new Character(CharacterTemplates.From(loader, path[..^4], content), world.Physics,
                new Vector3(20, 0.5f + content.Stats.Radius + content.Stats.Length / 2, -30), animations: CharacterAnimationSets.From(loader, path[..^4], content));
            world.Entities.Add(character);                                             // off the origin, where a lost item would be
            CharacterTests.Run(world, 0.5f);
            var held = character.Equipment.Count(s => s.Item is not null);
            (equipped, slots, staffs) = (equipped + (held > 0 ? 1 : 0), slots + held, staffs + (character.SpellSource >= 0 ? 1 : 0));
            var reach = content.Stats.Radius + content.Stats.Length + 6;               // a horseman's lance reaches 5 m
            far.AddRange(character.Equipment.Where(s => s.Item is { } item && (!(Vector3.Distance(item.Tip, character.Position) <= reach)
                    || !(Vector3.Distance(item.Transform.Translation, character.BoneWorld(s.Bone).Translation) < 1e-3f)))   // NaN counts as far; the grip is on its bone
                .Select(s => $"{content.Id}: {s.Item!.Template.Id} on {s.BoneName} at {s.Item.Tip} (body {character.Position})"));
            tipped += character.Equipment.Count(s => s.Item?.Template.Attach0 is not null);
        }
        var classes = items.GroupBy(i => i.WeaponClass).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
        output.WriteLine($"{items.Count} items ({string.Join(", ", classes)}); {items.Count(i => i.BlockValue > 0)} block, {items.Count(i => i.PassiveAbility != 0)} with a passive");
        output.WriteLine($"{equipped} templates hold {slots} items; {staffs} hold a staff; {tipped} held items have a tip; {far.Count} far from their holder");
        far.Take(20).ToList().ForEach(output.WriteLine);
        Assert.Empty(far);                                                         // the whole pose chain puts every held item at hand
        Assert.Equal(454, items.Count);
        Assert.Equal((200, 92, 48, 292, 11), (items.Count(i => i.WeaponClass == WeaponClass.Default), items.Count(i => i.WeaponClass == WeaponClass.Staff),
            items.Count(i => i.WeaponClass == WeaponClass.SlashMedium), items.Count(i => i.BlockValue > 0), items.Count(i => i.PassiveAbility != 0)));
        Assert.Equal((286, 591, 86, 291), (equipped, slots, staffs, tipped));      // as ENTITIES_AI §8.1.1 records
    }

    [GameContentFact]
    public void EveryCharacterTemplate_HasItsAnimations()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var failures = new List<string>();
        int templates = 0, animations = 0, actions = 0, sets = 0, made = 0;
        foreach (var path in loader.Files.ListFiles("Data/Characters", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            if (loader.LoadUncached(path[..^4]).Root is not CharacterTemplateContent template)
            {
                continue;
            }
            templates++;
            AnimationAction? Counted(AnimationActionContent content)
            {
                var action = CharacterAnimationActions.Create(content);
                made += action is null ? 0 : 1;
                if (content is JumpAction { MinRange: null } or JumpAction { MaxRange: null })
                {
                    failures.Add($"{template.Id}: a jump without its range");             // §4.2 counts on every vanilla jump having both ends
                }
                return action;
            }
            try
            {
                var missing = new List<string>();
                var built = CharacterAnimationSets.From(loader, path[..^4], template, Counted, missing);
                failures.AddRange(missing.Select(m => $"{template.Id}: missing {m}"));
                sets += built.Sets.Count(s => s is not null);
                animations += template.AnimationSets.Sum(s => s.Animations.Count);
                actions += template.AnimationSets.Sum(s => s.Animations.Sum(a => a.Clip.Actions.Count));
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException)
            {
                failures.Add($"{template.Id}: {e.Message}");
            }
        }
        output.WriteLine($"{templates} templates, {sets} animation sets, {animations} animations, {actions} actions ({made} made); {failures.Count} failed");
        failures.Take(20).ToList().ForEach(output.WriteLine);
        Assert.Equal(338, templates);
        Assert.Equal((1090, 16475, 16264), (sets, animations, actions));        // as ENTITIES_AI §4.1 records
        Assert.Equal(MadeActions, made);                                          // the kinds of §4.2 that work now
        Assert.Equal(8, failures.Count);
        Assert.All(failures, f => Assert.StartsWith("a_basic_character: missing", f));   // a base template: its clips are not in its skeleton
    }

    [GameContentFact]
    public void EveryTemplate_StandsFallsAndDies_ThroughItsOwnClips()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var stuck = new List<string>();
        var (count, felledByTheFall, lyingForever) = (0, 0, 0);
        foreach (var path in loader.Files.ListFiles("Data/Characters", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            if (loader.LoadUncached(path[..^4]).Root is not CharacterTemplateContent content)
            {
                continue;
            }
            count++;
            using var world = CharacterTests.World();
            var character = new Character(CharacterTemplates.From(loader, path[..^4], content), world.Physics, new Vector3(0, 0.5f + content.Stats.Radius + content.Stats.Length / 2, 0),
                random: new Random(count), animations: CharacterAnimationSets.From(loader, path[..^4], content));
            world.Entities.Add(character);
            CharacterTests.Run(world, 1);
            character.Damage(new DamageEntry(AttackProperties.Knockback, GameElements.None, 500, 2), null, character.Position - Vector3.UnitX);
            CharacterTests.Run(world, 14);                                            // flight, then down and up: two soldiers use a 5.8 s clip for each
            var standing = character.State == CharacterStates.Idle;
            if (!standing && character.State == CharacterStates.Dead)
            {
                felledByTheFall++;                                                   // the landing killed it, or its knockdown clip's suicide
                standing = true;
            }
            else if (!standing && character.State == CharacterStates.KnockDown && character.Animator!.Clock.IsLooping)
            {
                lyingForever++;                                                      // its knockdown clip loops: it stays down, as in the original
                standing = true;
            }
            var afterFall = $"{character.State}/{character.CurrentAnimation}/{character.Animator!.Clock.Clip!.Name} t{character.Animator.Clock.Time:F2}/{character.Animator.Clock.Clip.Duration:F2} speed {character.AnimationSpeed:F2}x{character.Animator.Clock.ClipSpeed:F2} ground {character.Body.IsTouchingGround} pushed {character.Body.IsPushed}";
            character.Kill();
            CharacterTests.Run(world, 10);                                            // death clips last up to 7 s
            if (!standing || !character.IsDead)
            {
                var clock = character.Animator.Clock;
                stuck.Add($"{content.Id}: {(standing ? "" : $"not back up ({afterFall}); ")}{(character.IsDead ? "" : $"not dead ({character.State}, {clock.Clip!.Name} t{clock.Time:F2}/{clock.Clip.Duration:F2} x{clock.ClipSpeed:F2})")}");
            }
        }
        output.WriteLine($"{count} templates; {felledByTheFall} killed by the fall, {lyingForever} lying for good (a looping knockdown clip); {stuck.Count} stuck");
        stuck.Take(30).ToList().ForEach(output.WriteLine);
        Assert.Equal(338, count);
        Assert.Empty(stuck);
        Assert.Equal((9, 0), (felledByTheFall, lyingForever));                   // 3 by the landing, 6 warriors by their knockdown clip
    }

    [GameContentFact]
    public void CharactersSetOnEveryLevel_StandUnhurt()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        int levels = 0, characters = 0, hurt = 0, standing = 0;
        foreach (var path in loader.Files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            if (loader.LoadUncached(path[..^4]).Root is not LevelModelContent level)
            {
                continue;
            }
            using var world = new GameWorld();
            if (LevelCollision.AddTo(world.Physics, level) == 0)
            {
                continue;
            }
            levels++;
            var nav = level.NavMesh;
            var stride = Math.Max(1, nav.Triangles.Count / 12);
            for (var i = 0; i < nav.Triangles.Count; i += stride)
            {
                var t = nav.Triangles[i];
                var a = nav.Vertices[t.A] + nav.Vertices[t.B] + nav.Vertices[t.C];
                var centre = new Vector3(a.X, a.Y, a.Z) / 3;
                if (world.Physics.CastSegment(centre + new Vector3(0, 2, 0), centre - new Vector3(0, 10, 0)) is { Material: not null } ground)
                {
                    world.Entities.Add(new Character(CharacterTests.Goblin, world.Physics, ground.Position + new Vector3(0, 1.4f, 0), random: new Random(i)));
                }
            }
            CharacterTests.Run(world, 2);
            foreach (var character in world.Characters)
            {
                characters++;
                hurt += character.HitPoints < character.MaxHitPoints ? 1 : 0;
                standing += character.Body.IsTouchingGround && !character.IsDead ? 1 : 0;
            }
        }
        output.WriteLine($"{levels} levels, {characters} characters set 0.5 m above the ground: {standing} standing after 2 s, {hurt} hurt");
        Assert.True(levels > 100);
        Assert.Equal(0, hurt);
        Assert.True(standing > characters * 95 / 100, $"only {standing} of {characters} standing");
    }
}
