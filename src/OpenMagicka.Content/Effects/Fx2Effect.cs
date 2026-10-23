using System.Buffers.Binary;

namespace OpenMagicka.Content.Effects;

public readonly record struct Fx2State(int Operation, int At, uint Value);

public sealed record Fx2Pass(int Technique, int Pass, int CountAt, IReadOnlyList<Fx2State> States);

public readonly record struct Fx2SamplerState(int At, int Operation, int ValueAt, uint Value);

/// <summary>D3DXPARAMETER_CLASS.</summary>
public enum Fx2ParameterClass
{
    Scalar,
    Vector,
    MatrixRows,
    MatrixColumns,
    Object,
    Struct,
}

/// <summary>D3DXPARAMETER_TYPE (the values an effect uses).</summary>
public enum Fx2ParameterType
{
    Void,
    Bool,
    Int,
    Float,
    String,
    Texture,
    Texture1D,
    Texture2D,
    Texture3D,
    TextureCube,
    Sampler,
    Sampler1D,
    Sampler2D,
    Sampler3D,
    SamplerCube,
    PixelShader,
    VertexShader,
}

/// <summary>
/// A top-level effect parameter. Flag 1 (D3DX_PARAMETER_SHARED) marks the parameters the
/// original shared between effects through an EffectPool.
/// </summary>
public sealed record Fx2Parameter(string Name, string Semantic, uint Flags, Fx2ParameterClass Class, Fx2ParameterType Type)
{
    public const uint SharedFlag = 1;

    public bool IsShared => (Flags & SharedFlag) != 0;
}

/// <summary>A shader or other object bound to a pass state (Technique &gt;= 0) or to a parameter (Technique = -1).</summary>
public sealed record Fx2Resource(int At, int Technique, int Pass, int Element, int State, int Usage, byte[] Code);

/// <summary>
/// Structure of a DX9 fx_2_0 effect binary, as documented by Wine's d3dx9 parser: u32 tag
/// 0xFEFF0901, u32 offset, a data blob, then the structure part at 8 + offset (parameters,
/// techniques with passes and their state entries, then string and resource tables). All
/// positions are offsets into the blob (the file without its 8-byte header). Read-only.
/// </summary>
public sealed class Fx2Effect
{
    public const uint Tag = 0xFEFF0901;
    public const int HeaderSize = 8;

    private static readonly IReadOnlySet<uint> SamplerTypes = new HashSet<uint> { 10, 11, 12, 13, 14 };

    private Fx2Effect(IReadOnlyList<Fx2Parameter> parameters, IReadOnlyList<Fx2Pass> passes, IReadOnlyList<Fx2SamplerState> samplerStates,
        IReadOnlyList<Fx2Resource> resources)
    {
        Parameters = parameters;
        Passes = passes;
        SamplerStates = samplerStates;
        Resources = resources;
    }

    public IReadOnlyList<Fx2Parameter> Parameters { get; }

    public IReadOnlyList<Fx2Pass> Passes { get; }

    public IReadOnlyList<Fx2SamplerState> SamplerStates { get; }

    public IReadOnlyList<Fx2Resource> Resources { get; }

