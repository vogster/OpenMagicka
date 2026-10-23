namespace OpenMagicka.Tests.Content;

/// <summary>Writes the pieces Magicka's entity templates are made of (docs/formats/XNB_READERS.md §5).</summary>
internal static class TemplateTestWriter
{
    public const string Magicka = ", Magicka, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
    public const string Readers = "Magicka.ContentReaders.";

    public static XnbTestWriter S(this XnbTestWriter w, string value)
    {
        w.Writer.Write(value);
        return w;
    }

    public static XnbTestWriter I(this XnbTestWriter w, params int[] values)
    {
        foreach (var v in values)
        {
            w.Writer.Write(v);
        }
        return w;
    }

    public static XnbTestWriter B(this XnbTestWriter w, params bool[] values)
    {
        foreach (var v in values)
        {
            w.Writer.Write(v);
        }
        return w;
    }

    public static XnbTestWriter F(this XnbTestWriter w, params float[] values) => w.Floats(values);

    /// <summary>attack properties, elements, f32 amount, magnitude</summary>
    public static XnbTestWriter Damage(this XnbTestWriter w, int attack, int elements, float amount, float magnitude) =>
        w.I(attack, elements).F(amount, magnitude);

    public static XnbTestWriter IntDamage(this XnbTestWriter w, int attack, int elements, int amount, float magnitude) =>
        w.I(attack, elements, amount).F(magnitude);

    public static XnbTestWriter PointLight(this XnbTestWriter w) => w.F(2, 1, 1, 1, 0, 0, 0, 0.5f).Bytes(2).F(0.1f, 3);

    /// <summary>27 animation sets; the set at <paramref name="setIndex"/> gets one animation written by body.</summary>
    public static XnbTestWriter AnimationSets(this XnbTestWriter w, int setIndex = -1, Action<XnbTestWriter>? body = null)
    {
        for (var set = 0; set < 27; set++)
        {
            if (set == setIndex && body is not null)
            {
                w.I(1);
                body(w);
            }
            else
            {
                w.I(0);
            }
        }
        return w;
    }

    /// <summary>One condition collection with the given events (each writes its own type byte and fields).</summary>
    public static XnbTestWriter Conditions(this XnbTestWriter w, params Action<XnbTestWriter>[] events)
    {
        if (events.Length == 0)
        {
            return w.I(0);
        }
        w.I(1).Bytes(0x20).I(0, 0).F(0, 0).B(false).I(events.Length);
        foreach (var e in events)
        {
            e(w);
        }
        return w;
    }
}
