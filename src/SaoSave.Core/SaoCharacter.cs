using System.Text;

namespace SaoSave.Core;

/// <summary>
/// A character in the save (the player or the ArFA-sys partner of a save slot).
///
/// Layout from the game's own loader for VGSaveCharacterDataBlock (eboot 0x94E020):
/// FString name, u8 gender, i32 experience, i32 money, i32 customize_point (CP),
/// i32 skill_point (SP), i32 assigned_skill_point, TArray&lt;uint8&gt; skills (reflected as a byte
/// array; what each byte means is not confirmed yet, so it is shown read-only), then the
/// VGStatusCustomizePointAssignment struct: i32 strength, agility, vitality, dexterity,
/// intelligence, luck (points spent; a stat with 0 spent shows as 1), then equip_skill
/// (i32 layer count 10, ...). All values here are fixed-size, so editing never moves data.
/// </summary>
public sealed class SaoCharacter
{
    private readonly byte[] _d;

    /// <summary>Offset of the name FString in the save.</summary>
    public int Offset { get; }
    public string Name { get; }
    private readonly int _gender, _ints, _stats;
    public int SkillCount { get; }

    /// <summary>"Slot 1 player", "Slot 1 ArFA-sys"...</summary>
    public string Label { get; internal set; } = "";

    private SaoCharacter(byte[] d, int offset, string name, int gender, int skillCount)
    {
        _d = d;
        Offset = offset;
        Name = name;
        _gender = gender;
        _ints = gender + 1;
        SkillCount = skillCount;
        _stats = _ints + 20 + 4 + skillCount;
    }

    private int I(int at) => BitConverter.ToInt32(_d, at);
    private void I(int at, int v) => BitConverter.TryWriteBytes(_d.AsSpan(at), v);

    public byte Gender => _d[_gender];
    public int Experience { get => I(_ints); set => I(_ints, Math.Max(0, value)); }
    public int Money { get => I(_ints + 4); set => I(_ints + 4, Math.Max(0, value)); }
    public int CustomizePoints { get => I(_ints + 8); set => I(_ints + 8, Math.Max(0, value)); }
    public int SkillPoints { get => I(_ints + 12); set => I(_ints + 12, Math.Max(0, value)); }
    public int AssignedSkillPoints { get => I(_ints + 16); set => I(_ints + 16, Math.Max(0, value)); }

    /// <summary>The raw learned-skills bytes (TArray&lt;uint8&gt;).</summary>
    public IReadOnlyList<byte> SkillBytes => _d.AsSpan(_ints + 24, SkillCount).ToArray();

    public static readonly string[] StatNames = { "STR", "AGI", "VIT", "DEX", "INT", "LUC" };

    /// <summary>Points spent in a stat, in save order STR, AGI, VIT, DEX, INT, LUC.</summary>
    public int GetStat(int i) => I(_stats + i * 4);
    public void SetStat(int i, int v) => I(_stats + i * 4, Math.Max(0, v));


    /// <summary>
    /// Weapon layers of equip_skill. Order follows the weapon division of items (inferred from a
    /// fresh character: layers 1-5 carry the starting skills): handgun, shotgun, SMG, assault
    /// rifle, sniper rifle, heavy, launcher, photon sword, then two more.
    /// </summary>
    public static readonly string[] LayerNames = { "Handgun", "Shotgun", "SMG", "Assault rifle", "Sniper rifle", "Heavy", "Launcher", "Photon sword", "Layer 9", "Layer 10" };

    public int SkillSetLayers => I(_stats + 24);

    private int LayerOffset(int layer)
    {
        int p = _stats + 28;
        for (int k = 0; k < layer; k++) p += 4 + I(p) * 4;
        return p;
    }

    /// <summary>Equipped skill IDs of one weapon layer (-1 = empty).</summary>
    public int[] GetSkillSet(int layer)
    {
        int p = LayerOffset(layer), n = I(p);
        return Enumerable.Range(0, n).Select(i => I(p + 4 + i * 4)).ToArray();
    }

    /// <summary>Changes one equipped skill in place (the slot count never changes).</summary>
    public void SetSkillSetSlot(int layer, int slot, int skillId)
    {
        int p = LayerOffset(layer);
        if ((uint)slot < (uint)I(p)) I(p + 4 + slot * 4, skillId < 0 ? -1 : skillId);
    }

    /// <summary>Finds every character block in the save by its exact layout.</summary>
    internal static List<SaoCharacter> FindAll(byte[] b, int end)
    {
        var list = new List<SaoCharacter>();
        for (int p = 0; p + 64 < end; p++)
        {
            int n = BitConverter.ToInt32(b, p);
            if (n < 2 || n > 40 || p + 4 + n >= end || b[p + 3 + n] != 0) continue;
            bool ascii = true;
            for (int k = 0; k < n - 1 && ascii; k++) ascii = b[p + 4 + k] is >= 0x20 and <= 0x7E;
            if (!ascii) continue;
            int q = p + 4 + n;
            if (b[q] > 2) continue;
            int ints = q + 1;
            bool ok = true;
            for (int k = 0; k < 5 && ok; k++) ok = BitConverter.ToInt32(b, ints + k * 4) is >= 0 and <= 2_000_000_000;
            int skills = BitConverter.ToInt32(b, ints + 20);
            if (!ok || skills is < 0 or > 4096) continue;
            int stats = ints + 24 + skills;
            if (stats + 28 > end) continue;
            for (int k = 0; k < 6 && ok; k++) ok = BitConverter.ToInt32(b, stats + k * 4) >= 0; // any value an edited save may hold
            if (!ok || BitConverter.ToInt32(b, stats + 24) != 10) continue; // equip_skill: 10 weapon layers
            list.Add(new SaoCharacter(b, p, Encoding.ASCII.GetString(b, p + 4, n - 1), q, skills));
            p = stats + 24;
        }
        // Each save slot holds the player then the ArFA-sys partner.
        for (int i = 0; i < list.Count; i++)
            list[i].Label = $"Slot {i / 2 + 1} {(i % 2 == 0 ? "player" : "ArFA-sys")}";
        return list;
    }
}