    public static Fx2Effect Parse(ReadOnlySpan<byte> effect)
    {
        if (effect.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(effect) != Tag)
        {
            throw new InvalidDataException("not an fx_2_0 effect");
        }
        var blob = effect[HeaderSize..];
        var reader = new BlobReader(blob, (int)BinaryPrimitives.ReadUInt32LittleEndian(effect[4..]));

        var parameters = reader.Count(16);
        var techniques = reader.Count(12);
        reader.U32();
        reader.U32();
        var samplerStates = new List<Fx2SamplerState>();
        var parameterList = new List<Fx2Parameter>();
        for (var i = 0; i < parameters; i++)
        {
            var typedef = (int)reader.U32();
            var value = (int)reader.U32();
            var flags = reader.U32();
            reader.Skip(8 * reader.Count(8));
            parameterList.Add(new Fx2Parameter(reader.Name((int)reader.At(typedef + 8)), reader.Name((int)reader.At(typedef + 12)), flags,
                (Fx2ParameterClass)reader.At(typedef + 4), (Fx2ParameterType)reader.At(typedef)));
            if (SamplerTypes.Contains(reader.At(typedef)))
            {
                var count = (int)reader.At(value);
                for (var k = 0; k < count; k++)
                {
                    var at = value + 4 + 16 * k;
                    var valueAt = (int)reader.At(at + 12);
                    samplerStates.Add(new Fx2SamplerState(at, (int)reader.At(at), valueAt, reader.At(valueAt)));
                }
            }
        }

        var passes = new List<Fx2Pass>();
        for (var t = 0; t < techniques; t++)
        {
            reader.U32();
            var annotations = reader.Count(8);
            var passCount = reader.Count(12);
            reader.Skip(8 * annotations);
            for (var p = 0; p < passCount; p++)
            {
                reader.U32();
                var passAnnotations = reader.Count(8);
                var countAt = reader.Position;
                var stateCount = reader.Count(16);
                reader.Skip(8 * passAnnotations);
                var states = new Fx2State[stateCount];
                for (var s = 0; s < stateCount; s++)
                {
                    var at = reader.Position;
                    var operation = (int)reader.U32();
                    reader.U32();
                    reader.U32();
                    var valueAt = (int)reader.U32();
                    states[s] = new Fx2State(operation, at, reader.At(valueAt));
                }
                passes.Add(new Fx2Pass(t, p, countAt, states));
            }
        }

        var strings = reader.Count(8);
        var resourceCount = reader.Count(24);
        for (var i = 0; i < strings; i++)
        {
            reader.U32();
            reader.Skip(Align(reader.Count(1)));
        }
        var resources = new List<Fx2Resource>();
        for (var i = 0; i < resourceCount; i++)
        {
            var at = reader.Position;
            var technique = unchecked((int)reader.U32());
            var pass = (int)reader.U32();
            var element = unchecked((int)reader.U32());
            var state = unchecked((int)reader.U32());
            var usage = (int)reader.U32();
            var size = reader.Count(1);
            var code = reader.Bytes(size);
            reader.Skip(Align(size) - size);
            resources.Add(new Fx2Resource(at, technique, pass, element, state, usage, code));
        }
        if (reader.Position != blob.Length)
        {
            throw new InvalidDataException($"{blob.Length - reader.Position} unparsed bytes after the resources");
        }
        return new Fx2Effect(parameterList, passes, samplerStates, resources);
    }

    internal static int Align(int size) => (size + 3) & ~3;

    /// <summary>Bounds-checked little-endian reads over the blob.</summary>
    private ref struct BlobReader(ReadOnlySpan<byte> blob, int position)
    {
        private readonly ReadOnlySpan<byte> blob = blob;

        public int Position { get; private set; } = position;

        public uint U32()
        {
            var value = At(Position);
            Position += 4;
            return value;
        }

        /// <summary>A u32 count of items of at least itemSize bytes that must fit in the rest of the blob.</summary>
        public int Count(int itemSize)
        {
            var count = U32();
            if ((long)count * itemSize > blob.Length - Position)
            {
                throw new InvalidDataException($"effect count {count} exceeds the remaining data");
            }
            return (int)count;
        }

        public void Skip(int bytes)
        {
            if (bytes < 0 || Position + (long)bytes > blob.Length)
            {
                throw new InvalidDataException("effect structure runs past the end of the data");
            }
            Position += bytes;
        }

        public byte[] Bytes(int count)
        {
            var start = Position;
            Skip(count);
            return blob.Slice(start, count).ToArray();
        }

        /// <summary>A name string (u32 length with the terminating NUL, then bytes); offset 0 is no name.</summary>
        public readonly string Name(int offset)
        {
            if (offset == 0)
            {
                return "";
            }
            var length = At(offset);
            if (length > blob.Length - offset - 4L)
            {
                throw new InvalidDataException($"effect name at {offset} runs past the end of the data");
            }
            var bytes = blob.Slice(offset + 4, (int)length);
            var end = bytes.IndexOf((byte)0);
            return System.Text.Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
        }

        public readonly uint At(int offset) =>
            offset >= 0 && (long)offset + 4 <= blob.Length
                ? BinaryPrimitives.ReadUInt32LittleEndian(blob[offset..])
                : throw new InvalidDataException($"effect offset {offset} outside the data");
    }
}
