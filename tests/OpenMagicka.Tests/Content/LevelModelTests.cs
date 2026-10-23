using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xnb;
using static OpenMagicka.Tests.Content.XnbTestWriter;

namespace OpenMagicka.Tests.Content;

public class LevelModelTests
{
    private const string LevelModelReader = "Magicka.ContentReaders.LevelModelReader, Magicka, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
    private const string PolygonHead = ", PolygonHead, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
    private const string Vector3List = XnaContent + "ListReader`1[[Microsoft.Xna.Framework.Vector3" + Xna + "]]" + Xna;

    private static XnbContent Load(XnbTestWriter w) => XnbContent.Load(w.Build(), XnbReaderRegistry.Magicka);

    private static readonly float[] Identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    private static XnbTestWriter Str(XnbTestWriter w, string s)
    {
        w.Writer.Write(s);
        return w;
    }

    private static XnbTestWriter Int(XnbTestWriter w, params int[] values)
    {
        foreach (var v in values)
        {
            w.Writer.Write(v);
        }
        return w;
    }

    private static void Triangles(XnbTestWriter w, int vertices, params int[] indices)
    {
        w.TypeId(Vector3List).UInts((uint)vertices);
        for (var i = 0; i < vertices; i++)
        {
            w.Floats(i, 0, 0);
        }
        Int(w, indices.Length / 3);
        Int(w, indices);
    }

    private static void NavMesh(XnbTestWriter w, ushort vertexIndex = 2, ushort neighbour = 0xFFFF)
    {
        w.Writer.Write((ushort)3);
        w.Floats(0, 0, 0, 1, 0, 0, 0, 0, 1);
        w.Writer.Write((ushort)1);
        foreach (var v in new ushort[] { 0, 1, vertexIndex, neighbour, 0xFFFF, 0xFFFF })
        {
            w.Writer.Write(v);
        }
        w.Floats(1, 1.4f, 1).Bytes(1);       // costs, Water movement
    }

    private static void Lava(XnbTestWriter w)
    {
        w.TypeId("PolygonHead.Pipeline.LavaEffectReader" + PolygonHead);
        w.Floats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Str(w, ""); Str(w, ""); Str(w, "");
        w.Floats(0, 0, 0, 0, 0, 0, 0);
        Str(w, ""); Str(w, "");
    }

    private static void Liquid(XnbTestWriter w, Action<XnbTestWriter> effect)
    {
        effect(w);
        w.Null().Null().Null();              // VB, IB, declaration
        Int(w, 16, 3, 1).Bytes(1, 1, 0);     // stride, vertices, primitives; collision, freezable, auto-freeze
    }

    private static void Part(XnbTestWriter w, string name, int children, Action<XnbTestWriter>? body = null)
    {
        Str(w, name).Bytes(1).Null();        // affects shields, no model
        if (body is null)
        {
            Int(w, 0, 0, 0).Floats(0);       // mesh settings, liquids, locators; duration
            Int(w, 0, 0, 0).Bytes(0, 0);     // keyframes, effects, lights; no collision, no navmesh
        }
        else
        {
            body(w);
        }
        Int(w, children);
    }

    private static XnbTestWriter Level(Action<XnbTestWriter>? parts = null, Action<XnbTestWriter>? lights = null,
        Action<XnbTestWriter>? navMesh = null, Action<XnbTestWriter>? liquid = null, Action<XnbTestWriter>? slot0 = null,
        Action<XnbTestWriter>? forceField = null, Action<XnbTestWriter>? camera = null)
    {
        var w = new XnbTestWriter().TypeId(LevelModelReader);
        w.Null();                            // no static geometry
        if (parts is null) { Int(w, 0); } else { parts(w); }
        if (lights is null) { Int(w, 0); } else { lights(w); }
        Int(w, 1);                           // one particle effect
        Str(w, "torch_fire").Floats(1, 2, 3, 0, 0, 1, 5);
        Str(w, "Fire/torch");
        Int(w, 1).Floats(Identity);          // one physics entity
        Str(w, "barrel");
        if (liquid is null) { Int(w, 0); } else { Int(w, 1); liquid(w); }
        if (forceField is null) { Int(w, 0); } else { Int(w, 1); forceField(w); }
        if (slot0 is null) { w.Bytes(0); } else { w.Bytes(1); slot0(w); }
        for (var slot = 1; slot < 10; slot++)
        {
            w.Bytes(0);
        }
        if (camera is null) { w.Bytes(0); } else { w.Bytes(1); camera(w); }
        Int(w, 1);
        Str(w, "start_area").Floats(-1, 0, -1, 2, 2, 2, 0, 0, 0, 1);
        Int(w, 1);
        Str(w, "start0").Floats(Identity).Floats(1.5f);
        if (navMesh is null) { NavMesh(w); } else { navMesh(w); }
        return w;
    }

