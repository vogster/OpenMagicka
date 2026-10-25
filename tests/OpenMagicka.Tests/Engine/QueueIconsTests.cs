using OpenMagicka.Engine.Hud;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Tests.Engine;

/// <summary>The element queue under a wizard, animated as the original HUD (OM-54 phase B).</summary>
public class QueueIconsTests
{
    private const float Tick = 1 / 60f;

    private static (QueueIcons Icons, ElementQueue Queue) Conjured(params Elements[] elements)
    {
        var (icons, queue) = (new QueueIcons(), ElementQueue.Empty);
        foreach (var element in elements)
        {
            var changes = new List<QueueChange>();
            queue = queue.Conjure(element, changes: changes).Queue;
            icons.ApplyAll(changes);
        }
        return (icons, queue);
    }

    private static List<QueueIcon> Settled(QueueIcons icons, float seconds = 3)
    {
        for (var t = 0f; t < seconds; t += Tick)
        {
            icons.Update(Tick);
        }
        var drawn = new List<QueueIcon>();
        icons.Write(drawn);
        return drawn;
    }

    [Fact]
    public void ANewElement_PopsInGlowing_ThenSettlesInItsSlot()
    {
        var (icons, _) = Conjured(Elements.Earth, Elements.Arcane);

        var early = Settled(icons, 0.1f);
        Assert.True(early[1].Colour.X > 1.5f);                                                     // glowing
        var drawn = Settled(icons);

        Assert.Equal([Elements.Earth, Elements.Arcane], drawn.Select(i => i.Element));
        Assert.Equal(QueueIcons.SlotOffsets[1].X, drawn[1].Offset.X, 0);
        Assert.Equal(1, drawn[1].Scale.X, 1);
        Assert.Equal(1, drawn[1].Colour.W, 1);
    }

    [Fact]
    public void Merging_TurnsThePartnerIntoTheResult_AndTheFlierGoes()
    {
        var (icons, queue) = Conjured(Elements.Water, Elements.Fire, Elements.Earth);

        var drawn = Settled(icons);

        Assert.Equal([Elements.Steam, Elements.Earth], queue.Entries.Select(e => e.Element));
        Assert.Equal([Elements.Steam, Elements.Earth], drawn.Select(i => i.Element));
        Assert.Equal(QueueIcons.SlotOffsets[1].X, drawn[1].Offset.X, 0);
        Assert.Equal(icons.Elements, queue.Entries.Select(e => e.Element));
    }

    [Fact]
    public void Cancelling_TakesBothAway_AndTheRestSlideLeft()
    {
        var (icons, queue) = Conjured(Elements.Earth, Elements.Water, Elements.Lightning);

        var drawn = Settled(icons);

        Assert.Equal([Elements.Earth], queue.Entries.Select(e => e.Element));
        Assert.Equal([Elements.Earth], drawn.Select(i => i.Element));
        Assert.Equal(0, icons.Staying - 1);
    }

    [Fact]
    public void AFullQueuesRefusedElement_PopsAndGoes_AndAClearShrinksAll()
    {
        var (icons, queue) = Conjured(Elements.Earth, Elements.Earth, Elements.Earth, Elements.Earth, Elements.Earth, Elements.Earth);

        Assert.Equal(5, queue.Count);
        Assert.Equal(5, Settled(icons).Count);
        Assert.Equal(5, icons.Elements.Count);

        icons.ClearAll();
        Assert.Empty(Settled(icons));
        Assert.True(icons.IsEmpty);
    }

    [Fact]
    public void TheMagickHint_FadesOutTheOld_ThenInTheNew()
    {
        var hint = new MagickHint();
        hint.Update(0.1f, MagickType.Haste);
        Assert.Equal(MagickType.Haste, hint.Shown);
        hint.Update(0.3f, MagickType.Haste);
        Assert.Equal(1, hint.Alpha);
        Assert.Equal([Elements.Lightning, Elements.Arcane, Elements.Fire], hint.Recipe);

        hint.Update(0.1f, MagickType.Rain);
        Assert.Equal((MagickType.Haste, 0.6f), (hint.Shown, hint.Alpha), new AlphaComparer());
        hint.Update(0.2f, MagickType.Rain);
        Assert.Equal(MagickType.Rain, hint.Shown);
        Assert.Equal("#magick_performance", MagickHint.NameKey(MagickType.PerformanceEnchantment));
        Assert.Equal("#magick_thunderb", MagickHint.NameKey(MagickType.ThunderB));
    }

