using System.Text;

namespace SaoSave.Core;

/// <summary>
/// A whole avatar: gender plus the look bytes (figure, face, face paint, parts with their material
/// overrides, and the figure sliders). The game stores these in the same order in a character, in
/// the load-screen metadata and in a saved avatar preset, so the same bytes fit all three.
/// </summary>
public sealed record AvatarData(byte Gender, bool HasGender, byte[] Look);

public static class SaoAvatar
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SAOAVT01");

    /// <summary>"Slot 1 Player", "Slot 1 ArFA-sys"... for every character the tree holds.</summary>
    public static List<(int slot, bool arfa, string label)> Characters(SaoNode tree, SaoSaveFile s)
    {
        var list = new List<(int, bool, string)>();
        for (int i = 1; i <= 16; i++)
        {
            var slot = tree.Find($"Slot {i}");
            if (slot == null) break;
            foreach (bool arfa in new[] { false, true })
            {
                string who = arfa ? "ArFA-sys" : "Player";
                string name = slot.Find($"{who}/name/source")?.Text ?? "";
                list.Add((i, arfa, $"Slot {i} {who}  ({name})"));
            }
        }
        return list;
    }

    private static (int start, int length)? LookRange(SaoNode tree, int slot, bool arfa)
    {
        var ch = tree.Find($"Slot {slot}/{(arfa ? "ArFA-sys" : "Player")}");
        var look = ch?.Find("look");
        var sliders = ch?.Find("figure settings");
        if (look == null || sliders == null || sliders.Offset != look.Offset + look.Length) return null;
        return (look.Offset, sliders.Offset + sliders.Length - look.Offset);
    }

    /// <summary>Reads the avatar of one character. The save must be the one the tree was parsed from.</summary>
    public static AvatarData? Read(SaoSaveFile s, SaoNode tree, int slot, bool arfa)
    {
        if (LookRange(tree, slot, arfa) is not var (start, length)) return null;
        var gender = tree.Find($"Slot {slot}/{(arfa ? "ArFA-sys" : "Player")}/gender");
        if (gender == null) return null;
        return new AvatarData(s.Bytes(gender.Offset, 1)[0], true, s.Bytes(start, length).ToArray());
    }

    /// <summary>The saved avatar presets of a slot (only the ones in use). They hold no gender.</summary>
    public static List<(string label, AvatarData data)> Presets(SaoSaveFile s, SaoNode tree, int slot)
    {
        var list = new List<(string, AvatarData)>();
        for (int k = 1; k <= 4; k++)
        {
            var group = tree.Find($"Slot {slot}/Play record/avatar presets {k}");
            if (group == null) continue;
            for (int i = 0; i < group.Children.Count; i++)
            {
                var p = group.Children[i];
                var used = p.Find("used");
                var figure = p.Find("figure");
                if (used == null || figure == null || SaoTreeEdit.Format(s, used) != "On") continue;
                int end = p.Offset + p.Length;
                list.Add(($"Saved avatar {k}-{i + 1}", new AvatarData(0, false, s.Bytes(figure.Offset, end - figure.Offset).ToArray())));
            }
        }
        return list;
    }

    /// <summary>
    /// Puts an avatar on a character, and on the load-screen copy of it. Sizes may change, so this
    /// returns a new save (checked against the game's whole layout).
    /// </summary>
    public static SaoSaveFile Apply(SaoSaveFile s, SaoNode tree, int slot, bool arfa, AvatarData a, bool copyGender)
    {
        if (LookRange(tree, slot, arfa) is not var (start, length)) throw new ArgumentException("This character's look could not be located.");
        var edits = new List<(int at, int len, byte[] bytes)> { (start, length, a.Look) };
        if (copyGender && a.HasGender)
        {
            var g = tree.Find($"Slot {slot}/{(arfa ? "ArFA-sys" : "Player")}/gender") ?? throw new ArgumentException("No gender field.");
            edits.Add((g.Offset, 1, new[] { a.Gender }));
        }
        var meta = tree.Find($"Common/Slot metadata/[{slot - 1}]/{(arfa ? "ArFA-sys" : "player")} look");
        if (meta != null) edits.Add((meta.Offset, meta.Length, a.Look));

        var body = s.BodyCopy();
        foreach (var (at, len, bytes) in edits.OrderByDescending(e => e.at))
            body = SaoTreeEdit.Splice(body, at, len, bytes);
        return s.WithBody(body);
    }

    // ---- avatar files ----

    public static byte[] ToFile(AvatarData a)
    {
        var m = new MemoryStream();
        m.Write(Magic);
        m.WriteByte(a.HasGender ? (byte)1 : (byte)0);
        m.WriteByte(a.Gender);
        m.Write(BitConverter.GetBytes(a.Look.Length));
        m.Write(a.Look);
        return m.ToArray();
    }

    public static AvatarData FromFile(byte[] d)
    {
        if (d.Length < Magic.Length + 6 || !d.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("This is not an avatar file made by this editor.");
        int len = BitConverter.ToInt32(d, Magic.Length + 2);
        if (len < 0 || Magic.Length + 6 + len != d.Length) throw new InvalidDataException("This avatar file is damaged.");
        return new AvatarData(d[Magic.Length + 1], d[Magic.Length] == 1, d[(Magic.Length + 6)..]);
    }
}
