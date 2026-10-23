using Microsoft.Xna.Framework;
using OpenMagicka.Content.Particles;
using OpenMagicka.Engine.Particles;

namespace OpenMagicka.Tests.Particles;

public class ParticleRandomTests
{
    [Fact]
    public void TheTable_StartsWithTheSeededGeneratorsKnownValues()
    {
        // System.Random(0)'s first value, the same on .NET Framework and .NET.
        Assert.Equal(0.72624326f, ParticleRandom.Peek(0), 6);
    }

    [Fact]
    public void TheTable_IsTheOriginalsSequence_FloatsThenPercentBytes()
    {
        var random = new Random(0);
        var floats = Enumerable.Range(0, 65536).Select(_ => (float)random.NextDouble()).ToArray();
        var bytes = Enumerable.Range(0, 65536).Select(_ => (byte)random.Next(100)).ToArray();
        ushort at = 0;

        Assert.Equal(floats[0], ParticleRandom.Next(ref at));
        Assert.Equal(floats[1], ParticleRandom.Next(ref at));
        at = 65535;
        Assert.Equal(floats[65535], ParticleRandom.Next(ref at));
        Assert.Equal(0, at);   // the offset wraps
        at = 7;
        Assert.Equal(bytes[7] >= 30 ? 1 : -1, ParticleRandom.Sign(ref at, 30));
    }

    [Fact]
    public void ARangedValue_IsSpreadBetweenItsBounds_ByThePowerOfTheDistribution()
    {
        ushort at = 3;
        var r = ParticleRandom.Peek(3);

        Assert.Equal(MathF.Pow(r, 2) * (5 - 1) + 1, ParticleRandom.Range(ref at, 1, 5, 2), 5);
        Assert.Equal(4, at);
    }

    [Fact]
    public void AConeDirection_IsAUnitVector_TurnedByTheEmitter()
    {
        ushort at = 100;
        var direction = ParticleRandom.Cone(ref at, Matrix.Identity, MathHelper.PiOver4, 1);

        Assert.Equal(1, direction.Length(), 4);
        Assert.Equal(102, at);   // two values used
    }

    [Fact]
    public void AnArcDirection_LiesWithinItsVerticalRange()
    {
        for (ushort start = 0; start < 2000; start += 2)
        {
            var at = start;
            var direction = ParticleRandom.Arc(ref at, Matrix.Identity, MathHelper.PiOver2, 1, 0.1f, 0.5f, 1);

            Assert.Equal(1, direction.Length(), 4);
            Assert.InRange(MathF.Asin(direction.Y), 0.1f - 1e-4f, 0.5f + 1e-4f);
        }
    }
}

public class ParticlePoolTests
{
    [Fact]
    public void AnInstance_IsLaidOutAsTheShaderReadsIt()
    {
        Assert.Equal(ParticleInstance.SizeInBytes, System.Runtime.CompilerServices.Unsafe.SizeOf<ParticleInstance>());
        Assert.Equal((24, 40, 44), ((int)System.Runtime.InteropServices.Marshal.OffsetOf<ParticleInstance>(nameof(ParticleInstance.Color)),
            (int)System.Runtime.InteropServices.Marshal.OffsetOf<ParticleInstance>(nameof(ParticleInstance.Sprite)),
            (int)System.Runtime.InteropServices.Marshal.OffsetOf<ParticleInstance>(nameof(ParticleInstance.Velocity))));
    }

    [Fact]
    public void ThePool_IsWhollyReusable_AfterManyLives()
    {
        var pool = new ParticlePool(capacity: 8);
        for (var round = 0; round < 50; round++)
        {
            for (var i = 0; i < 8; i++)
            {
                pool.Spawn(new ParticleSpawn { TTL = 0.01f, StartSize = 1, EndSize = 1 });
            }
            pool.Update(0.1f);
            pool.Update(0.1f);
        }

        Assert.Equal(-1, pool.LastActive);
        for (var i = 0; i < 8; i++)
        {
            pool.Spawn(new ParticleSpawn { TTL = 1, StartSize = 1, EndSize = 1 });
        }
        Assert.Equal(7, pool.LastActive);
    }

