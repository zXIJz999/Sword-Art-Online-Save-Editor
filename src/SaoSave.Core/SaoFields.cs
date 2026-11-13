using System.Text;

namespace SaoSave.Core;

public enum FieldKind { Int32, Int64, Float, Byte, Bool32 }

/// <summary>One fixed-size value in the save, found through the game's own serializer layout.</summary>
public sealed record SaoField(string Group, string Name, int Offset, FieldKind Kind, string Note = "");

/// <summary>
/// Locates the parts of the save outside characters and item arrays, using the order the game's
/// serializers read them (eboot module 0x94xxxx, see docs/RESEARCH.md). Each part is found by a
/// verified anchor and checked before it is offered for editing.
/// </summary>
internal static class SaoLayout
{
    private static int I(byte[] b, int p) => BitConverter.ToInt32(b, p);

    /// <summary>Companion names by character number (game text CMN_Char_D_UCRnnn).</summary>
    public static readonly Dictionary<int, string> CharacterNames = new()
    {
        [0] = "Player", [1] = "ArFA-sys", [2] = "Kureha", [3] = "Zeliska", [4] = "Itsuki", [5] = "Daisy",
        [6] = "Taipan", [7] = "Bazalt Joe", [8] = "Kirito", [9] = "Asuna", [10] = "Sinon", [11] = "Leafa",
        [12] = "Yui", [13] = "Lisbeth", [14] = "Silica", [15] = "Yuuki", [16] = "Argo", [17] = "Klein",
        [18] = "Agil", [21] = "Spiegel", [22] = "LLENN", [23] = "Fukaziroh", [24] = "Pitohui", [25] = "M",
        [26] = "Premiere", [27] = "Strea", [28] = "Philia", [29] = "Rain", [30] = "Yamikaze",
        [32] = "Musketeer X", [33] = "Dyne", [37] = "Shirley", [38] = "Clarence", [39] = "Lievre",
        [42] = "Alice", [43] = "Eugeo", [45] = "Seven", [46] = "Yuna", [47] = "Eiji", [48] = "Sachi",
    };

    public static List<SaoField> Find(byte[] b, int end, List<SaoCharacter> chars)
    {
        var fields = new List<SaoField>();
        Options(b, fields);
        for (int s = 0; s * 2 < chars.Count; s++)
        {
            int player = chars[s * 2].Offset;
            int nextSlot = s * 2 + 2 < chars.Count ? chars[s * 2 + 2].Offset : end;
            string slot = $"Slot {s + 1}";
            PlayRecord(b, player, slot, fields);
            int afasys = ArfaSys(b, player, nextSlot, slot, fields);
            if (afasys > 0) Reputation(b, player, afasys, slot, fields);
        }
        return fields;
    }

