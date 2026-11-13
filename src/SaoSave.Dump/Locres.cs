using System.Text;

namespace SaoSave.Dump;

/// <summary>
/// Reads a UE4 .locres file (localised text) into (namespace, key, text) rows.
/// Handles the legacy format and versions 1-3 (compact, optimized and CityHash keys).
/// </summary>
public static class Locres
{
    private static readonly byte[] Magic = { 0x0E, 0x14, 0x74, 0x75, 0x67, 0x4A, 0x03, 0xFC, 0x4A, 0x15, 0x90, 0x9D, 0xC3, 0x37, 0x7F, 0x1B };

    public static List<(string ns, string key, string text)> Read(byte[] d)
    {
        int p = 0;
        int version = 0;
        if (d.AsSpan(0, 16).SequenceEqual(Magic)) { p = 16; version = d[p++]; }
        if (version == 0) throw new InvalidDataException("legacy locres not supported");

        long stringsOffset = BitConverter.ToInt64(d, p); p += 8;
        if (version >= 2) p += 4; // entries count

        // Localised strings live in a table at the end; read it first.
        int q = (int)stringsOffset;
        int stringCount = BitConverter.ToInt32(d, q); q += 4;
        var strings = new string[stringCount];
        for (int i = 0; i < stringCount; i++)
        {
            strings[i] = Str(d, ref q);
            if (version >= 3) q += 4; // ref count
        }

        var rows = new List<(string, string, string)>();
        int nsCount = BitConverter.ToInt32(d, p); p += 4;
        for (int n = 0; n < nsCount; n++)
        {
            if (version >= 2) p += 4; // namespace hash
            string ns = Str(d, ref p);
            int keyCount = BitConverter.ToInt32(d, p); p += 4;
            for (int k = 0; k < keyCount; k++)
            {
                if (version >= 2) p += 4; // key hash
                string key = Str(d, ref p);
                p += 4; // source string hash
                int index = BitConverter.ToInt32(d, p); p += 4;
                rows.Add((ns, key, index >= 0 && index < strings.Length ? strings[index] : ""));
            }
        }
        return rows;
    }

    private static string Str(byte[] d, ref int p)
    {
        int len = BitConverter.ToInt32(d, p); p += 4;
        if (len == 0) return "";
        if (len > 0) { var s = Encoding.UTF8.GetString(d, p, len - 1); p += len; return s; }
        var w = Encoding.Unicode.GetString(d, p, (-len - 1) * 2); p += -len * 2; return w;
    }
}
