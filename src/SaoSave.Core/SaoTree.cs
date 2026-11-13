using System.Text;

namespace SaoSave.Core;

public enum NodeKind { Group, List, U8, U16, I32, I64, F32, Bool, Str, Bytes }

/// <summary>
/// One value or group in the save, as the game's own loader reads it. Every byte of the file
/// belongs to exactly one leaf. <see cref="Path"/> is stable across re-parses (used to find the
/// same value again after an edit that changes sizes).
/// </summary>
public sealed class SaoNode
{
    public string Name { get; }
    public NodeKind Kind { get; }
    public int Offset { get; internal set; }
    public int Length { get; internal set; }
    public SaoNode? Parent { get; private set; }
    public List<SaoNode> Children { get; } = new();

    /// <summary>For Str: the text. For Bytes/List: the element count.</summary>
    public string? Text { get; internal set; }
    public int Count { get; internal set; }

    /// <summary>For List: how many bytes of the list are the element count (4).</summary>
    public bool HasCount { get; internal set; }

    internal SaoNode(string name, NodeKind kind, int offset) { Name = name; Kind = kind; Offset = offset; }

    internal SaoNode Add(SaoNode child) { child.Parent = this; Children.Add(child); return child; }

    public string Path => Parent == null ? Name : Parent.Path + "/" + Name;

    public bool IsValue => Kind is NodeKind.U8 or NodeKind.U16 or NodeKind.I32 or NodeKind.I64 or NodeKind.F32 or NodeKind.Bool;

    public SaoNode? Find(string relPath)
    {
        var n = this;
        foreach (var part in relPath.Split('/'))
        {
            n = n.Children.FirstOrDefault(c => c.Name == part);
            if (n == null) return null;
        }
        return n;
    }

    public IEnumerable<SaoNode> Descendants()
    {
        foreach (var c in Children)
        {
            yield return c;
            foreach (var d in c.Descendants()) yield return d;
        }
    }

    public override string ToString() => $"{Name} @0x{Offset:X} ({Kind}, {Length} bytes)";
}

/// <summary>Thrown when the bytes do not follow the game's layout.</summary>
public sealed class SaoParseException(string message) : Exception(message);

/// <summary>
/// Reads the whole save the way the game does (eboot module 0x94xxxx; see docs/RESEARCH.md).
/// Version-gated fields are all present (current game version). A few element counts are not
/// stored in the file (the game takes them from its data tables); those are passed in as
/// <see cref="Layout"/> and solved by <see cref="SaoTree.Parse"/>.
/// </summary>
internal sealed class TreeReader
{
    private readonly byte[] _d;
    private readonly int _end;
    private int _p;
    private SaoNode _cur;

    public TreeReader(byte[] d, int start, int end, SaoNode root) { _d = d; _p = start; _end = end; _cur = root; }

    public int Pos => _p;

    private void Need(int n)
    {
        if (n < 0 || _p + n > _end) throw new SaoParseException($"read past the end at 0x{_p:X} ({_cur.Path})");
    }

    private SaoNode Leaf(string name, NodeKind kind, int len)
    {
        Need(len);
        var n = _cur.Add(new SaoNode(name, kind, _p) { Length = len });
        _p += len;
        return n;
    }

    public int I32(string name) { var n = Leaf(name, NodeKind.I32, 4); return BitConverter.ToInt32(_d, n.Offset); }
    public void F32(string name) => Leaf(name, NodeKind.F32, 4);
    public void I64(string name) => Leaf(name, NodeKind.I64, 8);
    public void U8(string name) => Leaf(name, NodeKind.U8, 1);
    public void U16(string name) => Leaf(name, NodeKind.U16, 2);
    public void Bool(string name) => Leaf(name, NodeKind.Bool, 4);

