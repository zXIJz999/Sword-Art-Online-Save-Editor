using System.Security.Cryptography;
using System.Text;

namespace SaoSave.Core;

/// <summary>
/// A Sword Art Online: Fatal Bullet save (PS4 <c>ue4savegame.ps4.sav</c>, decrypted).
///
/// The file is the game's own binary layout, not UE4 GVAS. What is known (docs/RESEARCH.md):
/// it ends with <c>14 00 00 00</c> + HMAC-SHA1 of every earlier byte, and items are stored in
/// arrays (int32 count, then records). The editor works on the bytes: unknown regions are never
/// touched, and only records it has parsed are rewritten.
/// </summary>
public sealed class SaoSaveFile
{
    /// <summary>HMAC key the game signs saves with (from alfizari's Apache-2.0 tools).</summary>
    private static readonly byte[] SignatureKey = Encoding.ASCII.GetBytes("1FB00CC8D8D94CD0A94C847C2F04A921");
    private const int SignatureLength = 4 + 20;

    private byte[] _data;

    public string Name { get; }

    /// <summary>Item arrays found in the file, in file order.</summary>
    public List<ItemArray> Arrays { get; } = new();

    public bool SignatureValid { get; }

    /// <summary>PC saves are AES-256-ECB encrypted on top (key from alfizari's Apache-2.0 tools).</summary>
    private static readonly byte[] PcKey = Encoding.ASCII.GetBytes("A3fzg98u0cPO4dgrnjE0o78ie43g75w2");

    /// <summary>True when the file was a PC save (encrypted); it is encrypted again on export.</summary>
    public bool IsPc { get; }

    /// <summary>False for a save the game wrote without the signature (early PC versions).</summary>
    public bool HasSignature { get; }

    private SaoSaveFile(byte[] data, string name)
    {
        Name = name;
        if (data.Length >= 16 && BitConverter.ToInt32(data, 0) != 1 && data.Length % 16 == 0)
        {
            var plain = Pc(data, encrypt: false);
            if (BitConverter.ToInt32(plain, 0) == 1) { data = plain; IsPc = true; }
        }
        if (data.Length < 64 || BitConverter.ToInt32(data, 0) != 1)
            throw new InvalidDataException("Not a Fatal Bullet save (expected 01 00 00 00 at the start). If it is a PS4 save, decrypt it first.");
        _data = data;
        HasSignature = data.Length > SignatureLength && BitConverter.ToInt32(data, data.Length - SignatureLength) == 20;
        SignatureValid = HasSignature && Sign(data.AsSpan(0, data.Length - SignatureLength)).AsSpan().SequenceEqual(data.AsSpan(data.Length - 20));
        FindArrays();
        Characters = SaoCharacter.FindAll(_data, BodyEnd);
        Fields = SaoLayout.Find(_data, BodyEnd, Characters);
    }

    /// <summary>
    /// Options, play record, ArFA-sys money/AI and affinity values. All fixed-size and outside the
    /// item arrays, so edits write straight into the file data and never move anything.
    /// </summary>
    public List<SaoField> Fields { get; private set; } = new();

    public double GetField(SaoField f) => f.Kind switch
    {
        FieldKind.Int32 => BitConverter.ToInt32(_data, f.Offset),
        FieldKind.Int64 => BitConverter.ToInt64(_data, f.Offset),
        FieldKind.Float => BitConverter.ToSingle(_data, f.Offset),
        FieldKind.Byte => _data[f.Offset],
        FieldKind.Bool32 => BitConverter.ToInt32(_data, f.Offset) != 0 ? 1 : 0,
        _ => 0,
    };

    public void SetField(SaoField f, double v)
    {
        var at = _data.AsSpan(f.Offset);
        switch (f.Kind)
        {
            case FieldKind.Int32: BitConverter.TryWriteBytes(at, (int)Math.Clamp(Math.Round(v), int.MinValue, int.MaxValue)); break;
            case FieldKind.Int64: BitConverter.TryWriteBytes(at, (long)Math.Clamp(Math.Round(v), 0, 9_000_000_000_000_000_000d)); break;
            case FieldKind.Float: BitConverter.TryWriteBytes(at, (float)v); break;
            case FieldKind.Byte: _data[f.Offset] = (byte)Math.Clamp(Math.Round(v), 0, 255); break;
            case FieldKind.Bool32: BitConverter.TryWriteBytes(at, v != 0 ? 1 : 0); break;
        }
    }

