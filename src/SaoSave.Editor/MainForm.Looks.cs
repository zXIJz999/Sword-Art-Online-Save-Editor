using System.Drawing;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using SaoSave.Core;

namespace SaoSave.Editor;

/// <summary>
/// Looks and costumes page: name, gender, hair / face / eyebrows, face paint, and what each
/// player or ArFA-sys partner wears and carries. Built on the full-layout tree (like Advanced), so
/// every edit is checked against the game's own layout before it is accepted.
/// </summary>
internal sealed partial class MainForm
{
    private const string LooksTitle = "Looks and costumes";

    private int _looksWho; // index in the picker: slot * 2 + (0 player, 1 ArFA-sys)

    private sealed record Choice(int Value, string Name);

    // Verified against two real saves: Equipment[1] is the player, Equipment[2] the ArFA-sys partner.
    // Each entry holds the "entry id" of an inventory item: weapon, weapon, costume, accessory, accessory.
    private static readonly string[] GearNames = { "Weapon 1", "Weapon 2", "Costume", "Accessory 1", "Accessory 2" };

    // The game's own customize lists: 70 hairstyles, 50 faces, 50 eyebrows (SYS_Custom_* texts).
    private static readonly (string label, string part, int max)[] LookParts =
    {
        ("Hairstyle", "[0]", 70), ("Face", "[1]", 50), ("Eyebrows", "[2]", 50),
    };

    private static readonly string[] FacePaints =
        ("None FACEPAINT_001 FACEPAINT_002 FACEPAINT_003 FACEPAINT_004 FACEPAINT_005 FACEPAINT_006 FACEPAINT_007 " +
         "FACEPAINT_009 FACEPAINT_010 FACEPAINT_011 FACEPAINT_012 FACEPAINT_013 FACEPAINT_014 FACEPAINT_015 FACEPAINT_016 " +
         "FACEPAINT_017 FACEPAINT_018 FACEPAINT_019 FACEPAINT_020 FACEPAINT_021 FACEPAINT_022 FACEPAINT_023 FACEPAINT_024 " +
         "FACEPAINT_025 FACEPAINT_026 FACEPAINT_027 FACEPAINT_028 FACEPAINT_029 FACEPAINT_030 FACEPAINT_031 FACEPAINT_032").Split(' ');

    private void ReopenLooks() => BeginInvoke(() =>
    {
        _buildingTree = true;
        try { ShowPages(); _tabs.SelectedTabPageIndex = TabIndexOf(LooksTitle); }
        finally { _buildingTree = false; }
    });