    /// <summary>FString: int32 length (with the terminating zero; negative = UTF-16), then text.</summary>
    public string Str(string name)
    {
        Need(4);
        int len = BitConverter.ToInt32(_d, _p);
        int bytes = len >= 0 ? len : checked(-len * 2);
        if (len is > 0x100000 or < -0x100000) throw new SaoParseException($"bad string length {len} at 0x{_p:X} ({_cur.Path}/{name})");
        Need(4 + bytes);
        string text = len > 0 ? Encoding.Latin1.GetString(_d, _p + 4, Math.Max(0, len - 1))
                    : len < 0 ? Encoding.Unicode.GetString(_d, _p + 4, Math.Max(0, bytes - 2)) : "";
        var n = Leaf(name, NodeKind.Str, 4 + bytes);
        n.Text = text;
        return text;
    }

    /// <summary>FText as the game writes it: int32 flags, int8 history type, then by type.</summary>
    public void Text(string name)
    {
        Group(name, () =>
        {
            I32("flags");
            Need(1);
            sbyte type = (sbyte)_d[_p];
            U8("history type");
            if (type == 0) { Str("namespace"); Str("key"); Str("source"); }
            else if (type == -1) { } // empty text: nothing follows in this engine version
            else throw new SaoParseException($"unsupported text history type {type} at 0x{_p - 1:X} ({_cur.Path})");
        });
    }

    /// <summary>TArray&lt;uint8&gt; read in bulk: int32 count, then that many bytes.</summary>
    public void Bytes(string name)
    {
        Need(4);
        int n = BitConverter.ToInt32(_d, _p);
        if (n < 0 || n > 0x1000000) throw new SaoParseException($"bad byte array length {n} at 0x{_p:X} ({_cur.Path}/{name})");
        var node = Leaf(name, NodeKind.Bytes, 4 + n);
        node.Count = n;
    }

    public void Group(string name, Action body)
    {
        var g = _cur.Add(new SaoNode(name, NodeKind.Group, _p));
        var saved = _cur;
        _cur = g;
        try { body(); }
        finally { _cur = saved; g.Length = _p - g.Offset; }
    }

    /// <summary>int32 count, then that many elements.</summary>
    public void List(string name, Action<int> element, int max = 100_000)
    {
        Need(4);
        int n = BitConverter.ToInt32(_d, _p);
        if (n < 0 || n > max) throw new SaoParseException($"bad list count {n} at 0x{_p:X} ({_cur.Path}/{name})");
        var g = _cur.Add(new SaoNode(name, NodeKind.List, _p) { Count = n, HasCount = true });
        _p += 4;
        var saved = _cur;
        _cur = g;
        try { for (int i = 0; i < n; i++) element(i); }
        finally { _cur = saved; g.Length = _p - g.Offset; }
    }

    /// <summary>Elements whose count is not in the file (taken from the game's tables).</summary>
    public void Fixed(string name, int n, Action<int> element)
    {
        var g = _cur.Add(new SaoNode(name, NodeKind.List, _p) { Count = n, HasCount = false });
        var saved = _cur;
        _cur = g;
        try { for (int i = 0; i < n; i++) element(i); }
        finally { _cur = saved; g.Length = _p - g.Offset; }
    }

    public int PeekI32(int ahead = 0) { Need(4 + ahead); return BitConverter.ToInt32(_d, _p + ahead); }

    // ---- shared element readers ----------------------------------------------------

    public void I32List(string name) => List(name, i => I32($"[{i}]"));
    public void StrList(string name) => List(name, i => Str($"[{i}]"));

    /// <summary>int32 count, then (u8, int32) pairs (0x94EF40 / 0x944E70 / 0x94C5D0).</summary>
    public void PairList(string name, string a = "id", string b = "value", bool floatValue = false) =>
        List(name, i => Group($"[{i}]", () => { U8(a); if (floatValue) F32(b); else I32(b); }));

    /// <summary>Material override list (0x94FB50 / 0x94F9D0): u8, int32, name, name.</summary>
    public void MaterialOverrides(string name) =>
        List(name, i => Group($"[{i}]", () => { U8("slot"); I32("index"); Str("material"); Str("parameter"); }));