    [Fact]
    public void Level_ReadsEverySection()
    {
        var w = Level(
            parts: p =>
            {
                Int(p, 1);
                Part(p, "gate", children: 1, body: b =>
                {
                    Int(b, 1); Str(b, "gate_mesh").Bytes(1, 0);
                    Int(b, 0);                                   // liquids
                    Int(b, 1); Str(b, "gate_pivot").Floats(Identity).Floats(0.5f);
                    b.Floats(2.5f);                              // duration
                    Int(b, 1).Floats(0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1);
                    Int(b, 0);                                   // effects
                    Int(b, 1); Str(b, "gate_light").Floats(Identity);
                    b.Bytes(1).Bytes(5);                         // collision, Stone
                    Triangles(b, 3, 0, 1, 2);
                    b.Bytes(1); NavMesh(b);
                });
                Part(p, "gate_bar", children: 0);
            },
            lights: l =>
            {
                Int(l, 1);
                Str(l, "gate_light").Floats(0, 5, 0, 0, -1, 0);
                Int(l, 2, 3);                                    // spot, candle
                l.Floats(10).Bytes(1).Floats(0.5f, 0.8f);
                l.Floats(1, 0.9f, 0.7f, 0.1f, 0.1f, 0.1f, 0.3f, 2, 0.1f);
                Int(l, 512).Bytes(1);
            },
            liquid: q => Liquid(q, Lava),
            slot0: s => Triangles(s, 3, 0, 2, 1));

        var level = Assert.IsType<LevelModelContent>(Load(w).Root);

        Assert.Null(level.Geometry);
        var gate = Assert.Single(level.AnimatedParts);
        Assert.Equal(("gate", true, 2.5f), (gate.Name, gate.AffectShields, gate.AnimationDuration));
        Assert.Equal(new MeshSetting("gate_mesh", true, false), Assert.Single(gate.MeshSettings));
        Assert.Equal(0.5f, Assert.Single(gate.Locators).Radius);
        Assert.Single(gate.Animation);
        Assert.Equal("gate_light", Assert.Single(gate.Lights).LightName);
        Assert.Equal(CollisionMaterial.Stone, gate.Collision!.Material);
        Assert.Equal(new CollisionTriangle(0, 1, 2), Assert.Single(gate.Collision.Mesh.Triangles));
        Assert.NotNull(gate.NavMesh);
        Assert.Equal("gate_bar", Assert.Single(gate.Children).Name);

        var light = Assert.Single(level.Lights);
        Assert.Equal((LevelLightType.Spot, LightVariationType.Candle), (light.Type, light.Variation));
        Assert.Equal((10f, true, 512, true), (light.Range, light.UseAttenuation, light.ShadowMapSize, light.CastShadows));

        Assert.Equal(new VisualEffectPlacement("torch_fire", new Vector3(1, 2, 3), Vector3.UnitZ, 5, "Fire/torch"), Assert.Single(level.Effects));
        Assert.Equal("barrel", Assert.Single(level.PhysicsEntities).Template);
        var liquid = Assert.Single(level.Liquids);
        Assert.IsType<LavaEffectContent>(liquid.Effect);
        Assert.Equal((16, 3, 1, true, true, false), (liquid.VertexStride, liquid.NumVertices, liquid.PrimitiveCount, liquid.HasCollision, liquid.Freezable, liquid.AutoFreeze));
        Assert.Empty(level.ForceFields);

        Assert.Equal(10, level.CollisionMeshes.Count);
        Assert.Equal(new CollisionTriangle(0, 2, 1), Assert.Single(level.CollisionMeshes[(int)CollisionMaterial.Generic]!.Triangles));
        Assert.Null(level.CollisionMeshes[1]);
        Assert.Null(level.CameraMesh);

        var trigger = Assert.Single(level.TriggerAreas);
        Assert.Equal(("start_area", new Vector3(-1, 0, -1), new Vector3(2, 2, 2)), (trigger.Name, trigger.Corner, trigger.SideLengths));
        Assert.Equal(new Vector3(0, 1, 0), trigger.Center);
        Assert.Equal(("start0", 1.5f), (Assert.Single(level.Locators).Name, level.Locators[0].Radius));

        Assert.Equal(3, level.NavMesh.Vertices.Count);
        var triangle = Assert.Single(level.NavMesh.Triangles);
        Assert.Equal((0, 1, 2), (triangle.A, triangle.B, triangle.C));
        Assert.Null(triangle.NeighbourA);
        Assert.Equal(1.4f, triangle.CostBC);
        Assert.Equal(MovementProperties.Water, triangle.Movement);
    }