    private sealed class AlphaComparer : IEqualityComparer<(MagickType, float)>
    {
        public bool Equals((MagickType, float) a, (MagickType, float) b) => a.Item1 == b.Item1 && MathF.Abs(a.Item2 - b.Item2) < 1e-3f;

        public int GetHashCode((MagickType, float) obj) => 0;
    }

    [Fact]
    public void APlayersConjure_ShowsUnderItsWizard_InTheFrame()
    {
        using var play = EffectDirectorTests.Play();
        play.Start();
        play.Tick(Tick);
        play.Player.SelectedMagick = MagickType.Haste;

        Assert.True(play.Player.Avatar!.Conjure(Elements.Lightning));
        for (var i = 0; i < 60; i++)
        {
            play.Tick(Tick);
        }
        var gui = new OpenMagicka.Engine.Rendering.GuiPictures();
        play.WriteHud(gui);

        var queue = Assert.Single(gui.Queues);
        Assert.Equal(Elements.Lightning, Assert.Single(queue.Icons).Element);
        Assert.Equal((1, 3), (queue.RecipeFrom, queue.Recipe.Count));                               // Haste's rest shows from the second slot
        Assert.True(queue.MagickAlpha > 0.9f);
        Assert.Single(gui.Bars);
    }

    [Fact]
    public void TheQueue_SaysItsChanges_StepByStep()
    {
        var changes = new List<QueueChange>();
        var queue = ElementQueue.Empty.Conjure(Elements.Earth).Queue.Conjure(Elements.Water).Queue;

        queue.Conjure(Elements.Fire, changes: changes);
        Assert.Equal([new QueueAdded(Elements.Fire), new QueueMerged(Elements.Steam, 2, 1)], changes);

        changes.Clear();
        queue.Conjure(Elements.Lightning, changes: changes);
        Assert.Equal([new QueueAdded(Elements.Lightning), new QueueCancelled(2, 1)], changes);

        var full = Enumerable.Repeat(Elements.Earth, 5).Aggregate(ElementQueue.Empty, (q, e) => q.Conjure(e).Queue);
        changes.Clear();
        full.Conjure(Elements.Lightning, changes: changes);
        Assert.Equal([new QueueAdded(Elements.Lightning), new QueueCancelled(5, 4)], changes);       // Earth and Lightning cancel
    }

    [Fact]
    public void AMagickEntry_KeepsThePositionsInStep()
    {
        var (icons, queue) = (new QueueIcons(), ElementQueue.Empty.WithMagick(MagickType.Haste));
        icons.Apply(new QueueAdded(queue.Entries[0].Element));                                       // as the HUD takes a queue it did not see made
        var changes = new List<QueueChange>();
        queue = queue.Conjure(Elements.Fire, changes: changes).Queue;

        icons.ApplyAll(changes);
        Settled(icons);

        Assert.Equal(queue.Entries.Select(e => e.Element), icons.Elements);
        Assert.Equal(queue.Count, icons.Staying);
    }

    [Fact]
    public void ARefusedElement_HoldsNoPlace_ForTheNextChange()
    {
        var (icons, queue) = Conjured(Elements.Earth, Elements.Earth, Elements.Earth, Elements.Earth, Elements.Earth, Elements.Earth);
        var changes = new List<QueueChange>();
        queue = queue.Conjure(Elements.Lightning, changes: changes).Queue;

        icons.ApplyAll(changes);
        var drawn = Settled(icons);

        Assert.Equal(4, queue.Count);
        Assert.Equal(4, icons.Staying);
        Assert.All(drawn, i => Assert.Equal(Elements.Earth, i.Element));
    }
}