    /// <summary>Avatar parts (0x94DB70): per part 0x950CE0 (u8, u8, i32, i32) then overrides.</summary>
    public void AvatarParts(string name) =>
        List(name, i => Group($"[{i}]", () =>
        {
            U8("part"); U8("visible"); I32("id"); I32("color");
            MaterialOverrides("material overrides");
        }));
}

/// <summary>Counts the game takes from its data tables (not stored in the save).</summary>
public sealed record Layout(int TableArrays, int FlagGroupsA, int FlagGroupsB, int FlagBytes, int FlagPairsA, int FlagPairsB);

public static class SaoTree
{
    /// <summary>
    /// Parses the body (everything before the signature). Tries the table-driven counts until the
    /// whole file is consumed exactly, so a wrong guess can never be shown as data.
    /// </summary>
    public static SaoNode Parse(byte[] d, int end, out Layout layout)
    {
        // The slots start right after: bool (common chunk end) + int32 slot count. Find the start
        // by parsing the slots from each candidate until one reaches the end exactly.
        var head = new SaoNode("save", NodeKind.Group, 0);
        var r = new TreeReader(d, 0, end, head);
        ReadHeader(r);
        int commonStart = r.Pos;

        // Common chunk up to the table-driven part.
        var probe = new SaoNode("common", NodeKind.Group, commonStart);
        var pr = new TreeReader(d, commonStart, end, probe);
        CommonFixedPart(pr);
        int tableStart = pr.Pos;

        for (int slots = tableStart; slots + 8 <= end; slots++)
        {
            int count = BitConverter.ToInt32(d, slots + 4);
            if (count is < 1 or > 16 || BitConverter.ToInt32(d, slots) is not (0 or 1)) continue;
            // A slot starts with the play record: float play time, then the medal list count.
            float playTime = BitConverter.ToSingle(d, slots + 8);
            int medals = BitConverter.ToInt32(d, slots + 12);
            if (!(playTime >= 0 && playTime < 1e9f) || medals is < 0 or > 64) continue;
            if (BitConverter.ToInt32(d, slots + 16 + medals * 4 + 12) != 15) continue; // activity counts: always 15
            var slotRoot = new SaoNode("x", NodeKind.Group, 0);
            var sr = new TreeReader(d, slots + 8, end, slotRoot);
            try
            {
                for (int s = 0; s < count; s++) sr.Group($"Slot {s + 1}", () => Slot(sr));
            }
            catch (SaoParseException) { continue; }
            if (sr.Pos != end) continue;
            if (!SolveTables(d, tableStart, slots, out var l)) continue;
            layout = l;

            // Final parse with the solved layout.
            var root = new SaoNode("save", NodeKind.Group, 0);
            var fr = new TreeReader(d, 0, end, root);
            ReadHeader(fr);
            fr.Group("Common", () => { CommonFixedPart(fr); CommonTablePart(fr, l); fr.Bool("flag"); });
            int n = fr.I32("slot count");
            for (int s = 0; s < n; s++) fr.Group($"Slot {s + 1}", () => Slot(fr));
            if (fr.Pos != end) throw new SaoParseException($"parse ended at 0x{fr.Pos:X}, file body ends at 0x{end:X}");
            root.Length = end;
            return root;
        }
        throw new SaoParseException("could not find the save slots");
    }