    private static ParticleSpawn Spark(float ttl = 2) => new()
    {
        Position = new Vector3(1, 2, 3), Velocity = new Vector3(4, 0, 0), Gravity = -10, Drag = 0.5f, TTL = ttl,
        StartSize = 1, EndSize = 3, Rotation = 0.5f, RotationVelocity = 2, Color = new Vector4(1, 0.5f, 0.25f, 1),
        Sprite = 7, AlphaBlended = true, Hsv = true, Colorize = true, RotationAligned = true,
    };

    [Fact]
    public void ASpawnedParticle_TakesTheLowestFreeSlot_WithItsLookAtBirth()
    {
        var pool = new ParticlePool();

        pool.Spawn(Spark());
        pool.Spawn(Spark());

        Assert.Equal(1, pool.LastActive);
        var p = pool.Instances[0];
        Assert.Equal((new Vector3(1, 2, 3), 0.5f, 1f, 0f), (p.Position, p.Rotation, p.Radius, p.NormalizedLifetime));
        Assert.Equal((7, 1, 55, 1), ((int)p.Sprite, (int)p.AlphaBlended, (int)p.HsvColorize, (int)p.RotationAligned));
    }

    [Fact]
    public void AnUpdate_MovesTheParticle_AsTheOriginalDoes()
    {
        var pool = new ParticlePool();
        pool.Spawn(Spark());

        pool.Update(0.5f);

        var p = pool.Instances[0];
        var keep = MathF.Pow(0.5f, 0.5f);   // drag: the share of velocity kept per second
        var velocity = new Vector3(4 * keep, -10 * 0.5f * keep, 0);
        Assert.Equal(velocity.X, p.Velocity.X, 4);
        Assert.Equal(velocity.Y, p.Velocity.Y, 4);
        Assert.Equal(1 + velocity.X * 0.5f, p.Position.X, 4);
        Assert.Equal(2 + velocity.Y * 0.5f, p.Position.Y, 4);
        Assert.Equal((1.5f, 0.25f, 1.5f), (p.Rotation, p.NormalizedLifetime, p.Radius));   // size: start 1 -> end 3, a quarter through
    }

    [Fact]
    public void ADeadParticle_FreesItsSlot_AndIsHiddenOnTheNextUpdate()
    {
        var pool = new ParticlePool();
        pool.Spawn(Spark(ttl: 0.1f));
        pool.Spawn(Spark());

        pool.Update(0.2f);
        Assert.Equal(5, pool.Instances[0].Radius, 4);   // still drawn this step, its size extrapolated past the end (1 -> 3, at twice its life)
        pool.Update(0.1f);
        Assert.Equal(0, pool.Instances[0].Radius);
        pool.Spawn(Spark());

        Assert.Equal(new Vector3(1, 2, 3), pool.Instances[0].Position);   // the freed slot is taken again
        Assert.Equal(1, pool.LastActive);
    }

    [Fact]
    public void WhenTheLastParticlesDie_TheActiveRangeShrinks()
    {
        var pool = new ParticlePool();
        pool.Spawn(Spark());
        pool.Spawn(Spark(ttl: 0.1f));

        pool.Update(0.2f);

        Assert.Equal(0, pool.LastActive);
    }

    [Fact]
    public void AFullPool_IgnoresMoreSpawns()
    {
        var pool = new ParticlePool(capacity: 2);

        pool.Spawn(Spark());
        pool.Spawn(Spark());
        pool.Spawn(Spark());

        Assert.Equal(1, pool.LastActive);
    }
}

public class EmitterTests
{
    private static EmitterContent Continuous(float perSecond, EmitterFlags flags = EmitterFlags.None) => new(
        EmitterKind.Continuous, flags, 3, 0, new Dictionary<EmitterParameter, ParameterCurve>
        {
            [EmitterParameter.ParticlesPerSecond] = ParameterCurve.Constant(perSecond),
            [EmitterParameter.PositionX] = ParameterCurve.Constant(0),
            [EmitterParameter.PositionY] = ParameterCurve.Constant(0),
            [EmitterParameter.PositionZ] = ParameterCurve.Constant(0),
            [EmitterParameter.PositionXOffset] = ParameterCurve.Constant(0),
            [EmitterParameter.PositionYOffset] = ParameterCurve.Constant(0),
            [EmitterParameter.PositionZOffset] = ParameterCurve.Constant(0),
            [EmitterParameter.RotationY] = ParameterCurve.Constant(0),
            [EmitterParameter.LifeTimeMin] = ParameterCurve.Constant(2),
            [EmitterParameter.LifeTimeMax] = ParameterCurve.Constant(4),
            [EmitterParameter.Gravity] = ParameterCurve.Constant(0),
        });