    /// <summary>Player and ArFA-sys of every save slot. Edits write straight into the file data.</summary>
    public List<SaoCharacter> Characters { get; private set; } = new();

    private static byte[] Pc(byte[] data, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Key = PcKey;
        if (encrypt)
        {
            var padded = new byte[(data.Length + 15) / 16 * 16];
            data.CopyTo(padded, 0);
            return aes.EncryptEcb(padded, PaddingMode.None);
        }
        return aes.DecryptEcb(data, PaddingMode.None);
    }

    /// <summary>Loads a save from memory. The bytes are copied: edits never touch the caller's buffer.</summary>
    public static SaoSaveFile Load(byte[] data, string name) => new((byte[])data.Clone(), name);
    public static SaoSaveFile Load(string path) => new(File.ReadAllBytes(path), path);

    private static byte[] Sign(ReadOnlySpan<byte> body)
    {
        using var h = new HMACSHA1(SignatureKey);
        return h.ComputeHash(body.ToArray());
    }

    /// <summary>End of the game data: the signature (when present) follows it.</summary>
    private int BodyEnd => HasSignature ? _data.Length - SignatureLength : _data.Length;

    /// <summary>Player name stored near the start of the file.</summary>
    public string PlayerName
    {
        get
        {
            for (int p = 0; p < 0x200; p++)
                if (ItemRecord.TryString(_data, p, out var s, out _) && s.Length >= 1) return s;
            return "";
        }
    }

    // ---- finding item arrays -------------------------------------------------------

    private void FindArrays()
    {
        int end = BodyEnd;
        for (int p = 0; p + 8 < end; p++)
        {
            int count = BitConverter.ToInt32(_data, p);
            if (count < 1 || count > 5000) continue;
            var records = new List<ItemRecord>(count);
            int r = p + 4;
            for (int i = 0; i < count; i++)
            {
                var rec = ItemRecord.TryParse(_data, r, end);
                if (rec == null) { records = null; break; }
                records.Add(rec);
                r = rec.End;
            }
            if (records == null || records.Count == 0) continue;
            // An array of plain strings would also parse as "records" only by luck; require
            // the category byte of every real item to be a known one.
            if (records.Any(x => !x.IsEmpty && !Enum.IsDefined(x.Category))) continue;
            Arrays.Add(new ItemArray(Arrays.Count, p, records));
            p = r - 1;
        }
    }

    /// <summary>The player's own bag: the first array of 500 slots.</summary>
    public ItemArray? Inventory => Arrays.FirstOrDefault(a => a.Slots.Count == 500);

    // ---- writing --------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the file with every edited record written back, re-signs it and checks it parses
    /// the same. Records may change size (a longer item ID, more chips); nothing else moves.
    /// </summary>
    public byte[] Export()
    {
        int end = BodyEnd;
        using var ms = new MemoryStream(_data.Length + 4096);
        int at = 0;
        foreach (var a in Arrays)
        {
            ms.Write(_data, at, a.CountOffset - at);
            ms.Write(BitConverter.GetBytes(a.Slots.Count));
            foreach (var rec in a.Slots) rec.WriteTo(ms);
            at = a.Slots.Count == 0 ? a.CountOffset + 4 : a.OriginalEnd;
        }
        ms.Write(_data, at, end - at);
        var signed = Finish(ms.ToArray());

        // Self-check: the new file must parse into the same arrays with the same records.
        var check = new SaoSaveFile(signed, Name);
        if (check.SignatureValid != HasSignature || check.Arrays.Count != Arrays.Count
            || check.Arrays.Zip(Arrays).Any(x => x.First.Slots.Count != x.Second.Slots.Count
                || x.First.Slots.Zip(x.Second.Slots).Any(s => s.First.Id != s.Second.Id)))
            throw new InvalidDataException("Self-check failed: the edited save does not read back the same. Nothing was saved.");

        // Full layout check: when the original reads as the game's layout, the edited file must too.
        if (TryParse(_data, BodyEnd) && !TryParse(check._data, check.BodyEnd))
            throw new InvalidDataException("Self-check failed: the edited save no longer follows the game's layout. Nothing was saved.");
        return signed;
    }