    /// <summary>
    /// Solves the table-driven counts of the common chunk: N int32 lists x3 (0x946010), the 33-byte
    /// block (0x9467F0), two groups of 0x946EB0 records, then a byte block, ending at the bool.
    /// </summary>
    private static bool SolveTables(byte[] d, int start, int slotsAt, out Layout layout)
    {
        layout = new Layout(0, 0, 0, 0, 0, 0);
        int boolAt = slotsAt;
        for (int n = 0; n <= 400; n++)
        {
            // n lists, three times.
            int p = start;
            bool ok = true;
            for (int k = 0; k < 3 * n && ok; k++)
            {
                if (p + 4 > boolAt) { ok = false; break; }
                int c = BitConverter.ToInt32(d, p);
                if (c < 0 || p + 4 + c * 4 > boolAt) { ok = false; break; }
                p += 4 + c * 4;
            }
            if (!ok) break;
            int afterBlock = p + 33;
            if (afterBlock > boolAt) break;
            // Remaining region: groupA * (12 + 8*pairsA) + groupB * (12 + 8*pairsB) + bytes.
            int rest = boolAt - afterBlock;
            if (TryRecords(d, afterBlock, boolAt, out var l)) { layout = l with { TableArrays = n }; return true; }
            _ = rest;
        }
        return false;
    }

    /// <summary>
    /// The tail of the common chunk: two runs of 0x946EB0 records (int32, int32, bool, then a
    /// table-sized run of (bool, int32)), then a table-sized byte block. Picks the split that
    /// matches the record shape (bools are 0 or 1).
    /// </summary>
    private static bool TryRecords(byte[] d, int p, int end, out Layout layout)
    {
        layout = new Layout(0, 0, 0, 0, 0, 0);
        int rest = end - p;
        if (rest == 0) return true;
        for (int pairs = 0; pairs <= 64; pairs++)
        {
            int size = 12 + 8 * pairs;
            // Both record runs have the same shape, so only their total matters for the bytes.
            for (int g = 0; g * size <= rest && g <= 1024; g++)
            {
                int bytes = rest - g * size;
                if (bytes > 4096) continue;
                if (!RecordsLookRight(d, p, g, pairs)) continue;
                layout = new Layout(0, 0, g, bytes, pairs, pairs);
                return true;
            }
        }
        return false;
    }

    private static bool RecordsLookRight(byte[] d, int p, int count, int pairs)
    {
        for (int i = 0; i < count; i++)
        {
            int b = BitConverter.ToInt32(d, p + 8);
            if (b is not (0 or 1)) return false;
            for (int k = 0; k < pairs; k++)
                if (BitConverter.ToInt32(d, p + 12 + k * 8) is not (0 or 1)) return false;
            p += 12 + 8 * pairs;
        }
        return true;
    }

    /// <summary>Debug: parses the slots from a given offset and reports where it stops.</summary>
    public static string DebugSlots(byte[] d, int start, int end, int count)
    {
        var root = new SaoNode("dbg", NodeKind.Group, start);
        var r = new TreeReader(d, start, end, root);
        try { for (int s = 0; s < count; s++) r.Group($"Slot {s + 1}", () => Slot(r)); return $"ok, ended 0x{r.Pos:X} (end 0x{end:X})"; }
        catch (SaoParseException e) { return "stopped: " + e.Message + "\n" + string.Join("\n", Last(root, 12)); }
    }

    /// <summary>Debug: parses the common chunk fixed part from a given offset.</summary>
    public static string DebugCommon(byte[] d, int start, int end)
    {
        var root = new SaoNode("dbg", NodeKind.Group, start);
        var r = new TreeReader(d, start, end, root);
        try { CommonFixedPart(r); return $"ok, ended 0x{r.Pos:X}\n" + string.Join("\n", root.Descendants().Where(x => x.Path.Count(ch => ch == '/') <= 3).Take(80).Select(x => $"{x.Path} @0x{x.Offset:X} len {x.Length} {x.Text}")); }
        catch (SaoParseException e) { return "stopped: " + e.Message + "\n" + string.Join("\n", root.Descendants().Where(x => x.Path.Count(ch => ch == '/') <= 3).Take(80).Select(x => $"{x.Path} @0x{x.Offset:X} len {x.Length} {x.Text}")); }
    }

    private static IEnumerable<string> Last(SaoNode root, int n) =>
        root.Descendants().Where(x => x.Children.Count == 0).TakeLast(n).Select(x => $"{x.Path} @0x{x.Offset:X} len {x.Length} {x.Text}");

