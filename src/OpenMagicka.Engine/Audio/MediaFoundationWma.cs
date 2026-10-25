using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenMagicka.Content.Audio;

namespace OpenMagicka.Engine.Audio;

/// <summary>
/// WMA decoded by Windows' own decoder (Media Foundation's WMA decoder transform in <c>wmadmod.dll</c>), so Windows
/// needs nothing installed. The transform takes the wave's packets (the format's packet size each) as WMA 2 with the
/// codec data XAudio's xWMA implies, and gives float or 16-bit samples. Set up as FAudio's WMA decoder does (zlib;
/// its source was read, ours is written for this game's banks). COM is called through its tables: the slots are
/// the Windows SDK's (mfobjects.h, mftransform.h).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class MediaFoundationWma : IWmaDecoder
{
    private const uint Version = 0x00020070, StartupLite = 1;
    private const int NotAccepting = unchecked((int)0xC00D36B5), NeedMoreInput = unchecked((int)0xC00D6D72), StreamChange = unchecked((int)0xC00D6D61);
    private const int Drain = 0x1, BeginStreaming = 0x10000000, EndOfStream = 0x10000002, StartOfStream = 0x10000003;
    private const uint ProvidesSamples = 0x100, CanProvideSamples = 0x200, NoSample = 0x300;

    // IMFAttributes, IMFSample, IMFMediaBuffer, IMFTransform and IClassFactory slots.
    private const int Release = 2, GetUInt32 = 7, GetGuid = 10, SetUInt32 = 21, SetGuid = 24, SetBlob = 26;
    private const int ConvertToContiguousBuffer = 41, AddBuffer = 42, Lock = 3, Unlock = 4, SetCurrentLength = 6;
    private const int GetOutputStreamInfo = 7, GetOutputAvailableType = 14, GetOutputCurrentType = 18, SetInputType = 15, SetOutputType = 16, ProcessMessage = 23,
        ProcessInput = 24, ProcessOutput = 25, CreateInstance = 3;

    private static readonly Guid WmaDecoder = new("2eeb4adf-4578-4d10-bca7-bb955f56320a"), ClassFactory = new("00000001-0000-0000-c000-000000000046"),
        Transform = new("bf94c121-5b05-4e6f-8000-ba598961414d"), MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"),
        SubType = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"), UserData = new("b6bc765f-4c3b-40a4-bd51-2535b66fe09d"),
        BlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d"), BitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669"),
        BytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731"), Channels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a"),
        SampleRate = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba"), Audio = new("73647561-0000-0010-8000-00aa00389b71"),
        Wma2 = new("00000161-0000-0010-8000-00aa00389b71"), Pcm = new("00000001-0000-0010-8000-00aa00389b71"),
        Float = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>How often the decoder may answer without progress before the wave counts as undecodable.</summary>
    private const int MaxStalls = 1000;

    /// <summary>The codec data a WMA 2 wave of a bank goes with (none is stored: XAudio assumes this one).</summary>
    private static readonly byte[] Wma2CodecData = [0, 0, 0, 0, 31, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    private readonly delegate* unmanaged<Guid*, Guid*, nint*, int> getClassObject;

    private MediaFoundationWma(delegate* unmanaged<Guid*, Guid*, nint*, int> getClassObject) => this.getClassObject = getClassObject;

    public string Name => "Windows Media Foundation";

    /// <summary>Windows' decoder; none without Media Foundation or its WMA decoder (an N edition without the media pack).</summary>
    public static MediaFoundationWma? Open()
    {
        try
        {
            if (MFStartup(Version, StartupLite) < 0 || !NativeLibrary.TryLoad("wmadmod.dll", out var module)
                || !NativeLibrary.TryGetExport(module, "DllGetClassObject", out var export))
            {
                return null;
            }
            var decoder = new MediaFoundationWma((delegate* unmanaged<Guid*, Guid*, nint*, int>)export);
            Free(decoder.Create());
            return decoder;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or COMException or InvalidDataException)
        {
            return null;
        }
    }

    public short[] Decode(WaveBankEntry entry, ReadOnlySpan<byte> wave)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var format = entry.Format;
        if (format.Tag != WaveFormat.Wma || format.Wide || format.WmaBlockSize == 0 || format.WmaByteRate == 0)
        {
            throw new InvalidDataException("not a WMA 2 wave");
        }
        var decoder = Create();
        nint output = 0;
        try
        {
            SetTypes(decoder, format);
            var samples = new List<short>(entry.Duration * format.Channels);
            var isFloat = OutputIsFloat(decoder);
            output = OwnOutputSample(decoder);
            Check(Call(decoder, ProcessMessage, BeginStreaming, 0), "begin streaming");
            Check(Call(decoder, ProcessMessage, StartOfStream, 0), "start the stream");
            var stalls = 0;
            for (var at = 0; at < wave.Length;)
            {
                var packet = wave.Slice(at, Math.Min(format.WmaBlockSize, wave.Length - at));
                var result = Input(decoder, packet);
                if (result == NotAccepting)
                {
                    if (++stalls > MaxStalls)
                    {
                        throw new InvalidDataException("Windows' WMA decoder takes no more of the wave");
                    }
                    Pull(decoder, output, samples, format, ref isFloat);
                    continue;
                }
                Check(result, "take a packet");
                (at, stalls) = (at + packet.Length, 0);
                Pull(decoder, output, samples, format, ref isFloat);
            }
            Check(Call(decoder, ProcessMessage, EndOfStream, 0), "end the stream");
            Check(Call(decoder, ProcessMessage, Drain, 0), "drain");
            Pull(decoder, output, samples, format, ref isFloat);
            return WmaDecoders.Fit(CollectionsMarshal.AsSpan(samples), entry);
        }
        finally
        {
            Free(output);
            Free(decoder);
        }
    }

    private nint Create()
    {
        nint factory, transform;
        var (clsid, iid, transformIid) = (WmaDecoder, ClassFactory, Transform);
        Check(getClassObject(&clsid, &iid, &factory), "find the WMA decoder");
        try
        {
            Check(((delegate* unmanaged<nint, nint, Guid*, nint*, int>)Table(factory)[CreateInstance])(factory, 0, &transformIid, &transform), "make the WMA decoder");
        }
        finally
        {
            Free(factory);
        }
        return transform;
    }

    private static void SetTypes(nint decoder, MiniWaveFormat format)
    {
        Check(MFCreateMediaType(out var input), "make a media type");
        try
        {
            SetGuidOf(input, MajorType, Audio);
            SetGuidOf(input, SubType, Wma2);
            fixed (byte* data = Wma2CodecData)
            {
                var key = UserData;
                Check(((delegate* unmanaged<nint, Guid*, byte*, uint, int>)Table(input)[SetBlob])(input, &key, data, (uint)Wma2CodecData.Length), "set the codec data");
            }
            SetUInt(input, BlockAlignment, (uint)format.WmaBlockSize);
            SetUInt(input, BitsPerSample, 16);
            SetUInt(input, BytesPerSecond, (uint)format.AllowedWmaByteRate);              // the bank's own rate may be wrong
            SetUInt(input, Channels, (uint)format.Channels);
            SetUInt(input, SampleRate, (uint)format.SampleRate);
            Check(((delegate* unmanaged<nint, uint, nint, uint, int>)Table(decoder)[SetInputType])(decoder, 0, input, 0), "take the WMA format");
        }
        finally
        {
            Free(input);
        }
        ChooseOutput(decoder, format.Channels, format.SampleRate);
    }

    /// <summary>The first output the decoder offers of the wave's channels and rate, as 16-bit or float samples.</summary>
    private static void ChooseOutput(nint decoder, int channels, int rate)
    {
        for (uint index = 0; ; index++)
        {
            nint type;
            Check(((delegate* unmanaged<nint, uint, uint, nint*, int>)Table(decoder)[GetOutputAvailableType])(decoder, 0, index, &type), "find an output format");
            try
            {
                var subtype = GuidOf(type, SubType);
                var bits = UIntOf(type, BitsPerSample);
                if (GuidOf(type, MajorType) == Audio && ((subtype == Pcm && bits == 16) || (subtype == Float && bits == 32))
                    && UIntOf(type, Channels) == (uint)channels && UIntOf(type, SampleRate) == (uint)rate)
                {
                    Check(((delegate* unmanaged<nint, uint, nint, uint, int>)Table(decoder)[SetOutputType])(decoder, 0, type, 0), "set the output format");
                    return;
                }
            }
            finally
            {
                Free(type);
            }
        }
    }

    private static bool OutputIsFloat(nint decoder)
    {
        nint type;
        Check(((delegate* unmanaged<nint, uint, nint*, int>)Table(decoder)[GetOutputCurrentType])(decoder, 0, &type), "read the output format");
        try
        {
            return GuidOf(type, SubType) == Float;
        }
        finally
        {
            Free(type);
        }
    }

    /// <summary>A sample of our own for a decoder that does not give its own; none when it does.</summary>
    private static nint OwnOutputSample(nint decoder)
    {
        OutputStreamInfo info;
        Check(((delegate* unmanaged<nint, uint, OutputStreamInfo*, int>)Table(decoder)[GetOutputStreamInfo])(decoder, 0, &info), "read the output stream");
        if ((info.Flags & (ProvidesSamples | CanProvideSamples)) != 0)
        {
            return 0;
        }
        Check(MFCreateSample(out var sample), "make a sample");
        nint buffer = 0;
        try
        {
            Check(MFCreateMemoryBuffer(Math.Max(info.Size, 1u << 16), out buffer), "make a buffer");
            Check(((delegate* unmanaged<nint, nint, int>)Table(sample)[AddBuffer])(sample, buffer), "add a buffer");
            return sample;
        }
        catch
        {
            Free(sample);
            throw;
        }
        finally
        {
            Free(buffer);
        }
    }

    private static int Input(nint decoder, ReadOnlySpan<byte> packet)
    {
        Check(MFCreateSample(out var sample), "make a sample");
        try
        {
            Check(MFCreateMemoryBuffer((uint)packet.Length, out var buffer), "make a buffer");
            try
            {
                byte* bytes;
                uint max, length;
                Check(((delegate* unmanaged<nint, byte**, uint*, uint*, int>)Table(buffer)[Lock])(buffer, &bytes, &max, &length), "lock a buffer");
                packet.CopyTo(new Span<byte>(bytes, packet.Length));
                ((delegate* unmanaged<nint, int>)Table(buffer)[Unlock])(buffer);
                Check(((delegate* unmanaged<nint, uint, int>)Table(buffer)[SetCurrentLength])(buffer, (uint)packet.Length), "size a buffer");
                Check(((delegate* unmanaged<nint, nint, int>)Table(sample)[AddBuffer])(sample, buffer), "add a buffer");
            }
            finally
            {
                Free(buffer);
            }
            return ((delegate* unmanaged<nint, uint, nint, uint, int>)Table(decoder)[ProcessInput])(decoder, 0, sample, 0);
        }
        finally
        {
            Free(sample);
        }
    }

    /// <summary>Every sample the decoder has ready, as 16-bit samples.</summary>
    private static void Pull(nint decoder, nint own, List<short> samples, MiniWaveFormat format, ref bool isFloat)
    {
        for (var stalls = 0; ; stalls++)
        {
            if (stalls > MaxStalls)
            {
                throw new InvalidDataException("Windows' WMA decoder gives nothing more");
            }
            var data = new OutputData { Sample = own };
            uint status;
            if (own != 0)
            {
                nint ownBuffer;
                Check(((delegate* unmanaged<nint, nint*, int>)Table(own)[ConvertToContiguousBuffer])(own, &ownBuffer), "reach our buffer");
                ((delegate* unmanaged<nint, uint, int>)Table(ownBuffer)[SetCurrentLength])(ownBuffer, 0);
                Free(ownBuffer);
            }
            var result = ((delegate* unmanaged<nint, uint, uint, OutputData*, uint*, int>)Table(decoder)[ProcessOutput])(decoder, 0, 1, &data, &status);
            Free(data.Events);
            if (result == NeedMoreInput)
            {
                return;
            }
            if (result == StreamChange)
            {
                ChooseOutput(decoder, format.Channels, format.SampleRate);     // a new stream: its output set again
                isFloat = OutputIsFloat(decoder);
                continue;
            }
            try
            {
                Check(result, "decode");
                if ((data.Status & NoSample) == 0 && data.Sample != 0)
                {
                    Collect(data.Sample, samples, isFloat);
                    stalls = 0;
                }
            }
            finally
            {
                if (own == 0)
                {
                    Free(data.Sample);
                }
            }
        }
    }

    private static void Collect(nint sample, List<short> samples, bool isFloat)
    {
        nint buffer;
        Check(((delegate* unmanaged<nint, nint*, int>)Table(sample)[ConvertToContiguousBuffer])(sample, &buffer), "reach the samples");
        try
        {
            byte* bytes;
            uint max, length;
            Check(((delegate* unmanaged<nint, byte**, uint*, uint*, int>)Table(buffer)[Lock])(buffer, &bytes, &max, &length), "lock the samples");
            try
            {
                if (isFloat)
                {
                    foreach (var value in new ReadOnlySpan<float>(bytes, (int)length / 4))
                    {
                        samples.Add((short)Math.Clamp(MathF.Round(value * 32767), short.MinValue, short.MaxValue));
                    }
                }
                else
                {
                    samples.AddRange(new ReadOnlySpan<short>(bytes, (int)length / 2));
                }
            }
            finally
            {
                ((delegate* unmanaged<nint, int>)Table(buffer)[Unlock])(buffer);
            }
        }
        finally
        {
            Free(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OutputStreamInfo
    {
        public uint Flags;
        public uint Size;
        public uint Alignment;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OutputData
    {
        public uint StreamId;
        public nint Sample;
        public uint Status;
        public nint Events;
    }

    private static void** Table(nint thing) => *(void***)thing;

    private static int Call(nint thing, int slot, int message, nuint parameter) =>
        ((delegate* unmanaged<nint, int, nuint, int>)Table(thing)[slot])(thing, message, parameter);

    private static void SetGuidOf(nint attributes, Guid key, Guid value) =>
        Check(((delegate* unmanaged<nint, Guid*, Guid*, int>)Table(attributes)[SetGuid])(attributes, &key, &value), "set a format's kind");

    private static void SetUInt(nint attributes, Guid key, uint value) =>
        Check(((delegate* unmanaged<nint, Guid*, uint, int>)Table(attributes)[SetUInt32])(attributes, &key, value), "set a format's value");

    private static Guid GuidOf(nint attributes, Guid key)
    {
        Guid value;
        return ((delegate* unmanaged<nint, Guid*, Guid*, int>)Table(attributes)[GetGuid])(attributes, &key, &value) < 0 ? Guid.Empty : value;
    }

    private static uint UIntOf(nint attributes, Guid key)
    {
        uint value;
        return ((delegate* unmanaged<nint, Guid*, uint*, int>)Table(attributes)[GetUInt32])(attributes, &key, &value) < 0 ? 0 : value;
    }

    private static void Free(nint thing)
    {
        if (thing != 0)
        {
            ((delegate* unmanaged<nint, uint>)Table(thing)[Release])(thing);
        }
    }

    private static void Check(int result, string doing)
    {
        if (result < 0)
        {
            throw new InvalidDataException($"Windows' WMA decoder could not {doing} (0x{result:X8})");
        }
    }

    [DllImport("mfplat.dll")]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll")]
    private static extern int MFCreateMediaType(out nint type);

    [DllImport("mfplat.dll")]
    private static extern int MFCreateSample(out nint sample);

    [DllImport("mfplat.dll")]
    private static extern int MFCreateMemoryBuffer(uint length, out nint buffer);
}
