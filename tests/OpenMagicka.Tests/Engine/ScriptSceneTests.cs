using System.Text;
using OpenMagicka.Content.Levels;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using static OpenMagicka.Tests.Gameplay.AgentTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Engine;

/// <summary>The trigger engine (LEVELS_TRIGGERS.md §5): firing, repeats, conditions, delays, variants, the engine's own actions.</summary>
public class ScriptSceneTests
{
    private const float Frame = 1f / 60;

    private static (ScriptScene Scene, GameWorld World) Scene(string triggers, ScriptRegistry? registry = null, bool looped = false)
    {
        var xml = $"<Scene><Model>model</Model>{triggers}</Scene>";
        var definition = SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "Levels/test/test_s1.xml");
        var world = World();
        return (new ScriptScene(definition, new ScriptLevel(), registry ?? ScriptRegistry.Engine(), world, looped, seed: 1), world);
    }

    private static int Counter(ScriptScene scene, string name) => scene.Level.Counter(ScriptScene.Hash(name));

    private static void Run(ScriptScene scene, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += Frame)
        {
            scene.Update(Frame);
        }
    }

    [Fact]
    public void AnAutorunOnce_FiresOnce_ARepeat_EveryFrame_AndACooldown_OnceInIt()
    {
        var (scene, world) = Scene("""
            <Trigger id="once"><Then><counter name="once" action="inc" value="1"/></Then></Trigger>
            <Trigger id="always" repeat="true"><Then><counter name="always" action="INC" value="1"/></Then></Trigger>
            <Trigger id="every" repeat="0.5"><Then><counter name="every" action="inc" value="1"/></Then></Trigger>
            """);
        using var _ = world;

        Run(scene, 1);

        Assert.Equal(1, Counter(scene, "once"));
        Assert.Equal(60, Counter(scene, "always"));
        Assert.Equal(2, Counter(scene, "every"));
    }

    [Fact]
    public void Conditions_AreAnOrOfAnds_AndInvertTurnsOne()
    {
        var (scene, world) = Scene("""
            <Trigger id="setup"><Then><counter name="a" action="set" value="1"/><counter name="b" action="set" value="2"/></Then></Trigger>
            <Trigger id="and" repeat="true">
              <If><counter name="a" compareMethod="equal" value="1"/><counter name="b" compareMethod="greater" value="5"/></If>
              <Then><counter name="and" action="inc" value="1"/></Then>
            </Trigger>
            <Trigger id="or" repeat="true">
              <If><counter name="a" compareMethod="equal" value="9"/></If>
              <If><counter name="b" compareMethod="less" value="5"/><counter name="a" compareMethod="equal" value="9" invert="true"/></If>
              <Then><counter name="or" action="inc" value="1"/></Then>
            </Trigger>
            """);
        using var _ = world;

        Run(scene, 0.1f);

        Assert.Equal(0, Counter(scene, "and"));
        Assert.True(Counter(scene, "or") > 0);
    }

    [Fact]
    public void ADelay_Waits_AndRunsAreSpacedByIt()
    {
        var (scene, world) = Scene("""
            <Trigger id="later" autorun="false" repeat="true"><Then><counter name="n" action="inc" value="1" delay="1"/></Then></Trigger>
            """);
        using var _ = world;
        scene.ExecuteTrigger(ScriptScene.Hash("later"), null);
        scene.ExecuteTrigger(ScriptScene.Hash("later"), null);

        Run(scene, 0.9f);
        Assert.Equal(0, Counter(scene, "n"));
        Run(scene, 0.2f);
        Assert.Equal(1, Counter(scene, "n"));
        Run(scene, 1);
        Assert.Equal(2, Counter(scene, "n"));
    }

    [Fact]
    public void ExecuteTrigger_KeepsTheTargetsConditionsAndRepeat()
    {
        var (scene, world) = Scene("""
            <Trigger id="caller" autorun="false" repeat="true"><Then><executeTrigger trigger="oneshot"/><executeTrigger trigger="guarded"/></Then></Trigger>
            <Trigger id="oneshot" autorun="false"><Then><counter name="oneshot" action="inc" value="1"/></Then></Trigger>
            <Trigger id="guarded" autorun="false" repeat="true"><If><counter name="open" compareMethod="equal" value="1"/></If>
              <Then><counter name="guarded" action="inc" value="1"/></Then></Trigger>
            """);
        using var _ = world;
        var caller = ScriptScene.Hash("caller");

        scene.ExecuteTrigger(caller, null);
        Run(scene, Frame * 2);
        scene.Level.SetCounter(ScriptScene.Hash("open"), 1);
        scene.ExecuteTrigger(caller, null);
        Run(scene, Frame * 2);                                                             // a target updated earlier this frame runs its part the next

        Assert.Equal(1, Counter(scene, "oneshot"));
        Assert.Equal(1, Counter(scene, "guarded"));
    }

    [Fact]
    public void ARandomGroup_IsChosen_AndThePlainActionsFollowIt()
    {
        var (scene, world) = Scene("""
            <Trigger id="dice" autorun="false" repeat="true"><Then>
              <counter name="plain" action="inc" value="1"/>
              <Random><counter name="heads" action="inc" value="1"/></Random>
              <Random><counter name="tails" action="inc" value="1"/></Random>
            </Then></Trigger>
            """);
        using var _ = world;

        for (var i = 0; i < 40; i++)
        {
            scene.ExecuteTrigger(ScriptScene.Hash("dice"), null);
            Run(scene, Frame);
        }

        Assert.Equal(40, Counter(scene, "plain"));
        Assert.Equal(40, Counter(scene, "heads") + Counter(scene, "tails"));
        Assert.True(Counter(scene, "heads") > 5 && Counter(scene, "tails") > 5);
    }

    [Fact]
    public void APlainDelayedAction_RunsOutAsManyTimesFasterAsThereAreGroups_AsTheOriginal()
    {
        var (scene, world) = Scene("""
            <Trigger id="dice" autorun="false"><Then>
              <counter name="plain" action="inc" value="1" delay="1"/>
              <Random><counter name="x" action="inc" value="1"/></Random>
              <Random><counter name="y" action="inc" value="1"/></Random>
            </Then></Trigger>
            """);
        using var _ = world;
        scene.ExecuteTrigger(ScriptScene.Hash("dice"), null);

        Run(scene, 0.55f);

        Assert.Equal(1, Counter(scene, "plain"));                                         // two variants: its second of delay ran out in half
    }

    [Fact]
    public void ClearDelayedActions_DropsTheWaitingRuns()
    {
        var (scene, world) = Scene("""
            <Trigger id="slow" autorun="false"><Then><counter name="slow" action="inc" value="1" delay="1"/></Then></Trigger>
            <Trigger id="mine" autorun="false"><Then><counter name="mine" action="inc" value="1" delay="1"/><clearDelayedActions trigger="allbutthis" delay="0.5"/></Then></Trigger>
            """);
        using var _ = world;
        scene.ExecuteTrigger(ScriptScene.Hash("slow"), null);
        scene.ExecuteTrigger(ScriptScene.Hash("mine"), null);

        Run(scene, 1.5f);

        Assert.Equal(0, Counter(scene, "slow"));
        Assert.Equal(1, Counter(scene, "mine"));
    }

    [Fact]
    public void APoint_IsUsedWhileEnabled()
    {
        var (scene, world) = Scene("""
            <OnInteract locator="lever" type="use"><counter name="pulls" action="inc" value="1"/><disablePoi id="lever"/></OnInteract>
            <Trigger id="unlock" autorun="false"><Then><enablePoi id="lever"/></Then></Trigger>
            """);
        using var _ = world;
        var lever = ScriptScene.Hash("lever");

        scene.Interact(lever, null);
        Run(scene, Frame);
        scene.Interact(lever, null);
        Run(scene, Frame);
        Assert.Equal(1, Counter(scene, "pulls"));

        scene.ExecuteTrigger(ScriptScene.Hash("unlock"), null);
        Run(scene, Frame);
        scene.Interact(lever, null);
        Run(scene, Frame);
        Assert.Equal(2, Counter(scene, "pulls"));
    }

    [Fact]
    public void Timers_Run_PauseAndAreCompared()
    {
        var (scene, world) = Scene("""
            <Trigger id="start"><Then><addTimer name="t" value="0"/><addTimer name="p" value="0" playedTimer="true"/></Then></Trigger>
            <Trigger id="late"><If><timer name="t" compareMethod="greater" value="1"/></If><Then><counter name="late" action="set" value="1"/><timer name="t" paused="true"/></Then></Trigger>
            """);
        using var _ = world;
        Run(scene, Frame);

        for (var t = 0f; t < 1.5f; t += Frame)
        {
            scene.Level.Update(Frame, inCutscene: t > 0.5f);
            scene.Update(Frame);
        }

        Assert.Equal(1, Counter(scene, "late"));
        Assert.InRange(scene.Level.Timer(ScriptScene.Hash("t")), 1, 1.05f);                      // paused once past 1
        Assert.InRange(scene.Level.Timer(ScriptScene.Hash("p")), 0.45f, 0.55f);                  // played: not in the cutscene
    }

    [Fact]
    public void UnknownTags_AreListed_AndDoNothing()
    {
        var registry = ScriptRegistry.Engine();
        var (scene, world) = Scene("""
            <Trigger id="x" repeat="true"><If><bossDead/></If><Then><counter name="n" action="inc" value="1"/></Then></Trigger>
            <Trigger id="y"><Then><spawnUnicorn type="pink"/></Then></Trigger>
            """, registry);
        using var _ = world;

        Run(scene, 0.1f);

        Assert.Equal(0, Counter(scene, "n"));
        Assert.Equal(["if:bossdead", "then:spawnunicorn"], registry.Unknown);
    }

    [Fact]
    public void ATriggerSettingItselfOffWithNoDelay_RunsOnceAFrame_InsteadOfHanging()
    {
        var (scene, world) = Scene("""
            <Trigger id="loop" autorun="false" repeat="true"><Then><counter name="n" action="inc" value="1"/><executeTrigger trigger="loop"/></Then></Trigger>
            """);
        using var _ = world;
        scene.ExecuteTrigger(ScriptScene.Hash("loop"), null);

        Run(scene, Frame * 10);

        Assert.InRange(Counter(scene, "n"), 9, 11);
    }

    [Fact]
    public void ExecuteTrigger_KeepsASenderForEachWaitingRun_AndClearingLeavesThem_AsTheOriginal()
    {
        var (scene, world) = Scene("""
            <Trigger id="call" autorun="false" repeat="true"><Then><executeTrigger trigger="target" delay="1"/></Then></Trigger>
            <Trigger id="clear" autorun="false" repeat="true"><Then><clearDelayedActions trigger="call"/></Then></Trigger>
            <Trigger id="target" autorun="false" repeat="true"><Then><counter name="hits" action="inc" value="1"/></Then></Trigger>
            """);
        using var _ = world;
        var first = Animated(world, Orc, at: new System.Numerics.Vector3(0, 0.9f, 0));
        var second = Animated(world, Orc, at: new System.Numerics.Vector3(3, 0.9f, 0));
        var senders = new List<OpenMagicka.Gameplay.Characters.Character?>();
        scene.TriggerFired += (trigger, _, by) =>
        {
            if (trigger.Key == ScriptScene.Hash("target"))
            {
                senders.Add(by);
            }
        };

        scene.ExecuteTrigger(ScriptScene.Hash("call"), first);
        scene.ExecuteTrigger(ScriptScene.Hash("clear"), null);
        Run(scene, Frame * 2);
        scene.ExecuteTrigger(ScriptScene.Hash("call"), second);
        Run(scene, 1.1f);

        Assert.Equal([first], senders);                                                          // the cleared run's sender stayed first in line
    }

    [Fact]
    public void IgnoringConditions_FiresAGuardedTrigger_AndARunOnceAgain()
    {
        var (scene, world) = Scene("""
            <Trigger id="guarded" autorun="false"><If><counter name="open" compareMethod="equal" value="1"/></If>
              <Then><counter name="n" action="inc" value="1"/></Then></Trigger>
            """);
        using var _ = world;
        var guarded = ScriptScene.Hash("guarded");

        Assert.False(scene.ExecuteTrigger(guarded, null));
        Assert.True(scene.ExecuteTrigger(guarded, null, ignoreConditions: true));
        Assert.True(scene.ExecuteTrigger(guarded, null, ignoreConditions: true));                 // ignoring them ignores the run-once too
        Run(scene, Frame);

        Assert.Equal(2, Counter(scene, "n"));
    }

    [Fact]
    public void ABadAttribute_OrATwiceUsedId_FailsTheLoading()
    {
        var bad = Assert.Throws<InvalidDataException>(() => Scene("""<Trigger id="x"><Then><counter name="n" action="inc" value="lots"/></Then></Trigger>"""));
        Assert.Contains("lots", bad.Message);
        Assert.Throws<InvalidDataException>(() => Scene("""<Trigger id="x"><Then><counter name="n" action="sideways" value="1"/></Then></Trigger>"""));
        Assert.Throws<InvalidDataException>(() => Scene("""<Trigger id="x"><Then/></Trigger><Trigger id="X"><Then/></Trigger>"""));
    }

    [Fact]
    public void TheTriggersRun_InTheOrderOfTheirKeys()
    {
        var (scene, world) = Scene("""
            <Trigger id="zzz"><Then><counter name="order" action="set" value="1"/></Then></Trigger>
            <Trigger id="aaa"><Then><counter name="order" action="set" value="2"/></Then></Trigger>
            """);
        using var _ = world;

        Run(scene, Frame);

        var last = ScriptScene.Hash("zzz") > ScriptScene.Hash("aaa") ? 1 : 2;
        Assert.Equal(last, Counter(scene, "order"));
        Assert.Equal(scene.Triggers.Select(t => t.Key).Order(), scene.Triggers.Select(t => t.Key));
    }
}