    // ---- header and common chunk ---------------------------------------------------

    private static void ReadHeader(TreeReader r) => r.Group("Header", () =>
    {
        r.I32("version major"); r.I32("version minor"); r.I32("version patch"); r.Bool("version flag");
        r.I32("platform value");
    });

    private static void CommonFixedPart(TreeReader r)
    {
        r.I32("value");
        r.List("Slot metadata", i => r.Group($"[{i}]", () => Metadata(r)), 64);
        Options(r);
        r.List("Item box", i => Item(r, i), 5000);
        r.Group("Player card", () => PlayerCard(r));
        r.List("Player cards received", i => r.Group($"[{i}]", () => PlayerCard(r)), 10000);
    }

    private static void CommonTablePart(TreeReader r, Layout l)
    {
        r.Group("Table lists", () =>
        {
            r.Fixed("A", l.TableArrays, i => r.I32List($"[{i}]"));
            r.Fixed("B", l.TableArrays, i => r.I32List($"[{i}]"));
            r.Fixed("C", l.TableArrays, i => r.I32List($"[{i}]"));
        });
        r.Group("Settings block", () =>
        {
            r.U8("u8 0"); r.I32("i32 4"); r.I32("i32 8"); r.I32("i32 12"); r.Bool("bool 16"); r.U8("u8 17");
            r.Bool("bool 18"); r.Bool("bool 19"); r.Bool("bool 20"); r.U8("u8 21"); r.U8("u8 22"); r.U8("u8 23");
        });
        void Rec(int i, int pairs) => r.Group($"[{i}]", () =>
        {
            r.I32("id"); r.I32("value"); r.Bool("flag");
            r.Fixed("entries", pairs, k => r.Group($"[{k}]", () => { r.Bool("on"); r.I32("value"); }));
        });
        r.Fixed("Records A", l.FlagGroupsA, i => Rec(i, l.FlagPairsA));
        r.Fixed("Records B", l.FlagGroupsB, i => Rec(i, l.FlagPairsB));
        r.Fixed("Flags", l.FlagBytes, i => r.U8($"[{i}]"));
    }

    /// <summary>VGSaveSlotMetadataChunk entry (0x942430): what the load screen shows.</summary>
    private static void Metadata(TreeReader r)
    {
        r.Bool("used"); r.I64("saved at (ticks)"); r.Text("player name");
        r.I32("value 40"); r.Bool("bool 44"); r.U8("u8 45"); r.Bool("bool 46"); r.I32("level"); r.Bool("bool 52"); r.Bool("bool 53");
        r.StrList("strings");
        r.Str("string 232"); r.Bool("bool 248");
        r.Group("player look", () => Look(r));
        r.Group("ArFA-sys look", () => Look(r));
    }

    /// <summary>Appearance as the metadata stores it: bytes, 2 names, int32, parts, int32, int32 list.</summary>
    private static void Look(TreeReader r)
    {
        r.Bytes("figure"); r.Str("face"); r.Str("face paint"); r.I32("value");
        r.AvatarParts("parts"); r.I32("value 2"); r.I32List("values");
    }