    [Fact]
    public void Light_UnknownType_IsInvalid()
    {
        var w = Level(lights: l =>
        {
            Int(l, 1);
            Str(l, "custom").Floats(0, 0, 0, 0, 0, 0);
            Int(l, 10, 0).Floats(0).Bytes(0).Floats(0, 0).Floats(0, 0, 0, 0, 0, 0, 0, 0, 0);
            Int(l, 0).Bytes(0);
        });

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void Liquid_WithANonLiquidEffect_IsInvalid()
    {
        var w = Level(liquid: q => Liquid(q, x => x.TypeId(XnaContent + "EffectReader" + Xna).UInts(1).Bytes(0)));

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void NavTriangle_VertexOutOfRange_IsInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Load(Level(navMesh: n => NavMesh(n, vertexIndex: 3))));
    }

    [Fact]
    public void CollisionTriangle_IndexOutOfRange_IsInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Load(Level(slot0: s => Triangles(s, 3, 0, 1, 3))));
    }

    private static Action<XnbTestWriter> Chain(int depth) => p =>
    {
        Int(p, 1);
        for (var i = 0; i < depth; i++)
        {
            Str(p, "p").Bytes(0).Null();
            Int(p, 0, 0, 0).Floats(0);
            Int(p, 0, 0, 0).Bytes(0, 0);
            Int(p, i == depth - 1 ? 0 : 1);
        }
    };

    [Fact]
    public void AnimatedParts_AtTheDepthLimit_Load()
    {
        var level = Assert.IsType<LevelModelContent>(Load(Level(parts: Chain(LevelModelReaderLimits.MaxPartDepth))).Root);

        var part = Assert.Single(level.AnimatedParts);
        for (var i = 1; i < LevelModelReaderLimits.MaxPartDepth; i++)
        {
            part = Assert.Single(part.Children);
        }
        Assert.Empty(part.Children);
    }

    [Fact]
    public void AnimatedParts_OneLevelTooDeep_AreInvalid()
    {
        var error = Assert.Throws<InvalidDataException>(() => Load(Level(parts: Chain(LevelModelReaderLimits.MaxPartDepth + 1))));
        Assert.Contains("nested deeper", error.Message);
    }

    [Fact]
    public void ForceField_CameraMeshAndWater_Read()
    {
        var w = Level(
            forceField: f =>
            {
                f.Floats(0, 0.5f, 1, 2, 3, 4, 5, 6, 7).Bytes(1);
                Str(f, "ripple");
                f.Floats(10).Null().Null().Null();
                Int(f, 12, 4, 2);
            },
            camera: c => Triangles(c, 3, 2, 1, 0),
            liquid: q => Liquid(q, x =>
            {
                x.TypeId("PolygonHead.Pipeline.RenderDeferredLiquidEffectReader" + PolygonHead);
                Str(x, "").Floats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                Str(x, ""); Str(x, "");
                x.Floats(0, 0, 0, 0, 0, 0, 0);
                Str(x, ""); Str(x, "");
            }));

        var level = Assert.IsType<LevelModelContent>(Load(w).Root);

        var field = Assert.Single(level.ForceFields);
        Assert.Equal((2f, 3f, 10f, 4), (field.Width, field.AlphaPower, field.Ttl, field.NumVertices));
        Assert.Equal(new OpenMagicka.Content.Xna.ExternalReference("ripple"), field.DisplacementMap);
        Assert.Equal(new CollisionTriangle(2, 1, 0), Assert.Single(level.CameraMesh!.Triangles));
        Assert.IsType<RenderDeferredLiquidEffectContent>(Assert.Single(level.Liquids).Effect);
    }

    [Fact]
    public void PartCollision_UnknownMaterial_IsInvalid()
    {
        var w = Level(parts: p =>
        {
            Int(p, 1);
            Part(p, "x", children: 0, body: b =>
            {
                Int(b, 0, 0, 0).Floats(0);
                Int(b, 0, 0, 0).Bytes(1).Bytes(10);
                Triangles(b, 3, 0, 1, 2);
                b.Bytes(0);
            });
        });

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void Liquid_NegativeStride_IsInvalid()
    {
        var w = Level(liquid: q =>
        {
            Lava(q);
            q.Null().Null().Null();
            Int(q, -16, 3, 1).Bytes(0, 0, 0);
        });

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void NavMesh_CountsBeyondTheData_AreInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Load(Level(navMesh: n => n.Writer.Write((ushort)60000))));
    }

    [Fact]
    public void NavTriangle_NeighbourOutOfRange_IsInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Load(Level(navMesh: n => NavMesh(n, neighbour: 1))));
    }

    [Fact]
    public void ListCountBeyondTheData_IsInvalid()
    {
        var w = new XnbTestWriter().TypeId(LevelModelReader).Null();
        Int(w, 50_000_000);

        Assert.Throws<InvalidDataException>(() => Load(w));
    }

    [Fact]
    public void CollisionSlot_WithoutVertexList_IsInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Load(Level(slot0: s => { s.Null(); Int(s, 0); })));
    }
}