    /// <summary>
    /// VGSaveGameOptionSettingChunk (reader 0x9432F0): 9 floats, 3 bytes, bools stored as int32,
    /// then input_assignment. Anchored on the key map that follows (count + "Gamepad_..." name).
    /// </summary>
    private static void Options(byte[] b, List<SaoField> f)
    {
        for (int p = 0x79; p + 16 < Math.Min(b.Length, 0x20000); p++)
        {
            int count = I(b, p), n = I(b, p + 4);
            if (count is < 1 or > 400 || n is < 5 or > 64 || p + 8 + n > b.Length) continue;
            if (Encoding.ASCII.GetString(b, p + 8, Math.Min(8, n - 1)) != "Gamepad_" && Encoding.ASCII.GetString(b, p + 8, Math.Min(3, n - 1)) != "Key") continue;
            int o = p - 0x79;
            bool floats = true;
            for (int k = 0; k < 9 && floats; k++) { float v = BitConverter.ToSingle(b, o + k * 4); floats = v is >= 0 and <= 10; }
            if (!floats) continue;
            const string g = "Game options";
            string[] fl = { "BGM volume", "SE volume", "Voice volume", "Camera speed X", "Camera speed Y", "Aim camera speed X", "Aim camera speed Y", "Scope camera speed X", "Scope camera speed Y" };
            for (int k = 0; k < 9; k++) f.Add(new(g, fl[k], o + k * 4, FieldKind.Float, "0 to 1"));
            f.Add(new(g, "Camera horizontal", o + 36, FieldKind.Byte, "0 normal, 1 inverted"));
            f.Add(new(g, "Camera vertical", o + 37, FieldKind.Byte, "0 normal, 1 inverted"));
            f.Add(new(g, "Controller type", o + 38, FieldKind.Byte));
            string[] bools1 = { "Controller vibration", "Status display", "Status display always", "Friend status 3D display", "Enemy status 3D display", "Gun sight display", "Mini map direction fixed", "Mini map display" };
            for (int k = 0; k < 8; k++) f.Add(new(g, bools1[k], o + 39 + k * 4, FieldKind.Bool32));
            f.Add(new(g, "Mini map range", o + 71, FieldKind.Byte));
            string[] bools2 = { "Prize display", "Damage display", "Log window display" };
            for (int k = 0; k < 3; k++) f.Add(new(g, bools2[k], o + 72 + k * 4, FieldKind.Bool32));
            f.Add(new(g, "Screen brightness", o + 84, FieldKind.Float, "0 to 1"));
            f.Add(new(g, "Message speed", o + 88, FieldKind.Float, "0 to 1"));
            f.Add(new(g, "Demo subtitles", o + 92, FieldKind.Bool32));
            f.Add(new(g, "Difficulty", o + 96, FieldKind.Byte, "game difficulty setting (small number)"));
            string[] bools3 = { "Send player card", "Receive player card", "ArFA-sys can join others", "Voice chat", "Aim assist", "Increase reputation" };
            for (int k = 0; k < 6; k++) f.Add(new(g, bools3[k], o + 97 + k * 4, FieldKind.Bool32));
            return;
        }
    }

    /// <summary>
    /// VGSavePlayRecordChunk (reader 0x947D10), the first part of a slot: play_time (float,
    /// seconds), medals (TArray&lt;i32&gt;), reward_point, reward_received_point,
    /// status_customize_slot_index, activity_counts (always 15). Found by walking back from the
    /// slot's player block.
    /// </summary>
    private static void PlayRecord(byte[] b, int player, string slot, List<SaoField> f)
    {
        for (int p = player - 64; p > Math.Max(0, player - 0x6000); p--)
        {
            if (I(b, p) != 15) continue;
            bool ok = true;
            for (int k = 1; k <= 15 && ok; k++) ok = I(b, p + k * 4) is >= 0 and <= 100_000_000;
            if (!ok) continue;
            // Largest medal count that fits: a short count could match a zero medal value.
            for (int n = 16; n >= 0; n--)
            {
                int c = p - 12 - 4 * n - 4;
                if (c < 4 || I(b, c) != n) continue;
                float t = BitConverter.ToSingle(b, c - 4);
                if (t is < 0 or > 1e9f) continue;
                string g = $"{slot} play record";
                f.Add(new(g, "Play time (seconds)", c - 4, FieldKind.Float));
                f.Add(new(g, "Reward points", p - 12, FieldKind.Int32));
                f.Add(new(g, "Reward points received", p - 8, FieldKind.Int32));
                // Medal counts by type (game text SYS_Badge_0004-0006), spent at the Medal Trader.
                string[] medal = { "Attack medals", "Support medals", "Weapon medals" };
                for (int k = 0; k < n; k++) f.Add(new(g, k < medal.Length ? medal[k] : $"Medals {k + 1}", c + 4 + k * 4, FieldKind.Int32, "medal count"));
                return;
            }
        }
    }

