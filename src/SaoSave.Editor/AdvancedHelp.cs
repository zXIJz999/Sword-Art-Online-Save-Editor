using System.Text.RegularExpressions;
using SaoSave.Core;

namespace SaoSave.Editor;

/// <summary>Plain-English names and explanations for the Advanced page.</summary>
internal static partial class AdvancedHelp
{
    [GeneratedRegex(@"^(value|u8|u16|bool|string)( \d+)?$")]
    private static partial Regex UnknownName();

    /// <summary>Fields the game stores but nobody knows the meaning of yet (hidden unless asked for).</summary>
    public static bool IsUnknown(SaoNode n) => UnknownName().IsMatch(n.Name);

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Common"] = "Shared by all save slots",
        ["Slot metadata"] = "Load screen info",
        ["Game options"] = "Game options",
        ["Play record"] = "Play record",
        ["Player"] = "Your character",
        ["ArFA-sys"] = "ArFA-sys partner",
        ["Abyss player"] = "Your character (Abyss dungeon)",
        ["Abyss ArFA-sys"] = "ArFA-sys partner (Abyss dungeon)",
        ["Equipment"] = "What each character wears and carries",
        ["Abyss equipment"] = "What each character carries (Abyss)",
        ["Inventory"] = "Item bag",
        ["Equipment sets"] = "Saved equipment sets",
        ["Abyss inventory"] = "Item bag (Abyss)",
        ["Abyss equipment sets"] = "Saved equipment sets (Abyss)",
        ["Progress flags"] = "Story progress flags",
        ["Party"] = "Party members",
        ["Affinity"] = "Affinity with each character",
        ["Quests"] = "Quests",
        ["ArFA-sys data"] = "ArFA-sys partner data",
        ["Shops"] = "Shop stock",
        ["Current map"] = "Current map",
        ["Map position"] = "Position on the map",
        ["Map records"] = "Map progress",
        ["experience"] = "EXP (total)",
        ["credits"] = "Credits (money)",
        ["CP"] = "Customize points (CP)",
        ["SP"] = "Skill points (SP)",
        ["assigned SP"] = "Skill points already spent",
        ["skills"] = "Learned skills (raw)",
        ["stat points"] = "Stat points spent",
        ["equip skill layers"] = "Equipped skills per weapon",
        ["favorite skill sets"] = "Saved skill sets",
        ["look"] = "Looks",
        ["figure"] = "Body shape (raw)",
        ["face"] = "Face preset",
        ["face paint"] = "Face paint",
        ["parts"] = "Hair, face and eyebrows",
        ["figure settings"] = "Body shape sliders",
        ["item id"] = "Item",
        ["quantity"] = "Amount",
        ["flag"] = "Item flags",
        ["category"] = "Item type",
        ["entry id"] = "Bag position",
        ["weapon"] = "Weapon details",
        ["division"] = "Weapon type",
        ["level"] = "Level",
        ["attack"] = "Attack",
        ["chips"] = "Chips",
        ["play time (seconds)"] = "Play time (seconds)",
        ["medals"] = "Medals (attack, support, weapon)",
        ["reward points"] = "Reward points",
        ["reward points received"] = "Reward points already received",
        ["activity counts"] = "Activity counters",
        ["hero impersonator setting"] = "Disguise setting (lobby)",
        ["pocket money"] = "ArFA-sys pocket money",
        ["pocket money (lifetime total)"] = "ArFA-sys pocket money, lifetime total",
        ["bank money"] = "ArFA-sys bank money",
        ["costume change times"] = "Times the costume was changed",
        ["hair change times"] = "Times the hair was changed",
        ["emotion set"] = "Emotes set",
        ["AI type"] = "ArFA-sys AI type",
        ["AI battle style"] = "ArFA-sys battle style",
        ["AI recovery type"] = "ArFA-sys healing behaviour",
        ["AI rescue type"] = "ArFA-sys rescue behaviour",
        ["gender"] = "Gender (0 male, 1 female)",
        ["player name"] = "Player name",
        ["source"] = "Text",
        ["Flags"] = "Flags",
    };

    private static readonly Dictionary<string, string> Help = new(StringComparer.OrdinalIgnoreCase)
    {
        ["experience"] = "Total EXP. The level is worked out from this, so raise it to level up.",
        ["credits"] = "The character's money.",
        ["CP"] = "Points to spend on stats at the customize menu.",
        ["SP"] = "Points to spend on learning skills.",
        ["assigned SP"] = "Skill points the game counts as already spent. Leave alone unless you know why.",
        ["stat points"] = "Points spent on each stat. 0 shows as 1 in game and each point adds 1.",
        ["STR"] = "Strength points spent.", ["AGI"] = "Agility points spent.", ["VIT"] = "Vitality points spent.",
        ["DEX"] = "Dexterity points spent.", ["INT"] = "Intelligence points spent.", ["LUC"] = "Luck points spent.",
        ["skills"] = "The learned skills as raw bytes. Use the Skills page instead.",
        ["equip skill layers"] = "Skills equipped per weapon type. Use the Skills page instead.",
        ["face paint"] = "Name of the face paint, for example FACEPAINT_001, or None.",
        ["parts"] = "Hair, face and eyebrows. Use the Looks and costumes page instead.",
        ["gender"] = "0 is male and 1 is female. Costume names end in M or F, so match them.",
        ["item id"] = "The item's internal code. Change it to turn this slot into a different item.",
        ["quantity"] = "How many of this item you have.",
        ["category"] = "0 weapon, 1 bullet, 2 costume, 3 accessory, 5 material, 7 empty. Do not change this.",
        ["entry id"] = "The item's position in the bag. Do not change this.",
        ["level"] = "Weapon level.",
        ["play time (seconds)"] = "Total play time in seconds. 3600 is one hour.",
        ["medals"] = "Three counts: attack, support and weapon medals.",
        ["reward points"] = "Reward points earned.",
        ["hero impersonator setting"] = "The lobby disguise. 0 is off. Which number is which character is not known yet.",
        ["pocket money"] = "ArFA-sys pocket money.",
        ["bank money"] = "Money in the ArFA-sys bank.",
        ["Progress flags"] = "One byte per story event. Changing them can break the story. Only change them if you know what they do.",
        ["Quests"] = "Every quest and its progress. Changing these can break the story.",
        ["Affinity"] = "A number per character, in the game's character order. Higher means closer.",
        ["Shops"] = "What each shop sells and how much is left.",
        ["Map records"] = "Which parts of each map were explored.",
        ["Equipment"] = "One row per character. Slots are weapon, weapon, costume, accessory, accessory, each a bag position. Use the Looks and costumes page instead.",
        ["Current map"] = "The map the save loads into.",
        ["Game options"] = "The settings from the options menu: volumes, camera, difficulty and so on.",
        ["Difficulty"] = "0 is the easiest. Higher is harder.",
        ["BGM volume"] = "Music volume, 0 to 1.", ["SE volume"] = "Sound effect volume, 0 to 1.", ["Voice volume"] = "Voice volume, 0 to 1.",
    };

    /// <summary>The name shown in the tree.</summary>
    public static string Friendly(SaoNode n)
    {
        if (n.Name.StartsWith("Slot ") && n.Name.Length > 5 && char.IsDigit(n.Name[5]) && n.Parent?.Parent == null && n.Find("Player/name/source")?.Text is { } who)
            return who == "Player" ? $"{n.Name}  (not used yet)" : $"{n.Name}  -  {who}";
        if (n.Name.StartsWith('[') && n.Parent?.Name == "Equipment")
            return n.Name switch { "[1]" => "[1]  Your character", "[2]" => "[2]  ArFA-sys partner", _ => n.Name + "  Companion " + n.Name.Trim('[', ']') };
        if (n.Name.StartsWith("slot ") && n.Parent?.Parent?.Name == "Equipment" && int.TryParse(n.Name[5..], out int k) && k is >= 0 and <= 4)
            return new[] { "Weapon 1", "Weapon 2", "Costume", "Accessory 1", "Accessory 2" }[k];
        return Names.TryGetValue(n.Name, out var f) ? f : n.Name;
    }

    /// <summary>One or two sentences about the selected entry.</summary>
    public static string Describe(SaoNode n)
    {
        if (Help.TryGetValue(n.Name, out var h)) return h;
        if (n.Name.StartsWith("slot ") && n.Parent?.Parent?.Name == "Equipment")
            return "The bag position of what is worn here (-1 is nothing). Use the Looks and costumes page instead.";
        if (IsUnknown(n)) return "A field of the game's own. Nobody knows what it does yet. Leave it alone.";
        if (n.Name.StartsWith("Slot ") && n.Name.Length > 5 && char.IsDigit(n.Name[5]) && n.Parent?.Parent == null)
            return "One of the 4 save files inside this file. Open it with the arrow to see your character, bag, quests and more.";
        return n.Kind switch
        {
            NodeKind.Group => "A folder of values. Open it with the arrow.",
            NodeKind.List => "A list. Open it with the arrow. Use the buttons above to add or remove entries.",
            NodeKind.Bool => "On or Off. Type On or Off.",
            NodeKind.Str => "Text. Click the Value cell and type new text.",
            NodeKind.F32 => "A number with decimals.",
            NodeKind.Bytes => "Raw data. Leave it alone unless you know what it is.",
            _ => "A whole number. Click the Value cell and type a new one.",
        };
    }

    /// <summary>True for entries where a wrong edit can break the save or the story.</summary>
    public static bool Risky(SaoNode n) =>
        n.Name is "Progress flags" or "Quests" or "category" or "entry id" or "Map records" or "assigned SP"
        || n.Parent?.Name is "Progress flags" or "Quests";
}
