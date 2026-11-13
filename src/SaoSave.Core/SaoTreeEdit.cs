using System.Globalization;
using System.Text;

namespace SaoSave.Core;

/// <summary>
/// Shows and edits any value of the full layout (<see cref="SaoTree"/>). The save must be
/// <see cref="SaoSaveFile.Committed"/> and the tree parsed from it. Fixed-size values outside the
/// item lists are written in place; everything else (strings, byte arrays, list elements, values
/// inside item lists) builds a new body that must still parse, and returns a new save.
/// </summary>
public static class SaoTreeEdit
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Format(SaoSaveFile s, SaoNode n)
    {
        var b = s.Bytes(n.Offset, n.Length);
        return n.Kind switch
        {
            NodeKind.U8 => b[0].ToString(Inv),
            NodeKind.U16 => BitConverter.ToUInt16(b).ToString(Inv),
            NodeKind.I32 => BitConverter.ToInt32(b).ToString(Inv),
            NodeKind.I64 => BitConverter.ToInt64(b).ToString(Inv),
            NodeKind.F32 => BitConverter.ToSingle(b).ToString("R", Inv),
            NodeKind.Bool => BitConverter.ToInt32(b) != 0 ? "On" : "Off",
            NodeKind.Str => n.Text ?? "",
            NodeKind.Bytes => Convert.ToHexString(b[4..]),
            NodeKind.List => $"{n.Count} items",
            _ => "",
        };
    }

    public static bool Editable(SaoNode n) => n.IsValue || n.Kind is NodeKind.Str or NodeKind.Bytes;

    /// <summary>The new bytes for a leaf, or an error message.</summary>
    public static byte[] Encode(SaoNode n, string text)
    {
        text = text.Trim();
        static T Num<T>(Func<string, IFormatProvider, T> parse, string t, string what)
        {
            try { return parse(t, Inv); }
            catch (Exception e) when (e is FormatException or OverflowException) { throw new ArgumentException($"Not a valid {what}: {t}"); }
        }
        return n.Kind switch
        {
            NodeKind.U8 => [Num(byte.Parse, text, "number 0 to 255")],
            NodeKind.U16 => BitConverter.GetBytes(Num(ushort.Parse, text, "number 0 to 65535")),
            NodeKind.I32 => BitConverter.GetBytes(Num(int.Parse, text, "whole number")),
            NodeKind.I64 => BitConverter.GetBytes(Num(long.Parse, text, "whole number")),
            NodeKind.F32 => BitConverter.GetBytes(Num(float.Parse, text, "number")),
            NodeKind.Bool => BitConverter.GetBytes(text.ToLowerInvariant() is "1" or "true" or "on" or "yes" ? 1 : text.ToLowerInvariant() is "0" or "false" or "off" or "no" ? 0 : throw new ArgumentException("Type On or Off.")),
            NodeKind.Str => FString(text),
            NodeKind.Bytes => ByteArray(text),
            _ => throw new ArgumentException("This entry is a group, not a value."),
        };
    }

    /// <summary>FString bytes: ASCII when possible, else UTF-16 (negative length), with the zero.</summary>
    public static byte[] FString(string text)
    {
        if (text.Length == 0) return new byte[4];
        if (text.All(c => c < 0x80))
        {
            var a = new byte[4 + text.Length + 1];
            BitConverter.GetBytes(text.Length + 1).CopyTo(a, 0);
            Encoding.ASCII.GetBytes(text).CopyTo(a, 4);
            return a;
        }
        var u = new byte[4 + (text.Length + 1) * 2];
        BitConverter.GetBytes(-(text.Length + 1)).CopyTo(u, 0);
        Encoding.Unicode.GetBytes(text).CopyTo(u, 4);
        return u;
    }

    private static byte[] ByteArray(string hex)
    {
        hex = new string(hex.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length % 2 != 0) throw new ArgumentException("Hex needs pairs of digits (00 to FF).");
        var data = Convert.FromHexString(hex);
        var r = new byte[4 + data.Length];
        BitConverter.GetBytes(data.Length).CopyTo(r, 0);
        data.CopyTo(r, 4);
        return r;
    }

    /// <summary>
    /// Sets a value. Returns null when it was written in place (same save object), or the new
    /// save when the body had to be rebuilt.
    /// </summary>
    public static SaoSaveFile? Set(SaoSaveFile s, SaoNode n, string text)
    {
        var bytes = Encode(n, text);
        if (n.IsValue && bytes.Length == n.Length && !s.InItemList(n.Offset, n.Length))
        {
            s.WriteInPlace(n.Offset, bytes);
            return null;
        }
        var body = Splice(s.BodyCopy(), n.Offset, n.Length, bytes);

        // The player's name is also kept in the slot metadata (what the load screen shows).
        var m = System.Text.RegularExpressions.Regex.Match(n.Path, @"^save/Slot (\d+)/Player/name/source$");
        if (m.Success && Root(n).Find($"Common/Slot metadata/[{int.Parse(m.Groups[1].Value) - 1}]/player name/source") is { } meta
            && meta.Offset < n.Offset)
            body = Splice(body, meta.Offset, meta.Length, bytes);
        return s.WithBody(body);
    }

    private static SaoNode Root(SaoNode n) { while (n.Parent != null) n = n.Parent; return n; }

    /// <summary>Adds a copy of the last element of a list (the count is updated).</summary>
    public static SaoSaveFile AddCopy(SaoSaveFile s, SaoNode list)
    {
        if (list.Kind != NodeKind.List || !list.HasCount) throw new ArgumentException("Pick a list whose size is stored in the save.");
        if (list.Children.Count == 0) throw new ArgumentException("The list is empty: there is no element to copy.");
        var last = list.Children[^1];
        var body = s.BodyCopy();
        var copy = body.AsSpan(last.Offset, last.Length).ToArray();
        body = Splice(body, last.Offset + last.Length, 0, copy);
        BitConverter.GetBytes(list.Count + 1).CopyTo(body, list.Offset);
        return s.WithBody(body);
    }

    /// <summary>Removes one element of a list (the count is updated).</summary>
    public static SaoSaveFile Remove(SaoSaveFile s, SaoNode element)
    {
        var list = element.Parent;
        if (list is not { Kind: NodeKind.List, HasCount: true }) throw new ArgumentException("Pick an element of a list whose size is stored in the save.");
        var body = Splice(s.BodyCopy(), element.Offset, element.Length, []);
        BitConverter.GetBytes(list.Count - 1).CopyTo(body, list.Offset);
        return s.WithBody(body);
    }

    public static byte[] Splice(byte[] body, int at, int oldLen, byte[] repl)
    {
        var r = new byte[body.Length - oldLen + repl.Length];
        body.AsSpan(0, at).CopyTo(r);
        repl.CopyTo(r, at);
        body.AsSpan(at + oldLen).CopyTo(r.AsSpan(at + repl.Length));
        return r;
    }

    public static string KindName(SaoNode n) => n.Kind switch
    {
        NodeKind.U8 => "byte", NodeKind.U16 => "u16", NodeKind.I32 => "int", NodeKind.I64 => "int64",
        NodeKind.F32 => "float", NodeKind.Bool => "on/off", NodeKind.Str => "text", NodeKind.Bytes => $"bytes ({n.Count})",
        NodeKind.List => n.HasCount ? "list" : "fixed list", _ => "",
    };
}