    private static bool TryParse(byte[] d, int end)
    {
        try { SaoTree.Parse(d, end, out _); return true; }
        catch (SaoParseException) { return false; }
    }

    /// <summary>Signs (when the original was signed) and encrypts (PC) a rebuilt body.</summary>
    private byte[] Finish(byte[] body)
    {
        byte[] signed = body;
        if (HasSignature)
        {
            signed = new byte[body.Length + SignatureLength];
            body.CopyTo(signed, 0);
            BitConverter.GetBytes(20).CopyTo(signed, body.Length);
            Sign(body).CopyTo(signed, body.Length + 4);
        }
        if (IsPc) signed = Pc(signed, encrypt: true);
        return signed;
    }

    // ---- full layout (Advanced page) ---------------------------------------------------

    /// <summary>
    /// Writes every pending edit and loads the result fresh, so the file data, the item lists
    /// and the full layout all describe the same bytes. Call before using <see cref="ParseTree"/>.
    /// </summary>
    public SaoSaveFile Committed() => new(Export(), Name);

    /// <summary>The whole save as the game reads it (see <see cref="SaoTree"/>).</summary>
    public SaoNode ParseTree() => SaoTree.Parse(_data, BodyEnd, out _);

    /// <summary>A copy of the game data (everything before the signature).</summary>
    public byte[] BodyCopy() => _data.AsSpan(0, BodyEnd).ToArray();

    /// <summary>Reads bytes of the game data.</summary>
    public ReadOnlySpan<byte> Bytes(int offset, int length) => _data.AsSpan(offset, length);

    /// <summary>True when the bytes overlap an item list this class rewrites on export.</summary>
    public bool InItemList(int offset, int length) =>
        Arrays.Any(a => offset < a.OriginalEnd && offset + length > a.CountOffset);

    /// <summary>Writes a fixed-size value in place (outside item lists only).</summary>
    public void WriteInPlace(int offset, ReadOnlySpan<byte> bytes)
    {
        if (InItemList(offset, bytes.Length)) throw new InvalidOperationException("Inside an item list: use WithBody.");
        bytes.CopyTo(_data.AsSpan(offset));
    }

    /// <summary>
    /// A new save from an edited body (signed and encrypted like this one). The new body must
    /// still parse as the game's layout from start to end, or nothing changes.
    /// </summary>
    public SaoSaveFile WithBody(byte[] body)
    {
        SaoTree.Parse(body, body.Length, out _); // throws SaoParseException when the edit breaks the layout
        return new SaoSaveFile(Finish(body), Name);
    }
}

/// <summary>An array of item records in the save.</summary>
public sealed class ItemArray
{
    public int Index { get; }
    public int CountOffset { get; }
    public int OriginalEnd { get; }
    public List<ItemRecord> Slots { get; }

    public ItemArray(int index, int countOffset, List<ItemRecord> slots)
    {
        Index = index;
        CountOffset = countOffset;
        Slots = slots;
        OriginalEnd = slots[^1].End;
    }

    public int Used => Slots.Count(s => !s.IsEmpty);
}

/// <summary>Item kind, from the byte at +8 of the record head.</summary>
public enum ItemCategory : byte { Weapon = 0, Bullet = 1, Costume = 2, Accessory = 3, Material = 5 }

/// <summary>
/// One item: ID, a 35-byte head, chips, and a 32-byte tail. Head fields that are understood
/// are exposed; the rest is kept byte for byte.
/// </summary>
public sealed class ItemRecord
{
    public const string EmptyId = "None";
    private const int HeadSize = 35, TailSize = 32; // TailSize: the usual size, for new blank records

    public string Id { get; set; } = EmptyId;
    public byte[] Head { get; private set; } = new byte[HeadSize];
    public List<Chip> Chips { get; } = new();
    public byte[] Tail { get; private set; } = new byte[TailSize];

    /// <summary>File offset just after the record, as read.</summary>
    public int End { get; private set; }

    public bool IsEmpty => Id == EmptyId;

