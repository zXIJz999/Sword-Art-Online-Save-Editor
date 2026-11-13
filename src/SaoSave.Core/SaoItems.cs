using System.Reflection;

namespace SaoSave.Core;

/// <summary>One item the game knows: ID, English name, kind, and whether it comes from paid DLC.</summary>
public sealed record SaoItem(string Id, string Name, string Kind, bool Dlc);

/// <summary>
/// Item and chip names. items.tsv / chips.tsv are built from alfizari's Apache-2.0
/// items.json / chips.json; the DLC flag comes from the game's own ContentDefinition asset.
/// </summary>
public static class SaoItems
{
    private static List<SaoItem>? _all;
    private static Dictionary<string, SaoItem>? _byId;
    private static List<(byte id, string name)>? _chips;

    public static IReadOnlyList<SaoItem> All => _all ??= LoadItems();

    public static SaoItem? Find(string id)
    {
        _byId ??= All.GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _byId.TryGetValue(id, out var i) ? i : null;
    }

    public static string NameOf(string id) => id == ItemRecord.EmptyId ? "(empty)" : Find(id)?.Name ?? id;

    public static IReadOnlyList<(byte id, string name)> Chips => _chips ??= LoadChips();

    public static string ChipName(byte id) => id == 0 ? "(none)" : Chips.FirstOrDefault(c => c.id == id).name ?? $"Chip {id}";

    /// <summary>The save's category byte for an item ID, from its prefix.</summary>
    public static ItemCategory? CategoryOf(string id) =>
        id.StartsWith('W') ? ItemCategory.Weapon
        : id.StartsWith("BUL", StringComparison.Ordinal) ? ItemCategory.Bullet
        : id.StartsWith("COS", StringComparison.Ordinal) ? ItemCategory.Costume
        : id.StartsWith("ACC", StringComparison.Ordinal) ? ItemCategory.Accessory
        : id.StartsWith("MAT", StringComparison.Ordinal) ? ItemCategory.Material
        : null;

    private static Stream Resource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream("SaoSave.Core." + name)
        ?? throw new InvalidOperationException(name + " is not embedded");

    private static List<SaoItem> LoadItems()
    {
        using var r = new StreamReader(Resource("items.tsv"));
        r.ReadLine();
        var list = new List<SaoItem>();
        while (r.ReadLine() is { } line)
        {
            var f = line.Split('\t');
            if (f.Length >= 4) list.Add(new SaoItem(f[0], f[1], f[2], f[3] == "1"));
        }
        return list;
    }

    private static List<(byte, string)> LoadChips()
    {
        using var r = new StreamReader(Resource("chips.tsv"));
        r.ReadLine();
        var list = new List<(byte, string)>();
        while (r.ReadLine() is { } line)
        {
            var f = line.Split('\t');
            if (f.Length >= 2 && byte.TryParse(f[0], out var id)) list.Add((id, f[1].Replace(" (DONT EDIT)", "")));
        }
        return list.OrderBy(c => c.Item1).ToList();
    }
}

/// <summary>Edits shared by the desktop and web editors, so both write the same bytes.</summary>
public static class SaoEdits
{
    /// <summary>
    /// Puts an item into the first empty slot of <paramref name="bag"/>. The new record copies an
    /// existing item of the same kind (so every unknown byte has a value the game wrote itself),
    /// then gets its own ID, slot index and, for weapons, the level and variant its ID encodes
    /// (WSM01_0500 = level 1, variant 500). Returns false when there is no free slot or no item of
    /// that kind to copy.
    /// </summary>
    public static bool AddItem(SaoSaveFile save, ItemArray bag, string id, int quantity = 1)
    {
        var category = SaoItems.CategoryOf(id);
        if (category == null) return false;
        int slot = bag.Slots.FindIndex(s => s.IsEmpty);
        if (slot < 0) return false;
        var template = bag.Slots.FirstOrDefault(s => !s.IsEmpty && s.Category == category)
                       ?? save.Arrays.SelectMany(a => a.Slots).FirstOrDefault(s => !s.IsEmpty && s.Category == category);
        if (template == null) return false;

        var rec = template.Clone();
        rec.Id = id;
        rec.SlotIndex = slot;
        rec.Quantity = quantity;
        rec.State = bag.Slots.Where(s => !s.IsEmpty).Select(s => s.State).DefaultIfEmpty(2).GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
        if (category == ItemCategory.Weapon)
        {
            rec.Chips.Clear();
            if (WeaponDivision(id) is { } div) rec.Grade = div;
            var m =System.Text.RegularExpressions.Regex.Match(id, @"^W[A-Z]{2}(\d\d)_(\d{4})$");
            if (m.Success)
            {
                rec.Level = int.Parse(m.Groups[1].Value);
                BitConverter.TryWriteBytes(rec.Head.AsSpan(18), int.Parse(m.Groups[2].Value));
            }
        }
        bag.Slots[slot] = rec;
        return true;
    }

    /// <summary>
    /// The weapon type byte the game keeps in every weapon record, from the item id's prefix:
    /// 0 handgun, 1 shotgun, 2 SMG, 3 assault rifle, 4 sniper, 5 heavy, 6 launcher, 7 photon sword.
    /// Confirmed against weapons the game itself placed in the save, except heavy (by order).
    /// </summary>
    public static byte? WeaponDivision(string id) => id.Length >= 3 && id[0] == 'W' ? id[1..3] switch
    {
        "HG" => 0, "SG" => 1, "SM" => 2, "AS" => 3, "SR" => 4, "HF" => 5, "LC" => 6, "OS" => 7, _ => null,
    } : null;

    /// <summary>
    /// Sets the weapon type of every weapon in the save to what its id says. Guns added by older
    /// versions of this editor all carry "handgun" and cannot be equipped. Returns how many changed.
    /// </summary>
    public static int FixWeaponTypes(SaoSaveFile save)
    {
        int n = 0;
        foreach (var a in save.Arrays)
            foreach (var s in a.Slots)
                if (!s.IsEmpty && s.Category == ItemCategory.Weapon && WeaponDivision(s.Id) is { } d && s.Grade != d) { s.Grade = d; n++; }
        return n;
    }

    /// <summary>Items of <paramref name="wanted"/> the bag does not hold yet.</summary>
    public static List<SaoItem> Missing(ItemArray bag, IEnumerable<SaoItem> wanted)
    {
        var owned = bag.Slots.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        return wanted.Where(i => !owned.Contains(i.Id) && SaoItems.CategoryOf(i.Id) != null).ToList();
    }
}