    private Control BuildLooksPage()
    {
        var page = Ui.Panel(DockStyle.Fill);
        if (_tree == null)
        {
            page.Controls.Add(Ui.TextBlock(_treeError == null
                ? "Open this page to load the whole save. It takes a moment."
                : "This save could not be read as the game's full layout, so this page is off. The other pages still work.\n\n" + _treeError,
                Ui.Pad, Ui.Pad, 1100, 120, color: _treeError == null ? Ui.Dim : Ui.Warning));
            return page;
        }

        var save = _save!;
        var tree = _tree;
        string Val(SaoNode? n) => n == null ? "" : SaoTreeEdit.Format(save, n);

        var slots = Enumerable.Range(1, 16).Select(i => tree.Find($"Slot {i}")).TakeWhile(s => s != null).Select(s => s!).ToList();
        if (slots.Count == 0)
        {
            page.Controls.Add(Ui.TextBlock("No save slots were found.", Ui.Pad, Ui.Pad, 600, 30, color: Ui.Warning));
            return page;
        }
        _looksWho = Math.Clamp(_looksWho, 0, slots.Count * 2 - 1);
        var slot = slots[_looksWho / 2];
        string who = _looksWho % 2 == 0 ? "Player" : "ArFA-sys";
        int equipIndex = _looksWho % 2 == 0 ? 1 : 2;
        var ch = slot.Find(who)!;
        var look = ch.Find("look");

        // Writes one value. A same-size value is written in place; anything else rebuilds the save.
        void Apply(SaoNode? n, string text, string what)
        {
            if (n == null || Val(n) == text) return;
            try
            {
                var next = SaoTreeEdit.Set(save, n, text);
                if (next == null)
                {
                    _dirty = true;
                    SetStatus("Changed (not saved yet)");
                    Log($"{slot.Name} {who}: {what}");
                }
                else
                {
                    _save = next;
                    _tree = next.ParseTree();
                    _treeStale = false;
                    _dirty = true;
                    SetStatus("Changed (not saved yet)");
                    Log($"{slot.Name} {who}: {what}");
                    ReopenLooks();
                }
            }
            catch (Exception ex) when (ex is ArgumentException or SaoParseException or InvalidDataException)
            {
                XtraMessageBox.Show(this, $"That value was not changed.\n\n{ex.Message}", AppTitle);
                ReopenLooks();
            }
        }

        // ---- top bar: which character ----
        var bar = Ui.Panel(DockStyle.Top, 120);
        var pick = new ComboBoxEdit { Location = new Point(Ui.Pad, 14), Size = new Size(380, 32) };
        pick.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
        for (int i = 0; i < slots.Count * 2; i++)
        {
            var s = slots[i / 2];
            string w = i % 2 == 0 ? "Player" : "ArFA-sys";
            pick.Properties.Items.Add($"{s.Name} {w}  ({Val(s.Find(w + "/name/source"))})");
        }
        pick.SelectedIndex = _looksWho;
        pick.SelectedIndexChanged += (_, _) => { _looksWho = pick.SelectedIndex; ReopenLooks(); };
        bar.Controls.Add(pick);
        bar.Controls.Add(Ui.TextBlock(
            "Costumes, accessories and weapons list what is in this slot's bag, so add a costume on the Inventory page first and it appears here. " +
            "Costume names end in F (female) or M (male): pick one that matches the gender.\n" +
            "Edits are checked against the game's layout before they are accepted. Save, then test one change in game before changing many.",
            Ui.Pad, 58, 1250, 48, color: Ui.Dim));
        page.Controls.Add(bar);

        // ---- left column: looks ----
        int x1 = Ui.Pad, x2 = 640, y1 = 76, y2 = 76, row = 52;
        var body = Ui.Panel(DockStyle.Fill);
        page.Controls.Add(body);
        page.Controls.SetChildIndex(body, 0); // docks last, so the toolbar never covers it

        body.Controls.Add(Ui.Label("Looks", x1, y1 - 36, Ui.HeadingFont));
        void Caption(Control host, string text, int x, int y) => host.Controls.Add(Ui.Label(text, x, y + 6));

        // name
        Caption(body, "Name", x1, y1 - 12 + 0);
        var node = ch.Find("name/source");
        var name = new TextEdit { Location = new Point(x1 + 140, y1 - 12), Size = new Size(300, 32), Text = Val(node) };
        name.Properties.MaxLength = 16;
        name.Leave += (_, _) =>
        {
            string t = name.Text.Trim();
            if (t.Length == 0) { name.Text = Val(node); return; }
            Apply(node, t, $"name = {t}");
        };
        body.Controls.Add(name);

        // gender
        y1 += row - 12;
        Caption(body, "Gender", x1, y1);
        var gNode = ch.Find("gender");
        var gender = new ComboBoxEdit { Location = new Point(x1 + 140, y1), Size = new Size(300, 32) };
        gender.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
        gender.Properties.Items.AddRange(new[] { "Male (0)", "Female (1)" });
        gender.SelectedIndex = Val(gNode) == "1" ? 1 : 0;
        gender.SelectedIndexChanged += (_, _) => Apply(gNode, gender.SelectedIndex.ToString(), $"gender = {gender.Text}");
        body.Controls.Add(gender);

        // face paint
        y1 += row;
        Caption(body, "Face paint", x1, y1);
        var fpNode = look?.Find("face paint");
        string curPaint = Val(fpNode);
        var paint = new ComboBoxEdit { Location = new Point(x1 + 140, y1), Size = new Size(300, 32) };
        paint.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
        paint.Properties.Items.AddRange(FacePaints);
        if (curPaint.Length > 0 && !FacePaints.Contains(curPaint)) paint.Properties.Items.Add(curPaint);
        paint.EditValue = curPaint.Length == 0 ? "None" : curPaint;
        paint.SelectedIndexChanged += (_, _) => Apply(fpNode, paint.Text, $"face paint = {paint.Text}");
        body.Controls.Add(paint);

        // hair / face / eyebrows
        foreach (var (label, part, max) in LookParts)
        {
            y1 += row;
            Caption(body, label, x1, y1);
            var idNode = look?.Find($"parts/{part}/id");
            var spin = Ui.WholeNumber(0, max);
            spin.Location = new Point(x1 + 140, y1);
            spin.Size = new Size(120, 32);
            if (int.TryParse(Val(idNode), out int cur)) spin.Value = Math.Clamp(cur, 0, max);
            spin.Leave += (_, _) => Apply(idNode, ((int)spin.Value).ToString(), $"{label.ToLowerInvariant()} = {(int)spin.Value}");
            body.Controls.Add(spin);
            body.Controls.Add(Ui.Label($"1 to {max}", x1 + 280, y1 + 6, color: Ui.Dim));
        }
        y1 += row;
        body.Controls.Add(Ui.TextBlock(
            "The game stores the hair, face and eyebrow choice as three numbers. Which number is which is read from a real save " +
            "(hair up to 70, face and eyebrows up to 50). Colours are not edited here: set those in the game's own customizer.",
            x1, y1, 560, 110, color: Ui.Dim));

        // ---- right column: gear ----
        body.Controls.Add(Ui.Label("Costume and gear", x2, y2 - 36, Ui.HeadingFont));
        var inv = slot.Find("Inventory");
        var eq = slot.Find("Equipment")?.Children.ElementAtOrDefault(equipIndex);
        if (inv == null || eq == null)
        {
            body.Controls.Add(Ui.TextBlock("This slot has no equipment list.", x2, y2, 500, 30, color: Ui.Warning));
            return page;
        }

        // inventory items by category: 0 weapon, 2 costume, 3 accessory
        var owned = new Dictionary<int, List<Choice>> { [0] = new(), [2] = new(), [3] = new() };
        var ownedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var it in inv.Children)
        {
            string id = it.Find("item id")?.Text ?? ItemRecord.EmptyId;
            if (id == ItemRecord.EmptyId) continue;
            ownedIds.Add(id);
            if (!int.TryParse(Val(it.Find("category")), out int cat) || !owned.TryGetValue(cat, out var list)) continue;
            if (!int.TryParse(Val(it.Find("entry id")), out int entry) || entry < 0) continue;
            list.Add(new Choice(entry, SaoItems.NameOf(id)));
        }
        foreach (var l in owned.Values) l.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // Items the bag lacks are offered too: picking one adds it to the bag and equips it.
        const int NotOwned = 100000;
        var notOwned = new Dictionary<int, List<SaoItem>>();
        foreach (var kind in new[] { 0, 2, 3 })
        {
            var cat = kind == 0 ? ItemCategory.Weapon : kind == 2 ? ItemCategory.Costume : ItemCategory.Accessory;
            notOwned[kind] = SaoItems.All.Where(i => SaoItems.CategoryOf(i.Id) == cat && !ownedIds.Contains(i.Id))
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        void AddAndEquip(string itemId, int k)
        {
            try
            {
                var c = save.Committed();
                var bags = c.Arrays.Where(a => a.Slots.Count == 500).ToList();
                var bag = bags.FirstOrDefault(b => b.CountOffset == inv.Offset);
                if (bag == null || !inv.Children.Select(n => n.Find("item id")?.Text ?? ItemRecord.EmptyId).SequenceEqual(bag.Slots.Select(s => s.Id)))
                    throw new ArgumentException("This slot has no bag the editor can add to (only a slot that has been played). Use a slot with progress.");
                int free = bag.Slots.FindIndex(s => s.IsEmpty);
                if (free < 0 && k >= 2 && int.TryParse(Val(eq.Find($"slot {k}")), out int worn) && worn >= 0
                    && inv.Children.ElementAtOrDefault(worn)?.Find("item id") is { } idNode)
                {
                    // Bag full: turn the worn costume / accessory into the picked one (its record has no other unique data).
                    string old = SaoItems.NameOf(idNode.Text ?? "");
                    var swapped = SaoTreeEdit.Set(save, idNode, itemId) ?? save;
                    _save = swapped;
                    _tree = swapped.ParseTree();
                    _treeStale = false;
                    _dirty = true;
                    SetStatus("Changed (not saved yet)");
                    Log($"{slot.Name} {who}: bag full, so {old} became {SaoItems.NameOf(itemId)} ({GearNames[k]}).");
                    ReopenLooks();
                    return;
                }
                if (free < 0) throw new ArgumentException($"The bag is full ({bag.Used} of {bag.Slots.Count}). Remove some items on the Inventory page first, then pick this again.");
                if (!SaoEdits.AddItem(c, bag, itemId)) throw new ArgumentException("Could not add the item: there is no item of that kind in the save to copy.");
                var next = c.Committed();
                var t2 = next.ParseTree();
                if (Val2(next, t2.Find($"{slot.Name}/Inventory/[{free}]/entry id")) != free.ToString())
                    throw new ArgumentException("The new item got an unexpected bag position, so nothing was changed.");
                SaoTreeEdit.Set(next, t2.Find($"{slot.Name}/Equipment/[{equipIndex}]/slot {k}")!, free.ToString());
                _save = next;
                _tree = t2;
                _treeStale = false;
                _dirty = true;
                SetStatus("Changed (not saved yet)");
                Log($"{slot.Name} {who}: added {SaoItems.NameOf(itemId)} to the bag and equipped it as {GearNames[k]}.");
            }
            catch (Exception ex) when (ex is ArgumentException or SaoParseException or InvalidDataException)
            {
                XtraMessageBox.Show(this, $"Nothing was changed.\n\n{ex.Message}", AppTitle);
            }
            ReopenLooks();
        }

        for (int k = 0; k < 5; k++)
        {
            int kind = k < 2 ? 0 : k == 2 ? 2 : 3;
            var options = new List<Choice>();
            if (kind != 2) options.Add(new Choice(-1, "(nothing)"));
            options.AddRange(owned[kind]);
            var extra = notOwned[kind];
            for (int i = 0; i < extra.Count; i++)
                options.Add(new Choice(NotOwned + i, $"{extra[i].Name}  [not in bag: will be added]{(extra[i].Dlc ? " [DLC]" : "")}"));
            var slotNode = eq.Find($"slot {k}");
            int current = int.TryParse(Val(slotNode), out int cv) ? cv : -1;
            if (!options.Any(o => o.Value == current)) options.Add(new Choice(current, current < 0 ? "(nothing)" : "(unknown item)"));

            Caption(body, GearNames[k], x2, y2 + k * row - 12);
            var edit = new LookUpEdit { Location = new Point(x2 + 140, y2 + k * row - 12), Size = new Size(520, 32) };
            edit.Properties.DataSource = options;
            edit.Properties.ValueMember = nameof(Choice.Value);
            edit.Properties.DisplayMember = nameof(Choice.Name);
            edit.Properties.Columns.Add(new LookUpColumnInfo(nameof(Choice.Name), "Name", 420));
            edit.Properties.ShowHeader = false;
            edit.Properties.DropDownRows = 16;
            edit.Properties.SearchMode = SearchMode.AutoSearch;
            edit.Properties.PopupFilterMode = PopupFilterMode.Contains;
            edit.EditValue = current;
            string gearName = GearNames[k];
            int slotK = k;
            edit.EditValueChanged += (_, _) =>
            {
                if (edit.EditValue is not int v) return;
                if (v >= NotOwned) BeginInvoke(() => AddAndEquip(extra[v - NotOwned].Id, slotK));
                else Apply(slotNode, v.ToString(), $"{gearName} = {edit.Text}");
            };
            body.Controls.Add(edit);
        }
        y2 += 5 * row;
        body.Controls.Add(Ui.TextBlock(
            $"{owned[2].Count} costumes, {owned[3].Count} accessories and {owned[0].Count} weapons are in this slot's bag. " +
            "Pick an item marked \"not in bag\" and it is added to the bag and equipped. " +
            "Companions such as Kureha keep their own sets and are not listed here.",
            x2, y2, 640, 90, color: Ui.Dim));
        return page;
    }

    private static string Val2(SaoSaveFile s, SaoNode? n) => n == null ? "" : SaoTreeEdit.Format(s, n);
}
