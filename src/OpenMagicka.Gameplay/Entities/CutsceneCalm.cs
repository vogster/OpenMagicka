using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells.Effects;
using OpenMagicka.Gameplay.Spells.Magicks;
using Barrier = OpenMagicka.Gameplay.Spells.Effects.Barrier;

namespace OpenMagicka.Gameplay.Entities;

/// <summary>
/// What a cutscene's start clears (LEVELS_TRIGGERS.md §9.3), as the original: every running magick
/// ends (but a revival on its way), and within 40 of the camera's ground point the whirlwinds, barriers, mines, shields, grease and
/// the Reaper go; the characters there stop attacking, thaw, are let go of vines and fear, and an NPC's
/// charm ends; a summoned NPC dies.
/// </summary>
public static class CutsceneCalm
{
    public const float Radius = 40;

    public static void Calm(EntityManager manager, Vector3 around)
    {
        ArgumentNullException.ThrowIfNull(manager);
        foreach (var effect in manager.Entities.OfType<IMagickEffect>().Where(e => e is not ReviveEffect).ToList())
        {
            effect.End();                                                     // ours: a revival on its way (a checkpoint's) still rises
        }
        foreach (var entity in manager.Query(around, Radius))
        {
            switch (entity)
            {
                case Whirlwind whirlwind:
                    whirlwind.Kill();
                    break;
                case Barrier barrier:
                    barrier.Kill();
                    break;
                case SpellMine mine:
                    mine.Kill();
                    break;
                case SpellShield shield:
                    shield.Kill();
                    break;
                case GreaseField grease:
                    grease.Kill();
                    break;
                case Reaper reaper:
                    reaper.Kill();
                    break;
                case Character character:
                    Calm(character);
                    break;
            }
        }
    }

    private static void Calm(Character character)
    {
        character.StopAttacking();
        character.StopStatuses(StatusEffects.Frozen);
        character.ReleaseEntanglement();
        character.RemoveFear();
        if (!character.IsPlayer)
        {
            character.EndCharm();
            if (character.IsSummoned)
            {
                character.Kill();
            }
        }
    }
}
