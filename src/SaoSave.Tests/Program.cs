using SaoSave.Core;

// Checks a save loads, writes back unchanged, and that edits (quantity, chips, a new item in an
// empty slot) survive a write + re-read with a valid signature.
var original = File.ReadAllBytes(args[0]);
var s = SaoSaveFile.Load(original, "test");
var checks = new List<(string, bool)>();
checks.Add(($"signature valid ({s.Arrays.Count} item arrays, player '{s.PlayerName}')", s.SignatureValid));
checks.Add(("untouched export is byte-identical", s.Export().AsSpan().SequenceEqual(original)));

var inv = s.Inventory!;
Console.WriteLine($"inventory: {inv.Used} of {inv.Slots.Count} used");
var bullet = inv.Slots.First(x => x.Category == ItemCategory.Bullet && !x.IsEmpty);
bullet.Quantity = 9999;
var weapon = inv.Slots.First(x => x.Category == ItemCategory.Weapon && x.Chips.Count > 0);
weapon.Chips[0] = weapon.Chips[0] with { Effect = 0.25f };
// New items through the shared add code: a costume and a weapon (level/variant from its ID).
int emptyIndex = inv.Slots.FindIndex(x => x.IsEmpty);
SaoEdits.AddItem(s, inv, "COS036_010_F");
SaoEdits.AddItem(s, inv, "WSM01_0500");
int weaponIndex = emptyIndex + 1;

var bytes = s.Export();
var back = SaoSaveFile.Load(bytes, "back");
var inv2 = back.Inventory!;
checks.Add(("edited save signature valid", back.SignatureValid));
checks.Add(("bullet quantity 9999", inv2.Slots.First(x => x.Id == bullet.Id).Quantity == 9999));
checks.Add(("chip effect 25%", Math.Abs(inv2.Slots.First(x => x.Id == weapon.Id && x.Chips.Count == weapon.Chips.Count).Chips[0].Effect - 0.25f) < 1e-6));
checks.Add(("added costume present", inv2.Slots[emptyIndex].Id == "COS036_010_F"));
checks.Add(("same number of arrays", back.Arrays.Count == s.Arrays.Count));
checks.Add(("added weapon has level 1 and variant 500, no chips", inv2.Slots[weaponIndex] is { Id: "WSM01_0500", Level: 1, Chips.Count: 0 } w && BitConverter.ToInt32(w.Head, 18) == 500 && w.SlotIndex == weaponIndex));
checks.Add(($"names known: {SaoItems.NameOf("COS036_010_F")}, {SaoItems.NameOf("WSM01_0500")}, chip {SaoItems.ChipName(1)}", SaoItems.Find("WSM01_0500") != null));
checks.Add(($"DLC items tagged: {SaoItems.All.Count(i => i.Dlc)}", SaoItems.All.Count(i => i.Dlc) == 352));
// Characters: values found in the test save, then an edit round trip.
{
    var c0 = SaoSaveFile.Load(original, "chars");
    var p = c0.Characters[0];
    checks.Add(($"{c0.Characters.Count} characters; slot 1 player '{p.Name}' EXP {p.Experience} credits {p.Money} CP {p.CustomizePoints} SP {p.SkillPoints}",
        c0.Characters.Count == 8 && p.Name == "zXIJz" && p.Money == 20875 && p.Experience == 1030 && p.CustomizePoints == 30 && p.SkillPoints == 30));
    p.Money = 9_999_999; p.CustomizePoints = 500; p.SetStat(0, 40); p.SkillPoints = 999;
    var cb = c0.Export();
    var c1 = SaoSaveFile.Load(cb, "chars2");
    var q = c1.Characters[0];
    checks.Add(("character edit: credits, CP, STR, SP read back; size unchanged",
        q.Money == 9_999_999 && q.CustomizePoints == 500 && q.GetStat(0) == 40 && q.SkillPoints == 999 && cb.Length == original.Length && c1.SignatureValid));
}

// Fixed-size fields found through the game's serializer order (values from the test save).
{
    var f0 = SaoSaveFile.Load(original, "fields");
    SaoField F(string group, string name) => f0.Fields.Single(x => x.Group == group && x.Name == name);
    var play = F("Slot 1 play record", "Play time (seconds)");
    var bgm = F("Game options", "BGM volume");
    var diff = F("Game options", "Difficulty");
    var pocket = F("Slot 1 ArFA-sys", "Pocket money");
    var zel = F("Slot 1 affinity", "Zeliska (character 3)");
    checks.Add(($"fields: {f0.Fields.Count}; play time {f0.GetField(play)}s, reward {f0.GetField(F("Slot 1 play record", "Reward points"))}, BGM {f0.GetField(bgm)}, Zeliska {f0.GetField(zel)}",
        f0.GetField(play) == 1176 && f0.GetField(F("Slot 1 play record", "Reward points")) == 1875 && f0.GetField(bgm) == 1
        && f0.GetField(F("Slot 1 play record", "Attack medals")) == 3 && f0.GetField(F("Slot 1 play record", "Weapon medals")) == 4
        && f0.GetField(F("Game options", "Message speed")) == 0.5 && f0.GetField(diff) == 1 && f0.GetField(zel) == 5));
    f0.SetField(pocket, 5_000_000_000); f0.SetField(bgm, 0.4); f0.SetField(zel, 999); f0.SetField(play, 360000);
    var p0 = f0.Characters[0];
    p0.SetSkillSetSlot(0, 2, 115);
    var fb = f0.Export();
    var f1 = SaoSaveFile.Load(fb, "fields2");
    SaoField G(SaoField x) => f1.Fields.Single(y => y.Group == x.Group && y.Name == x.Name);
    checks.Add(("field edits read back (pocket money 5e9, BGM 0.4, affinity, play time, skill set slot); size unchanged",
        f1.GetField(G(pocket)) == 5_000_000_000 && Math.Abs(f1.GetField(G(bgm)) - 0.4) < 1e-6 && f1.GetField(G(zel)) == 999
        && f1.GetField(G(play)) == 360000 && f1.Characters[0].GetSkillSet(0)[2] == 115 && fb.Length == original.Length && f1.SignatureValid));

    checks.Add(($"skill names: {SaoSkills.NameOf(82)}, {SaoSkills.NameOf(115)}", SaoSkills.NameOf(82) == "Dash Attack 1" && SaoSkills.All.Count > 200));
}