    /// <summary>VGSaveGameOptionSettingChunk (0x9432F0).</summary>
    private static void Options(TreeReader r) => r.Group("Game options", () =>
    {
        foreach (var f in new[] { "BGM volume", "SE volume", "Voice volume", "Camera speed X", "Camera speed Y", "Aim camera speed X", "Aim camera speed Y", "Scope camera speed X", "Scope camera speed Y" }) r.F32(f);
        r.U8("Camera horizontal"); r.U8("Camera vertical"); r.U8("Controller type");
        foreach (var f in new[] { "Controller vibration", "Status display", "Status display always", "Friend status 3D display", "Enemy status 3D display", "Gun sight display", "Mini map direction fixed", "Mini map display" }) r.Bool(f);
        r.U8("Mini map range");
        foreach (var f in new[] { "Prize display", "Damage display", "Log window display" }) r.Bool(f);
        r.F32("Screen brightness"); r.F32("Message speed"); r.Bool("Demo subtitles"); r.U8("Difficulty");
        foreach (var f in new[] { "Send player card", "Receive player card", "ArFA-sys can join others", "Voice chat", "Aim assist", "Increase reputation" }) r.Bool(f);
        r.Group("Button assignment", () =>
        {
            r.List("actions", i => r.Group($"[{i}]", () => { r.Str("action"); r.I32("key"); }));
            r.List("axes", i => r.Group($"[{i}]", () => { r.Str("axis"); r.Str("key"); }));
        });
    });

    /// <summary>VGSaveInventoryItemEntry (0x94F2D0).</summary>
    internal static void Item(TreeReader r, int i) => r.Group($"[{i}]", () =>
    {
        r.Str("item id"); r.I32("quantity"); r.I32("flag"); r.U8("category"); r.I32("entry id");
        r.Group("weapon", () =>
        {
            r.U8("division"); r.I32("level"); r.I32("variation"); r.I32("attack"); r.U8("equip option");
            // 0x9504B0: two int32, then the chips (u8 id, float effect).
            r.I32("value a"); r.I32("value b");
            r.PairList("chips", "chip", "effect", floatValue: true);
        });
        r.Group("customize", () => { r.I32("customize skill count"); r.PairList("customize skills", "skill", "value"); });
        r.MaterialOverrides("material override");
    });

    /// <summary>VGSavePlayerCardChunk (0x945C80).</summary>
    private static void PlayerCard(TreeReader r)
    {
        r.Bool("valid"); r.Text("name"); r.Text("comment"); r.I32("value 56"); r.I32("value 60");
        r.U16("u16 64"); r.U16("u16 66");
        r.Group("date 1", () => { r.U16("a"); r.U16("b"); r.U16("c"); });
        r.Group("date 2", () => { r.U16("a"); r.U16("b"); r.U16("c"); });
        // 0x945570: u16, two u16 lists, u16, u16.
        r.Group("titles", () =>
        {
            r.U16("selected");
            r.List("list 1", i => r.U16($"[{i}]"));
            r.List("list 2", i => r.U16($"[{i}]"));
            r.U16("u16 a"); r.U16("u16 b");
        });
        r.List("characters", i => r.Group($"[{i}]", () => CardCharacter(r)));
    }

    /// <summary>A character on a player card (0x9449C0).</summary>
    private static void CardCharacter(TreeReader r)
    {
        r.Text("name"); r.U8("gender");
        for (int k = 0; k < 10; k++) r.I32($"value {k}");
        for (int k = 0; k < 5; k++) r.Str($"name {k}");
        r.I32List("list 1"); r.I32List("list 2"); r.I32List("list 3");
        r.Bytes("figure"); r.Str("face"); r.Str("face paint"); r.I32("value");
        r.AvatarParts("parts"); r.I32("value 2"); r.I32List("values");
        r.PairList("pairs 1"); r.PairList("pairs 2"); r.PairList("pairs 3");
    }

    // ---- slot ------------------------------------------------------------------------