    public int Quantity { get => BitConverter.ToInt32(Head, 0); set => BitConverter.TryWriteBytes(Head.AsSpan(0), value); }
    public int State { get => BitConverter.ToInt32(Head, 4); set => BitConverter.TryWriteBytes(Head.AsSpan(4), value); }
    public ItemCategory Category { get => (ItemCategory)Head[8]; set => Head[8] = (byte)value; }
    public int SlotIndex { get => BitConverter.ToInt32(Head, 9); set => BitConverter.TryWriteBytes(Head.AsSpan(9), value); }

    /// <summary>Weapon grade byte (0-7 seen). Other items hold 10 here.</summary>
    public byte Grade { get => Head[13]; set => Head[13] = value; }

    /// <summary>Weapon level (WHG51 = 51, WAS11 = 11...). Weapons only.</summary>
    public int Level { get => BitConverter.ToInt32(Head, 14); set => BitConverter.TryWriteBytes(Head.AsSpan(14), value); }

    public static bool TryString(byte[] d, int p, out string s, out int next)
    {
        s = ""; next = p;
        if (p + 4 > d.Length) return false;
        int n = BitConverter.ToInt32(d, p);
        if (n < 2 || n > 64 || p + 4 + n > d.Length || d[p + 3 + n] != 0) return false;
        for (int k = 0; k < n - 1; k++) if (d[p + 4 + k] < 0x20 || d[p + 4 + k] > 0x7E) return false;
        s = Encoding.ASCII.GetString(d, p + 4, n - 1);
        next = p + 4 + n;
        return true;
    }

    public static ItemRecord? TryParse(byte[] d, int p, int limit)
    {
        if (!TryString(d, p, out var id, out int q)) return null;
        if (q + HeadSize + 4 > limit) return null;
        int chips = BitConverter.ToInt32(d, q + HeadSize);
        if (chips < 0 || chips > 16) return null;
        int tail = q + HeadSize + 4 + chips * 5;
        int tailEnd = TailEnd(d, tail, limit);
        if (tailEnd < 0) return null;
        var r = new ItemRecord { Id = id, Head = d[q..(q + HeadSize)], Tail = d[tail..tailEnd], End = tailEnd };
        for (int i = 0; i < chips; i++)
        {
            int c = q + HeadSize + 4 + i * 5;
            r.Chips.Add(new Chip(d[c], BitConverter.ToSingle(d, c + 1)));
        }
        return r;
    }

    /// <summary>
    /// End of the part after the chips: customize skill count (i32), customize skills (count, 5 bytes
    /// each), then material overrides (count; each u8, i32, two strings). The size varies, so it is
    /// measured. Returns -1 when the bytes are not that.
    /// </summary>
    private static int TailEnd(byte[] d, int p, int limit)
    {
        if (p + 12 > limit) return -1;
        p += 4;                                            // customize skill count
        int pairs = BitConverter.ToInt32(d, p);
        if (pairs < 0 || pairs > 64) return -1;
        p += 4 + pairs * 5;
        if (p + 4 > limit) return -1;
        int overrides = BitConverter.ToInt32(d, p);
        if (overrides < 0 || overrides > 64) return -1;
        p += 4;
        for (int i = 0; i < overrides; i++)
        {
            p += 5;
            for (int k = 0; k < 2; k++)
            {
                if (p + 4 > limit) return -1;
                int n = BitConverter.ToInt32(d, p);
                if (n < 1 || n > 128 || p + 4 + n > limit) return -1;
                p += 4 + n;
            }
        }
        return p <= limit ? p : -1;
    }

    public void WriteTo(Stream s)
    {
        var id = Encoding.ASCII.GetBytes(Id);
        s.Write(BitConverter.GetBytes(id.Length + 1));
        s.Write(id);
        s.WriteByte(0);
        s.Write(Head);
        s.Write(BitConverter.GetBytes(Chips.Count));
        foreach (var c in Chips) { s.WriteByte(c.Id); s.Write(BitConverter.GetBytes(c.Effect)); }
        s.Write(Tail);
    }

    /// <summary>Copy of this record (used as a template when adding an item).</summary>
    public ItemRecord Clone()
    {
        var r = new ItemRecord { Id = Id, Head = (byte[])Head.Clone(), Tail = (byte[])Tail.Clone(), End = End };
        r.Chips.AddRange(Chips.Select(c => c with { }));
        return r;
    }
}

/// <summary>A weapon chip: its ID and effect (0.01 = 1%).</summary>
public sealed record Chip(byte Id, float Effect);