// Full parse: the game's whole layout must consume the file exactly.
{
    var raw = File.ReadAllBytes(args[0]);
    var tree = SaoTree.Parse(raw, raw.Length - 24, out var layout);
    var p1 = tree.Find("Slot 1/Player/name/source");
    checks.Add(($"full parse: {tree.Descendants().Count()} values, slot 1 player \"{p1?.Text}\", layout {layout.TableArrays}/{layout.FlagGroupsB}/{layout.FlagBytes}",
        p1?.Text == "zXIJz" && tree.Find("Common/Game options")?.Offset == 0xD20 && tree.Find("Slot 1/Inventory")?.Count == 500));

    // The hand-found fields and characters must sit exactly on the parser's values.
    var sv = SaoSaveFile.Load(raw, "cross");
    string? Expect(SaoField f)
    {
        var m = System.Text.RegularExpressions.Regex.Match(f.Group, @"^Slot (\d+) (play record|ArFA-sys|affinity)$");
        if (f.Group == "Game options") return "Common/Game options/" + f.Name;
        if (!m.Success) return null;
        string slot = $"Slot {m.Groups[1].Value}/";
        return m.Groups[2].Value switch
        {
            "play record" => f.Name switch
            {
                "Play time (seconds)" => slot + "Play record/play time (seconds)",
                "Reward points" => slot + "Play record/reward points",
                "Reward points received" => slot + "Play record/reward points received",
                "Attack medals" => slot + "Play record/medals/[0]",
                "Support medals" => slot + "Play record/medals/[1]",
                "Weapon medals" => slot + "Play record/medals/[2]",
                _ => null,
            },
            "ArFA-sys" => f.Name switch
            {
                "Pocket money" => slot + "ArFA-sys data/pocket money",
                "Pocket money (lifetime total)" => slot + "ArFA-sys data/pocket money (lifetime total)",
                "Bank money" => slot + "ArFA-sys data/bank money",
                "Bank money rate" => slot + "ArFA-sys data/bank money rate",
                "Talk count" => slot + "ArFA-sys data/talk count",
                "AI type" => slot + "ArFA-sys data/AI type",
                "AI battle style" => slot + "ArFA-sys data/AI battle style",
                "AI recovery type" => slot + "ArFA-sys data/AI recovery type",
                "AI rescue type" => slot + "ArFA-sys data/AI rescue type",
                _ => null,
            },
            _ => slot + "Affinity/[" + System.Text.RegularExpressions.Regex.Match(f.Name, @"character (\d+)").Groups[1].Value + "]",
        };
    }
    var wrong = sv.Fields.Where(f => Expect(f) is not { } p || tree.Find(p)?.Offset != f.Offset).Select(f => $"{f.Group}/{f.Name}").ToList();
    var charWrong = sv.Characters.Select((ch, i) => (ch, i)).Where(x => tree.Find($"Slot {x.i / 2 + 1}/{(x.i % 2 == 0 ? "Player" : "ArFA-sys")}/name/source")?.Offset != x.ch.Offset).Count();
    checks.Add(($"hand-found fields match the parser: {sv.Fields.Count - wrong.Count}/{sv.Fields.Count}, characters {sv.Characters.Count - charWrong}/{sv.Characters.Count}" + (wrong.Count > 0 ? " (off: " + string.Join(", ", wrong.Take(5)) + ")" : ""),
        wrong.Count == 0 && charWrong == 0));
}

// PC layer: the same data AES-encrypted as the PC game stores it must open and save back identically.
using (var aes = System.Security.Cryptography.Aes.Create())
{
    aes.Key = System.Text.Encoding.ASCII.GetBytes("A3fzg98u0cPO4dgrnjE0o78ie43g75w2");
    var padded = new byte[(original.Length + 15) / 16 * 16];
    original.CopyTo(padded, 0);
    var pcFile = aes.EncryptEcb(padded, System.Security.Cryptography.PaddingMode.None);
    var pc = SaoSaveFile.Load(pcFile, "pc");
    checks.Add(("PC (AES) save opens", pc.IsPc && pc.Arrays.Count == s.Arrays.Count));
    checks.Add(("PC save writes back identically", pc.Export().AsSpan().SequenceEqual(pcFile)));
}
foreach (var (what, ok) in checks) Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} {what}");
return checks.All(c => c.Item2) ? 0 : 1;