    private static EmitterContent Pulse(int count, float trigger) => new(
        EmitterKind.Pulse, EmitterFlags.None, 0, count, new Dictionary<EmitterParameter, ParameterCurve>
        {
            [EmitterParameter.TriggerTime] = ParameterCurve.Constant(trigger),
            [EmitterParameter.LifeTimeMin] = ParameterCurve.Constant(1),
            [EmitterParameter.LifeTimeMax] = ParameterCurve.Constant(1),
            [EmitterParameter.AlphaMin] = ParameterCurve.Constant(0.5f),
            [EmitterParameter.AlphaMax] = ParameterCurve.Constant(0.5f),
        });

    private static int Alive(ParticlePool pool) => pool.LastActive + 1;

    [Fact]
    public void AContinuousEmitter_DrawsItsRandomValuesInTheOriginalsOrder()
    {
        // Seed 0: the first emitter's cursor starts at 0. Arc direction (2), position (3), then start size.
        var emitter = Continuous(1) with
        {
            Parameters = new Dictionary<EmitterParameter, ParameterCurve>(Continuous(1).Parameters)
            {
                [EmitterParameter.SizeStartMin] = ParameterCurve.Constant(0),
                [EmitterParameter.SizeStartMax] = ParameterCurve.Constant(10),
                [EmitterParameter.SizeStartDistribution] = ParameterCurve.Constant(1),
                [EmitterParameter.LifeTimeDistribution] = ParameterCurve.Constant(1),
            },
        };
        var pool = new ParticlePool();
        var effect = new VisualEffectInstance(new VisualEffectContent(VisualEffectType.Infinite, 2, [emitter], null), seed: 0);
        effect.Start(Matrix.Identity);

        effect.Update(1, pool);

        Assert.Equal(ParticleRandom.Peek(5) * 10, pool.Instances[0].Radius, 4);
    }

    [Fact]
    public void APulseEmitter_DrawsItsRandomValuesInTheOriginalsOrder()
    {
        // Position (3), then start size.
        var emitter = Pulse(1, 0) with
        {
            Parameters = new Dictionary<EmitterParameter, ParameterCurve>(Pulse(1, 0).Parameters)
            {
                [EmitterParameter.SizeStartMax] = ParameterCurve.Constant(10),
                [EmitterParameter.SizeStartDistribution] = ParameterCurve.Constant(1),
            },
        };
        var pool = new ParticlePool();
        var effect = new VisualEffectInstance(new VisualEffectContent(VisualEffectType.Infinite, 2, [emitter], null), seed: 0);
        effect.Start(Matrix.Identity);

        effect.Update(0.1f, pool);

        Assert.Equal(ParticleRandom.Peek(3) * 10, pool.Instances[0].Radius, 4);
    }

    [Fact]
    public void AnEffectWithoutEmitters_StillRunsItsTimeline()
    {
        var effect = new VisualEffectInstance(new VisualEffectContent(VisualEffectType.Single, 0.1f, [], null), seed: 0);
        effect.Start(Matrix.Identity);

        effect.Update(0.2f, new ParticlePool());

        Assert.False(effect.IsActive);
    }

    [Fact]
    public void AContinuousEmitter_SpawnsItsRate_OverTotalTime()
    {
        var pool = new ParticlePool();
        var effect = new VisualEffectInstance(new VisualEffectContent(VisualEffectType.Infinite, 1, [Continuous(10)], null), seed: 0);
        effect.Start(Matrix.Identity);

        for (var step = 0; step < 10; step++)
        {
            effect.Update(0.1f, pool);
        }

        Assert.InRange(Alive(pool), 9, 10);   // whole particles counted from the running total: 10 by t = 1 (floating point may leave one for the next step)
        Assert.All(Enumerable.Range(0, Alive(pool)), i => Assert.Equal(3, pool.Instances[i].Sprite));
    }