    private static void Slot(TreeReader r)
    {
        r.Group("Play record", () => PlayRecord(r));
        r.Group("Player", () => Character(r));
        r.Group("ArFA-sys", () => Character(r));
        r.Group("Abyss player", () => AbyssCharacter(r));
        r.Group("Abyss ArFA-sys", () => AbyssCharacter(r));
        r.List("Equipment", i => r.Group($"[{i}]", () => EquipEntry(r)));
        r.List("Abyss equipment", i => r.Group($"[{i}]", () => EquipEntry(r)));
        r.List("Inventory", i => Item(r, i), 5000);
        r.List("Equipment sets", i => r.List($"[{i}]", k => Item(r, k), 5000), 5000);
        r.List("Abyss inventory", i => Item(r, i), 5000);
        r.List("Abyss equipment sets", i => r.List($"[{i}]", k => Item(r, k), 5000), 5000);
        r.List("Progress flags", i => r.U8($"[{i}]"), 1_000_000);
        r.Group("Party", () =>
        {
            r.List("members", i => r.U8($"[{i}]"), 256);
            r.List("members 2", i => r.U8($"[{i}]"), 256);
            r.List("DLC parties", i => r.Group($"[{i}]", () => { r.Str("name"); r.List("members", k => r.U8($"[{k}]"), 256); }));
            r.List("members 3", i => r.U8($"[{i}]"), 256);
        });
        r.I32List("Affinity");
        r.List("Quests", i => r.Group($"[{i}]", () => Quest(r)));
        r.Group("ArFA-sys data", () => Afasys(r));
        r.I32("value 1200");
        r.List("Shops", i => r.Group($"[{i}]", () => Shop(r)));
        r.Str("Current map");
        r.Group("Map position", () => { r.I32("a"); r.I32("b"); r.I32("c"); });
        r.List("Map records", i => r.Group($"[{i}]", () => { r.Str("map"); r.Bytes("data"); }));
    }

    /// <summary>VGSavePlayRecordChunk (0x947D10).</summary>
    private static void PlayRecord(TreeReader r)
    {
        r.F32("play time (seconds)");
        r.I32List("medals");
        r.I32("reward points"); r.I32("reward points received"); r.I32("status customize slot index");
        r.I32List("activity counts");
        r.U8("hero impersonator setting"); r.I32("legendary"); r.I32("all slot"); r.I32("special");
        r.List("status customize slots", i => r.Group($"[{i}]", () =>
        {
            r.Group("player", () => CustomizePreset(r));
            r.Group("ArFA-sys", () => CustomizePreset(r));
        }));
        for (int k = 1; k <= 4; k++)
            r.List($"avatar presets {k}", i => r.Group($"[{i}]", () => AvatarPreset(r)));
    }

    /// <summary>Status customize preset (0x950810).</summary>
    private static void CustomizePreset(TreeReader r)
    {
        r.I32("value"); r.Bytes("skills");
        r.List("skill layers", i => r.Group($"[{i}]", () => SkillLayer(r)));
        r.List("favorite sets", i => r.List($"[{i}]", k => r.Group($"[{k}]", () => SkillLayer(r))));
        StatPoints(r);
        r.I32("value 80"); r.I32("value 84"); r.I32("value 88"); r.I32("value 92"); r.I32("value 96");
    }

    private static void SkillLayer(TreeReader r)
    {
        int n = r.PeekI32();
        r.I32List("skills");
        _ = n;
    }

    private static void StatPoints(TreeReader r) => r.Group("stat points", () =>
    {
        foreach (var s in new[] { "STR", "AGI", "VIT", "DEX", "INT", "LUC" }) r.I32(s);
    });

    /// <summary>Avatar preset (0x94F640).</summary>
    private static void AvatarPreset(TreeReader r)
    {
        r.Bool("used"); r.Bool("bool 88"); r.Bytes("figure"); r.Str("face"); r.Str("face paint"); r.I32("value");
        r.AvatarParts("parts"); r.I32("value 2"); r.I32List("values");
    }

    /// <summary>VGSaveCharacterDataBlock (0x94E020).</summary>
    private static void Character(TreeReader r)
    {
        r.Text("name"); r.U8("gender");
        r.I32("experience"); r.I32("credits"); r.I32("CP"); r.I32("SP"); r.I32("assigned SP");
        r.Bytes("skills");
        StatPoints(r);
        r.List("equip skill layers", i => r.Group($"[{i}]", () => SkillLayer(r)));
        r.List("favorite skill sets", i => r.List($"[{i}]", k => r.Group($"[{k}]", () => SkillLayer(r))));
        r.Group("look", () =>
        {
            r.Bytes("figure"); r.Str("face"); r.Str("face paint"); r.I32("value");
            r.AvatarParts("parts"); r.I32("value 2");
        });
        r.I32List("figure settings");
    }