    /// <summary>
    /// VGSaveAfasysDataChunk (reader 0x94B120): afasys_talk_count, talk_count, 4 shopping bytes,
    /// shoping_quest_clear_count, pocket_money (i64), grand_total_pocket_money (i64), bank_money,
    /// bank_money_rate, bank_money_operation (u8), bank_money_quest_clear_count,
    /// emotion_action_ids (TArray&lt;i32&gt;), emotion_set_id (name), ai_type, ai_battle_style_type,
    /// ai_recovery_type, ai_rescue_type. Anchored on the emotion set name. Returns the chunk start.
    /// </summary>
    private static int ArfaSys(byte[] b, int from, int to, string slot, List<SaoField> f)
    {
        for (int p = from; p + 64 < to; p++)
        {
            int n = I(b, p);
            if (n is < 1 or > 64) continue;
            int s = p + 4 + n * 4;
            if (s + 12 > to) continue;
            int len = I(b, s);
            if (len is < 3 or > 24 || b[s + 3 + len] != 0) continue;
            bool name = true;
            // Emotion set names are letters only (BRIGHT...); item IDs always carry digits.
            for (int k = 0; k < len - 1 && name; k++) name = b[s + 4 + k] is >= (byte)'A' and <= (byte)'Z' or (byte)'_';
            for (int k = 0; k < n && name; k++) name = I(b, p + 4 + k * 4) is > 0 and < 100_000;
            if (!name) continue;
            int ai = s + 4 + len;
            if (b[ai] > 20 || b[ai + 1] > 20 || b[ai + 2] > 20 || b[ai + 3] > 20) continue;
            int o = p - 45;
            if (o <= from || BitConverter.ToInt64(b, o + 16) < 0 || BitConverter.ToInt64(b, o + 24) < 0) continue;
            string g = $"{slot} ArFA-sys";
            f.Add(new(g, "Pocket money", o + 16, FieldKind.Int64));
            f.Add(new(g, "Pocket money (lifetime total)", o + 24, FieldKind.Int64));
            f.Add(new(g, "Bank money", o + 32, FieldKind.Int32));
            f.Add(new(g, "Bank money rate", o + 36, FieldKind.Int32));
            f.Add(new(g, "Talk count", o + 4, FieldKind.Int32));
            f.Add(new(g, "AI type", ai, FieldKind.Byte));
            f.Add(new(g, "AI battle style", ai + 1, FieldKind.Byte));
            f.Add(new(g, "AI recovery type", ai + 2, FieldKind.Byte));
            f.Add(new(g, "AI rescue type", ai + 3, FieldKind.Byte));
            return o;
        }
        return -1;
    }

    /// <summary>
    /// VGSaveReputationChunk: TArray&lt;i32&gt; of 50 affinity values (index = character number),
    /// directly followed by the quest chunk (count, then records starting with a name). Found
    /// walking back from the ArFA-sys chunk.
    /// </summary>
    private static void Reputation(byte[] b, int from, int afasys, string slot, List<SaoField> f)
    {
        for (int p = afasys - 208; p > from; p--)
        {
            if (I(b, p) != 50) continue;
            bool ok = true;
            for (int k = 0; k < 50 && ok; k++) ok = I(b, p + 4 + k * 4) is > -1_000_000 and < 1_000_000;
            if (!ok) continue;
            int quests = I(b, p + 204);
            if (quests < 0 || quests > 5000) continue;
            if (quests == 0 ? p + 208 != afasys : !(I(b, p + 208) is >= 2 and <= 128)) continue;
            string g = $"{slot} affinity";
            foreach (var (id, nm) in CharacterNames)
                if (id >= 2) f.Add(new(g, $"{nm} (character {id})", p + 4 + id * 4, FieldKind.Int32));
            return;
        }
    }
}

/// <summary>Skill names, SP cost and weapon types (from the game's VGSkillHeader assets).</summary>
public sealed record SaoSkill(int Id, string Name, int Sp, string Types);

public static class SaoSkills
{
    private static List<SaoSkill>? _all;
    public static IReadOnlyList<SaoSkill> All => _all ??= Load();

    /// <summary>Skills a player can learn: every skill except the enemy-only weapon arts.</summary>
    public static IEnumerable<SaoSkill> Learnable => All.Where(s => !s.Name.Contains("Enemy", StringComparison.OrdinalIgnoreCase));

    public static string NameOf(int id) => id < 0 ? "(empty)" : All.FirstOrDefault(s => s.Id == id)?.Name ?? $"Skill {id}";

    private static List<SaoSkill> Load()
    {
        using var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("SaoSave.Core.skills.tsv")!;
        using var r = new StreamReader(s);
        r.ReadLine();
        var list = new List<SaoSkill>();
        while (r.ReadLine() is { } line)
        {
            var f = line.Split('\t');
            if (f.Length >= 4 && int.TryParse(f[0], out var id))
                list.Add(new SaoSkill(id, f[1], int.TryParse(f[2], out var sp) ? sp : 0, f[3]));
        }
        return list.OrderBy(x => x.Id).ToList();
    }
}