    [Fact]
    public void AContinuousEmitter_PlacesItsParticles_BetweenTheEffectsLastAndCurrentPlace()
    {
        var pool = new ParticlePool();
        var effect = new VisualEffectInstance(new VisualEffectContent(VisualEffectType.Infinite, 1, [Continuous(40)], null), seed: 0);
        effect.Start(Matrix.Identity);
        effect.Update(0.05f, pool);   // 2 particles at the start place
        var before = Alive(pool);

        effect.Move(Matrix.CreateTranslation(10, 0, 0));
        effect.Update(0.1f, pool);   // 4 more, spread from x = 0 to x = 10

        Assert.Equal((2, 6), (before, Alive(pool)));
        Assert.Equal([2.5f, 5f, 7.5f, 10f], Enumerable.Range(2, 4).Select(i => MathF.Round(pool.Instances[i].Position.X, 3)));
    }

    [Fact]
    public void APulseEmitter_FiresOnce_WhenTheTimelinePassesItsTrigger()
    {
        var pool = new ParticlePool();
        var effect = new VisualEffectInstance(new VisualEffectContent(VisualEffectType.Infinite, 2, [Pulse(5, 0.3f)], null), seed: 0);
        effect.Start(Matrix.Identity);

        effect.Update(0.2f, pool);
        Assert.Equal(0, Alive(pool));
        effect.Update(0.2f, pool);
        Assert.Equal(5, Alive(pool));
        Assert.All(Enumerable.Range(0, 5), i => Assert.Equal(0.5f, pool.Instances[i].Color.W));   // a pulse takes alpha from AlphaMin/Max
        effect.Update(0.2f, pool);
        Assert.Equal(5, Alive(pool));
    }

    [Fact]
    public void ALoopingEffect_FiresItsPulseOnEveryLoop()
    {
        var pool = new ParticlePool();
        var effect = new VisualEffectInstance(new VisualEffectContent(VisualEffectType.Looping, 0.45f, [Pulse(1, 0.02f)], null), seed: 0);
        effect.Start(Matrix.Identity);

        for (var step = 0; step < 10; step++)
        {
            effect.Update(0.1f, pool);   // 1 s: the timeline wraps to 0.05 and 0.1, past the trigger each time
        }

        Assert.Equal(3, Alive(pool));
    }

    [Fact]
    public void ASingleEffect_StopsAfterItsDuration_AndAnInfiniteOneHoldsItsEnd()
    {
        var single = new VisualEffectInstance(new VisualEffectContent(VisualEffectType.Single, 0.3f, [Continuous(1)], null), seed: 0);
        var infinite = new VisualEffectInstance(new VisualEffectContent(VisualEffectType.Infinite, 0.3f, [Continuous(1)], null), seed: 0);
        var pool = new ParticlePool();
        single.Start(Matrix.Identity);
        infinite.Start(Matrix.Identity);

        for (var step = 0; step < 5; step++)
        {
            single.Update(0.1f, pool);
            infinite.Update(0.1f, pool);
        }

        Assert.False(single.IsActive);
        Assert.True(infinite.IsActive);
        Assert.Equal(0.3f, infinite.TimelinePosition);
    }
}

public class ParticleGameTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [OpenMagicka.Tests.Content.GameContentFact]
    public void EveryShippedEffect_RunsFiveSeconds()
    {
        var files = new OpenMagicka.Content.ContentFileSystem(OpenMagicka.Tests.Content.GameContent.ContentDirectory!);
        var library = EffectLibrary.Load(files);
        var pool = new ParticlePool();
        int effects = 0, spawned = 0, broken = 0;
        foreach (var effect in library.Effects)
        {
            // A particle born with no lifetime (a LifeTimeMax that is or animates to 0, as in
            // casing_gyrojet) gets an infinite inverse lifetime and a NaN radius for its one step,
            // as in the original.
            var instant = effect.Emitters.Any(e => e.Parameters.TryGetValue(EmitterParameter.LifeTimeMax, out var life) && life.Keys.Any(k => k.Value <= 0));
            pool.Clear();
            var instance = new VisualEffectInstance(effect, seed: 0);
            instance.Start(Matrix.Identity);
            for (var step = 0; step < 300; step++)
            {
                instance.Update(1 / 60f, pool);
                pool.Update(1 / 60f);
            }
            effects++;
            spawned += pool.LastActive + 1;
            for (var i = 0; i <= pool.LastActive; i++)
            {
                var p = pool.Instances[i];
                broken += instant || float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z + p.Radius) ? 0 : 1;
            }
        }
        output.WriteLine($"{effects} effects, {spawned} particle slots in use after 5 s, {broken} not finite");
        Assert.Equal(904, effects);
        Assert.True(spawned > 0);
        Assert.Equal(0, broken);
    }
}