    /// <summary>VGSaveAbyssDungeronCharacterDataBlock (0x94E4C0).</summary>
    private static void AbyssCharacter(TreeReader r)
    {
        r.I32("experience"); r.I32("credits"); r.I32("CP");
        StatPoints(r);
        r.List("equip skill layers", i => r.Group($"[{i}]", () => SkillLayer(r)));
        r.List("favorite skill sets", i => r.List($"[{i}]", k => r.Group($"[{k}]", () => SkillLayer(r))));
        r.Bytes("figure"); r.Str("face"); r.Str("face paint"); r.I32("value");
        r.AvatarParts("parts");
    }

    /// <summary>VGSaveCharacterEquipmentEntry (0x94E7E0): 5 int32.</summary>
    private static void EquipEntry(TreeReader r)
    {
        r.I32("slot 0"); r.I32("slot 1"); r.I32("slot 2"); r.I32("slot 3"); r.I32("slot 4");
    }

    /// <summary>Quest record (0x949BE0).</summary>
    private static void Quest(TreeReader r)
    {
        r.Str("quest"); r.Str("task"); r.I32("state"); r.I32("value 20"); r.I32("value 24"); r.I32("value 28");
        r.U8("u8 64"); r.Bool("bool 65"); r.Bool("bool 66"); r.I32("value 68");
        // 0x949E80: (u8, then 0x94A3E0 - entered past its first byte read - list of 0x94A8E0).
        r.List("steps", i => r.Group($"[{i}]", () =>
        {
            r.U8("type");
            r.List("objectives", k => r.Group($"[{k}]", () =>
            {
                r.Str("id"); r.U8("type"); r.Str("text"); r.I32("value"); r.U8("u8"); r.I32("count"); r.I32("max");
                r.Str("target");
                r.List("map", m => r.Group($"[{m}]", () => { r.U8("key"); r.Str("value"); }));
                r.Str("name");
            }));
        }));
        r.List("counters", i => r.Group($"[{i}]", () => { r.U8("type"); r.Str("target"); r.I32("count"); r.I32("max"); r.Str("name"); }));
    }

    /// <summary>VGSaveAfasysDataChunk (0x94B120).</summary>
    private static void Afasys(TreeReader r)
    {
        r.I32("ArFA-sys talk count"); r.I32("talk count");
        r.U8("shopping type 1"); r.U8("shopping type 2"); r.U8("shopping type 3"); r.U8("shopping type 4");
        r.I32("shopping quest clear count");
        r.I64("pocket money"); r.I64("pocket money (lifetime total)");
        r.I32("bank money"); r.I32("bank money rate"); r.U8("bank money operation"); r.I32("bank money quest clear count");
        r.I32List("emotion action ids"); r.Str("emotion set");
        r.U8("AI type"); r.U8("AI battle style"); r.U8("AI recovery type"); r.U8("AI rescue type");
        r.I32List("descriptions"); r.I32List("use functions");
        r.I32("costume change times"); r.I32("hair change times");
        r.I32List("party in times"); r.I32List("party in times 2");
        r.I64("bank money (new)");
    }

    /// <summary>Shop (0x94BE50).</summary>
    private static void Shop(TreeReader r)
    {
        r.Str("shop"); r.Str("inventory"); r.I32("value"); r.Bool("open");
        r.List("lines", i => r.Group($"[{i}]", () =>
        {
            r.U8("type"); r.I32("value 4"); r.Str("item"); r.I32("price"); r.I32("stock"); r.Bool("sold out");
            r.PairList("conditions");
            r.Str("name"); r.I32("value 56"); r.Str("text");
        }));
        r.List("items", i => Item(r, i), 5000);
    }
}
