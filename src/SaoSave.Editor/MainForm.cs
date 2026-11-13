using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraTab;
using SaoSave.Core;

namespace SaoSave.Editor;

internal sealed partial class MainForm : XtraForm
{
    private const string AppTitle = "Sword Art Online: Fatal Bullet Save Editor";

    private readonly XtraTabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly LabelControl _status = new() { Location = new Point(Ui.Pad, 10) };
    private readonly MemoEdit _log = new() { Dock = DockStyle.Fill, Properties = { ReadOnly = true } };
    private SaoSaveFile? _save;
    private string? _path;
    private bool _dirty;

    public MainForm(string? path)
    {
        Text = $"{AppTitle} - {Ui.Credit}";
        Size = new Size(1400, 900);
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;

        var header = Ui.Panel(DockStyle.Top, 116);
        header.Controls.Add(Ui.Label(AppTitle, Ui.Pad, 16, Ui.TitleFont));
        header.Controls.Add(Ui.CreditLabel(Ui.Pad + 2, 54));
        int x = 640;
        header.Controls.Add(Ui.Button("Open a file...", x, 150, (_, _) => OpenDialog(), 40)); x += 160;
        header.Controls.Add(Ui.Button("Save", x, 110, (_, _) => SaveFile(null), 40)); x += 118;
        header.Controls.Add(Ui.Button("Save a copy...", x, 130, (_, _) => SaveCopy(), 40)); x += 138;
        header.Controls.Add(Ui.Button("Undo all changes", x, 150, (_, _) => { if (_path != null) { _dirty = false; Open(_path); } }, 40));

        var footer = Ui.Panel(DockStyle.Bottom, 34);
        footer.Controls.Add(_status);
        var credit = Ui.Label($"Fatal Bullet Save Editor  -  {Ui.Credit}", 0, 10, color: Ui.Accent);
        footer.Controls.Add(credit);
        footer.Resize += (_, _) => credit.Left = footer.Width - credit.Width - Ui.Pad;

        Controls.Add(_tabs);
        Controls.Add(footer);
        Controls.Add(header);

        ShowPages();
        _tabs.SelectedPageChanged += (_, _) =>
        {
            string? t = _tabs.SelectedTabPage?.Text;
            if (t == AdvancedTitle || t == LooksTitle || t == AvatarTitle) { _treeReturn = t; EnsureTree(); }
        };
        if (path != null) Shown += (_, _) => Open(path);
        FormClosing += (_, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
    }

    // ---- files ----------------------------------------------------------------------

    private void OpenDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "Save files (*.sav)|*.sav|All files (*.*)|*.*", Title = "Open a decrypted Fatal Bullet save" };
        if (dlg.ShowDialog(this) == DialogResult.OK) Open(dlg.FileName);
    }

    private void Open(string path)
    {
        if (!ConfirmDiscard()) return;
        try
        {
            _save = SaoSaveFile.Load(path);
            _tree = null;
            _treeStale = true;
            _path = path;
            _dirty = false;
            Log($"Opened {path}. Player {_save.PlayerName}. {_save.Arrays.Count} item lists; bag {_save.Inventory?.Used ?? 0} of {_save.Inventory?.Slots.Count ?? 0} slots used.");
            if (!_save.SignatureValid) Log("WARNING: the file's signature does not match. Saving will write a fresh one.");
            int fixedTypes = SaoEdits.FixWeaponTypes(_save);
            if (fixedTypes > 0)
            {
                _dirty = true;
                Log($"Fixed the weapon type of {fixedTypes} guns (they were all filed as handguns, so they could not be equipped). Save to keep it.");
            }
            ShowPages();
            SetStatus(fixedTypes > 0 ? $"Opened {path}. Fixed the type of {fixedTypes} guns: press Save." : $"Opened {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            XtraMessageBox.Show(this, $"This file could not be opened as a Fatal Bullet save.\n\n{ex.Message}\n\nIf it is a PS4 save, decrypt it first.", AppTitle);
        }
    }

    private void SaveCopy()
    {
        if (_save == null) return;
        using var dlg = new SaveFileDialog { Filter = "Save files (*.sav)|*.sav", FileName = Path.GetFileName(_path) };
        if (dlg.ShowDialog(this) == DialogResult.OK) SaveFile(dlg.FileName);
    }

    private void SaveFile(string? path)
    {
        if (_save == null || _path == null) return;
        path ??= _path;
        try
        {
            var bytes = _save.Export();
            string backup = "";
            if (File.Exists(path))
            {
                backup = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                File.Copy(path, backup);
            }
            File.WriteAllBytes(path, bytes);
            _path = path;
            _dirty = false;
            Log(backup.Length > 0 ? $"Saved {path} (signed). Old file kept as {backup}." : $"Saved {path} (signed).");
            SetStatus($"Saved {path}");
            XtraMessageBox.Show(this, "Saved and signed." + (backup.Length > 0 ? $"\n\nYour old save was kept as:\n{backup}" : ""), AppTitle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            XtraMessageBox.Show(this, $"Saving failed. Nothing was changed.\n\n{ex.Message}", AppTitle);
        }
    }

    private bool ConfirmDiscard() =>
        !_dirty || XtraMessageBox.Show(this, "You have changes that are not saved. Throw them away?", AppTitle, MessageBoxButtons.YesNo) == DialogResult.Yes;

    private void MarkDirty() { _dirty = true; _treeStale = true; SetStatus("Changed (not saved yet)"); }
    private void SetStatus(string text) => _status.Text = text;
    private void Log(string text) => _log.Text += $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}";

    // ---- pages ----------------------------------------------------------------------

    private void ShowPages()
    {
        _tabs.TabPages.Clear();
        if (_save == null) AddPage("Welcome", BuildStartPage());
        else
        {
            AddPage("Characters", BuildCharactersPage());
            AddPage(LooksTitle, BuildLooksPage());
            AddPage(AvatarTitle, BuildAvatarPage());
            AddPage("Skills", BuildSkillsPage());
            AddPage("Inventory", BuildInventoryPage());
            AddPage("Progress and options", BuildFieldsPage());
            AddPage(AdvancedTitle, BuildAdvancedPage());
            var logPage = Ui.Panel(DockStyle.Fill);
            logPage.Controls.Add(_log);
            AddPage("Log", logPage);
        }
        AddPage("About", BuildAboutPage());
    }

    private int TabIndexOf(string title) =>
        Enumerable.Range(0, _tabs.TabPages.Count).FirstOrDefault(i => _tabs.TabPages[i].Text == title, -1);

    private void AddPage(string title, Control content)
    {
        var page = _tabs.TabPages.Add(title);
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
    }

    private Control BuildStartPage()
    {
        var page = Ui.Panel(DockStyle.Fill);
        int x = Ui.Pad * 2, y = 36;
        page.Controls.Add(Ui.Label("Welcome", x, y, Ui.TitleFont)); y += 56;
        foreach (var step in new[]
                 {
                     "1.   Export your PS4 save and decrypt it with your save tool. The file is ue4savegame.ps4.sav.",
                     "2.   Click  Open a file...  and pick it.",
                     "3.   Change what you want, then click  Save.  The editor signs the save so the game accepts it.",
                     "4.   Re-encrypt the save with your save tool and import it back to the PS4.",
                 })
        {
            page.Controls.Add(Ui.Label(step, x, y, Ui.BigFont));
            y += 34;
        }
        y += 16;
        page.Controls.Add(Ui.Button("Open a file...", x, 180, (_, _) => OpenDialog(), y));
        y += 70;
        page.Controls.Add(Ui.TextBlock("A backup of your old save is kept next to it every time you click Save.", x, y, 900, 30));
        y += 40;
        page.Controls.Add(Ui.TextBlock("Test on a copy of your save first. Keep the backup the editor makes.", x, y, 900, 30, color: Ui.Warning));
        y += 40;
        page.Controls.Add(Ui.TextBlock("Warning: Fatal Bullet has online co-op and PvP. Edited saves are used at your own risk.", x, y, 900, 30, color: Ui.Warning));
        y += 44;
        page.Controls.Add(Ui.TextBlock($"This editor is free. If someone sold it to you, or sold you an edited save, you were ripped off.  {Ui.Credit}.", x, y, 900, 44, Ui.BigFont, Ui.Accent));
        return page;
    }

    /// <summary>One character (player or ArFA-sys of a save slot) as the grid shows it.</summary>
    public sealed class CharacterRow
    {
        private readonly SaoCharacter _c;
        public CharacterRow(SaoCharacter c) { _c = c; }
        public string Who => _c.Label;
        public string Name => _c.Name;
        public int Experience { get => _c.Experience; set => _c.Experience = value; }
        public int Credits { get => _c.Money; set => _c.Money = value; }
        public int CP { get => _c.CustomizePoints; set => _c.CustomizePoints = value; }
        public int SP { get => _c.SkillPoints; set => _c.SkillPoints = value; }
        public int STR { get => _c.GetStat(0); set => _c.SetStat(0, value); }
        public int VIT { get => _c.GetStat(2); set => _c.SetStat(2, value); }
        public int INT { get => _c.GetStat(4); set => _c.SetStat(4, value); }
        public int AGI { get => _c.GetStat(1); set => _c.SetStat(1, value); }
        public int DEX { get => _c.GetStat(3); set => _c.SetStat(3, value); }
        public int LUC { get => _c.GetStat(5); set => _c.SetStat(5, value); }
    }

    private Control BuildCharactersPage()
    {
        var page = Ui.Panel(DockStyle.Fill);
        var rows = new BindingList<CharacterRow>(_save!.Characters.Select(c => new CharacterRow(c)).ToList());
        var grid = new GridControl { Dock = DockStyle.Fill, BindingContext = new BindingContext() };
        var view = new GridView(grid) { OptionsView = { ShowGroupPanel = false, ColumnAutoWidth = true, ShowIndicator = false } };
        grid.MainView = view;
        view.OptionsBehavior.AutoPopulateColumns = true;
        grid.DataSource = rows;
        view.RowHeight = 32;
        grid.ForceInitialize();
        view.Columns["Who"].OptionsColumn.AllowEdit = false;
        view.Columns["Name"].OptionsColumn.AllowEdit = false;
        view.Columns["Who"].Width = 150;
        view.Columns["Experience"].Caption = "EXP (total)";
        view.CellValueChanged += (_, _) => MarkDirty();

        var note = Ui.Panel(DockStyle.Top, 96);
        note.Controls.Add(Ui.TextBlock(
            "Every save slot has a player and an ArFA-sys partner. Unused slots show the game's defaults (\"Player\", \"Afasys\").\n" +
            "Stats are the points spent: 0 shows as 1 in game, each point adds 1. Level comes from total EXP (the game works it out).\n" +
            "Companions such as Kureha are not stored here: their stats come from their level.",
            Ui.Pad, 12, 1200, 80, color: Ui.Dim));
        page.Controls.Add(grid);
        page.Controls.Add(note);
        return page;
    }

    // ---- skills: learned list (size changes) and equipped skill sets (in place) ----

    private int _skillChar;

    /// <summary>
    /// Rebuilds the pages and returns to Skills. Deferred: the pages are rebuilt after the click
    /// or selection that asked for it has finished, never while its control is being disposed.
    /// </summary>
    private void ReopenSkills() => BeginInvoke(() =>
    {
        ShowPages();
        _tabs.SelectedTabPageIndex = TabIndexOf("Skills");
    });

    public sealed class SkillSetRow
    {
        private readonly SaoCharacter _c;
        private readonly int _layer;
        public SkillSetRow(SaoCharacter c, int layer) { _c = c; _layer = layer; }
        public string Weapon => _layer < SaoCharacter.LayerNames.Length ? SaoCharacter.LayerNames[_layer] : $"Layer {_layer + 1}";
        private int Get(int i) { var s = _c.GetSkillSet(_layer); return i < s.Length ? s[i] : -1; }
        public int Slot1 { get => Get(0); set => _c.SetSkillSetSlot(_layer, 0, value); }
        public int Slot2 { get => Get(1); set => _c.SetSkillSetSlot(_layer, 1, value); }
        public int Slot3 { get => Get(2); set => _c.SetSkillSetSlot(_layer, 2, value); }
        public int Slot4 { get => Get(3); set => _c.SetSkillSetSlot(_layer, 3, value); }
    }

    private Control BuildSkillsPage()
    {
        var page = Ui.Panel(DockStyle.Fill);
        var chars = _save!.Characters;
        if (chars.Count == 0)
        {
            page.Controls.Add(Ui.TextBlock("No characters were found in this save.", Ui.Pad, Ui.Pad, 900, 30, color: Ui.Warning));
            return page;
        }
        _skillChar = Math.Clamp(_skillChar, 0, chars.Count - 1);
        var ch = chars[_skillChar];

        var bar = Ui.Panel(DockStyle.Top, 104);
        var who = new ComboBoxEdit { Location = new Point(Ui.Pad, 14), Size = new Size(260, 32) };
        who.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
        foreach (var c in chars) who.Properties.Items.Add($"{c.Label}  ({c.Name})");
        who.SelectedIndex = _skillChar;
        who.SelectedIndexChanged += (_, _) => { _skillChar = who.SelectedIndex; ReopenSkills(); };
        bar.Controls.Add(who);

        bar.Controls.Add(Ui.Label("Equipped skills per weapon: click a cell to pick a skill (gadgets are in the list too).", Ui.Pad, 66, color: Ui.Dim));
        bar.Controls.Add(Ui.Label($"Learned skills are stored as {ch.SkillBytes.Count} raw bytes. Their meaning is not confirmed yet, so learning skills is not offered (see Advanced).", 300, 22, color: Ui.Warning));

        // skill sets
        var sets = new GridControl { Dock = DockStyle.Fill, BindingContext = new BindingContext() };
        var sv = new GridView(sets) { OptionsView = { ShowGroupPanel = false, ColumnAutoWidth = true, ShowIndicator = false } };
        sets.MainView = sv;
        sv.OptionsBehavior.AutoPopulateColumns = true;
        sets.DataSource = new BindingList<SkillSetRow>(Enumerable.Range(0, Math.Min(ch.SkillSetLayers, 16)).Select(l => new SkillSetRow(ch, l)).ToList());
        sv.RowHeight = 32;
        sets.ForceInitialize();
        var picker = new RepositoryItemLookUpEdit { ValueMember = "Id", DisplayMember = "Name", ShowHeader = false, NullText = "(empty)", DropDownRows = 18, SearchMode = SearchMode.AutoSearch, PopupFilterMode = PopupFilterMode.Contains };
        picker.Columns.Add(new LookUpColumnInfo("Name", "Name", 260));
        picker.DataSource = new[] { new SaoSkill(-1, "(empty)", 0, "") }.Concat(SaoSkills.All).ToList();
        sets.RepositoryItems.Add(picker);
        sv.Columns["Weapon"].OptionsColumn.AllowEdit = false;
        foreach (var col in new[] { "Slot1", "Slot2", "Slot3", "Slot4" })
        {
            sv.Columns[col].ColumnEdit = picker;
            sv.Columns[col].Caption = "Skill " + col[^1];
        }
        sv.CellValueChanged += (_, _) => MarkDirty();

        page.Controls.Add(sets);
        page.Controls.Add(bar);
        return page;
    }

    // ---- everything else with a fixed size: options, play record, ArFA-sys, affinity ----

    public sealed class FieldRow
    {
        private readonly SaoSaveFile _s;
        public FieldRow(SaoSaveFile s, SaoField f) { _s = s; Field = f; }
        public SaoField Field { get; }
        public string Group => Field.Group;
        public string Setting => Field.Name;
        public double Value { get => Math.Round(_s.GetField(Field), 4); set => _s.SetField(Field, value); }
        public string Note => Field.Kind switch
        {
            FieldKind.Bool32 => "1 = on, 0 = off",
            FieldKind.Byte => Field.Note.Length > 0 ? Field.Note : "0 to 255",
            _ => Field.Note,
        };
    }

    /// <summary>Medal count the Max button sets: high, but far from the int32 limit so spending and adding stay safe.</summary>
    private const int MaxMedals = 99_999;

    private Control BuildFieldsPage()
    {
        var page = Ui.Panel(DockStyle.Fill);
        var grid = new GridControl { Dock = DockStyle.Fill, BindingContext = new BindingContext() };
        var view = new GridView(grid) { OptionsView = { ShowGroupPanel = false, ColumnAutoWidth = true, ShowIndicator = false } };
        grid.MainView = view;
        view.OptionsBehavior.AutoPopulateColumns = true;
        grid.DataSource = new BindingList<FieldRow>(_save!.Fields.Select(f => new FieldRow(_save, f)).ToList());
        view.OptionsFind.AlwaysVisible = true;
        view.OptionsFind.FindNullPrompt = "Type here to search...";
        view.RowHeight = 30;
        grid.ForceInitialize();
        view.Columns["Field"].Visible = false;
        foreach (var name in new[] { "Group", "Setting", "Note" }) view.Columns[name].OptionsColumn.AllowEdit = false;
        view.Columns["Group"].GroupIndex = 0;
        view.ExpandAllGroups();
        view.CellValueChanged += (_, _) => MarkDirty();

        var note = Ui.Panel(DockStyle.Top, 128);
        note.Controls.Add(Ui.Button("Max medals (every slot)", Ui.Pad, 220, (_, _) =>
        {
            var medals = _save.Fields.Where(f => f.Note == "medal count").ToList();
            foreach (var m in medals) _save.SetField(m, MaxMedals);
            MarkDirty();
            grid.RefreshDataSource();
            Log($"Set {medals.Count} medal counts to {MaxMedals:N0}.");
        }, 12));
        note.Controls.Add(Ui.TextBlock(
            "Game options, play record (play time in seconds, reward points, medals), ArFA-sys pocket money and AI, and companion affinity per save slot.\n" +
            "Values are read from the places the game's own loader reads them. Slots you have not started show defaults.",
            Ui.Pad, 64, 1200, 60, color: Ui.Dim));
        page.Controls.Add(grid);
        page.Controls.Add(note);
        return page;
    }

    /// <summary>One bag slot as the grid shows it.</summary>
    public sealed class SlotRow
    {
        private readonly ItemRecord _r;
        public SlotRow(int slot, ItemRecord r) { Slot = slot; _r = r; }
        public ItemRecord Record => _r;
        public int Slot { get; }
        public string Item => SaoItems.NameOf(_r.Id);
        public string Id => _r.Id;
        public string Kind => _r.IsEmpty ? "" : _r.Category.ToString();
        public string Dlc => SaoItems.Find(_r.Id)?.Dlc == true ? "DLC" : "";
        public int Quantity { get => _r.Quantity; set => _r.Quantity = Math.Max(0, value); }
        public int Level { get => _r.Category == ItemCategory.Weapon ? _r.Level : 0; set { if (_r.Category == ItemCategory.Weapon) _r.Level = Math.Clamp(value, 1, 999); } }
        public string Chips => _r.Category == ItemCategory.Weapon ? string.Join(", ", _r.Chips.Select(c => $"{SaoItems.ChipName(c.Id)} {c.Effect * 100:0.##}%")) : "";
    }

    private Control BuildInventoryPage()
    {
        var page = Ui.Panel(DockStyle.Fill);
        var bag = _save!.Inventory;
        if (bag == null)
        {
            page.Controls.Add(Ui.TextBlock("No inventory was found in this save.", Ui.Pad, Ui.Pad, 900, 30, color: Ui.Warning));
            return page;
        }

        var rows = new BindingList<SlotRow>();
        void Reload()
        {
            rows.RaiseListChangedEvents = false;
            rows.Clear();
            for (int i = 0; i < bag.Slots.Count; i++) if (!bag.Slots[i].IsEmpty) rows.Add(new SlotRow(i, bag.Slots[i]));
            rows.RaiseListChangedEvents = true;
            rows.ResetBindings();
        }
        Reload();

        var grid = new GridControl { Dock = DockStyle.Fill, BindingContext = new BindingContext() };
        var view = new GridView(grid) { OptionsView = { ShowGroupPanel = false, ColumnAutoWidth = true, ShowIndicator = false } };
        grid.MainView = view;
        view.OptionsBehavior.AutoPopulateColumns = true;
        grid.DataSource = rows;
        view.OptionsFind.AlwaysVisible = true;
        view.OptionsFind.FindNullPrompt = "Type here to search...";
        view.OptionsSelection.MultiSelect = true;
        view.RowHeight = 30;
        grid.ForceInitialize();
        foreach (var name in new[] { "Slot", "Item", "Id", "Kind", "Dlc", "Chips" })
            view.Columns[name].OptionsColumn.AllowEdit = false;
        view.Columns["Record"].Visible = false;
        view.Columns["Dlc"].Caption = "DLC";
        view.Columns["Id"].Caption = "Game ID";
        view.Columns["Slot"].Width = 50;
        view.Columns["Item"].Width = 300;
        view.Columns["Chips"].Width = 360;
        view.CellValueChanged += (_, _) => MarkDirty();

        // ---- add bar: pick an item (search, kind and DLC filters), then add it ----
        var bar = Ui.Panel(DockStyle.Top, 104);
        var kindFilter = new ComboBoxEdit { Location = new Point(Ui.Pad, 14), Size = new Size(150, 32) };
        kindFilter.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
        kindFilter.Properties.Items.AddRange(new object[] { "All kinds", "Weapon", "Costume", "Accessory", "Bullet", "Material" });
        kindFilter.SelectedIndex = 0;
        var dlcOnly = new CheckEdit { Text = "DLC only", Location = new Point(Ui.Pad + 160, 18), Width = 100 };
        var pick = new LookUpEdit { Location = new Point(Ui.Pad + 270, 14), Size = new Size(420, 32) };
        pick.Properties.ValueMember = "Id";
        pick.Properties.DisplayMember = "Name";
        pick.Properties.Columns.Add(new LookUpColumnInfo("Name", "Name", 270));
        pick.Properties.Columns.Add(new LookUpColumnInfo("Kind", "Kind", 170));
        pick.Properties.PopupWidth = 520;
        pick.Properties.ShowHeader = false;
        pick.Properties.SearchMode = SearchMode.AutoSearch;
        pick.Properties.PopupFilterMode = PopupFilterMode.Contains;
        pick.Properties.DropDownRows = 18;
        pick.Properties.NullText = "Pick an item to add (type to search)";
        List<SaoItem> Wanted() => SaoItems.All
            .Where(i => SaoItems.CategoryOf(i.Id) != null)
            .Where(i => kindFilter.SelectedIndex <= 0 || i.Kind == kindFilter.Text)
            .Where(i => !dlcOnly.Checked || i.Dlc)
            .Select(i => i.Dlc ? i with { Kind = i.Kind + " · DLC" } : i)
            .ToList();
        void Refilter() { pick.Properties.DataSource = Wanted(); pick.EditValue = null; }
        kindFilter.SelectedIndexChanged += (_, _) => Refilter();
        dlcOnly.CheckedChanged += (_, _) => Refilter();
        Refilter();

        void BagFull() => XtraMessageBox.Show(this, $"The bag is full ({bag.Slots.Count} of {bag.Slots.Count} slots). The game has no more room.\n\nSelect items you do not need (for example extra weapons), click Remove selected, then add again.", AppTitle);

        void Added(int n, string what)
        {
            if (n > 0) MarkDirty();
            Reload();
            Log(what);
        }
        bar.Controls.Add(kindFilter);
        bar.Controls.Add(dlcOnly);
        bar.Controls.Add(pick);
        bar.Controls.Add(Ui.Button("Add item", Ui.Pad + 700, 120, (_, _) =>
        {
            if (pick.EditValue is not string id) return;
            if (!bag.Slots.Any(s => s.IsEmpty)) { BagFull(); return; }
            bool ok = SaoEdits.AddItem(_save, bag, id);
            Added(ok ? 1 : 0, ok ? $"Added {SaoItems.NameOf(id)}." : "Could not add: the bag is full, or there is no item of that kind in the save to copy.");
        }, 14));
        bar.Controls.Add(Ui.Button("Add all shown I don't have", Ui.Pad + 830, 230, (_, _) =>
        {
            if (!bag.Slots.Any(s => s.IsEmpty)) { BagFull(); return; }
            var missing = SaoEdits.Missing(bag, Wanted());
            if (missing.Count == 0) { Log("You already have all of those."); return; }
            if (XtraMessageBox.Show(this, $"Add {missing.Count} items to the bag ({bag.Slots.Count(s => s.IsEmpty)} free slots)?", AppTitle, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            int added = missing.TakeWhile(i => SaoEdits.AddItem(_save, bag, i.Id)).Count();
            Added(added, $"Added {added} of {missing.Count} items.");
        }, 14));
        bar.Controls.Add(Ui.Button("Remove selected", Ui.Pad + 1070, 150, (_, _) =>
        {
            var chosen = view.GetSelectedRows().Select(h => view.GetRow(h)).OfType<SlotRow>().ToList();
            if (chosen.Count == 0) return;
            if (XtraMessageBox.Show(this, $"Remove {chosen.Count} item(s) from the bag?", AppTitle, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (var r in chosen) bag.Slots[r.Slot] = EmptyLike(r.Record, r.Slot);
            Added(chosen.Count, $"Removed {chosen.Count} item(s).");
        }, 14));
        bar.Controls.Add(Ui.Label("Quantity and weapon level can be edited in the list. Chips: select a weapon and use the chip editor below.", Ui.Pad, 66, color: Ui.Dim));

        // ---- chip editor for the selected weapon ----
        var chipPanel = Ui.Panel(DockStyle.Bottom, 64);
        var chipLabel = Ui.Label("Select a weapon to edit its chips.", Ui.Pad, 22, color: Ui.Dim);
        chipPanel.Controls.Add(chipLabel);
        view.FocusedRowChanged += (_, _) => ShowChips();
        void ShowChips()
        {
            foreach (Control c in chipPanel.Controls.Cast<Control>().Where(c => c != chipLabel).ToList()) { chipPanel.Controls.Remove(c); c.Dispose(); }
            if (view.GetFocusedRow() is not SlotRow { Record: { Category: ItemCategory.Weapon } rec } row)
            {
                chipLabel.Text = "Select a weapon to edit its chips.";
                return;
            }
            chipLabel.Text = rec.Chips.Count == 0 ? $"{row.Item}: no chips yet." : $"{row.Item}:";
            int cx = Ui.Pad + 260;
            var chipList = SaoItems.Chips.ToList();
            for (int i = 0; i < rec.Chips.Count; i++)
            {
                int n = i;
                var id = new ComboBoxEdit { Location = new Point(cx, 16), Size = new Size(120, 30) };
                id.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
                foreach (var (_, name) in chipList) id.Properties.Items.Add(name);
                id.SelectedIndex = chipList.FindIndex(c => c.id == rec.Chips[n].Id);
                var pct = new SpinEdit();
                pct.Properties.IsFloatValue = true;
                pct.Properties.Mask.EditMask = "n2";
                pct.Properties.Increment = 5;
                pct.Properties.Mask.UseMaskAsDisplayFormat = true;
                pct.Properties.MinValue = 0;
                pct.Properties.MaxValue = 999;
                pct.Bounds = new Rectangle(cx + 124, 16, 78, 30);
                pct.Value = (decimal)Math.Round(rec.Chips[n].Effect * 100, 2);
                id.SelectedIndexChanged += (_, _) =>
                {
                    if (id.SelectedIndex >= 0) { rec.Chips[n] = rec.Chips[n] with { Id = chipList[id.SelectedIndex].id }; MarkDirty(); grid.RefreshDataSource(); }
                };
                pct.EditValueChanged += (_, _) => { rec.Chips[n] = rec.Chips[n] with { Effect = (float)pct.Value / 100f }; MarkDirty(); grid.RefreshDataSource(); };
                chipPanel.Controls.Add(id);
                chipPanel.Controls.Add(pct);
                chipPanel.Controls.Add(Ui.Label("%", cx + 206, 22));
                var drop = Ui.Button("X", cx + 222, 28, (_, _) => { rec.Chips.RemoveAt(n); MarkDirty(); grid.RefreshDataSource(); BeginInvoke(ShowChips); }, 16);
                drop.Height = 30;
                chipPanel.Controls.Add(drop);
                cx += 260;
            }
            if (rec.Chips.Count < MaxChips)
            {
                var add = Ui.Button("Add a chip", cx, 120, (_, _) =>
                {
                    byte first = SaoItems.Chips.FirstOrDefault(c => c.id > 0).id;
                    rec.Chips.Add(new Chip(first, 0.01f));
                    MarkDirty();
                    grid.RefreshDataSource();
                    BeginInvoke(ShowChips); // after the click, so the clicked button is not disposed under itself
                }, 14);
                add.Height = 34;
                chipPanel.Controls.Add(add);
            }
        }

        page.Controls.Add(grid);
        page.Controls.Add(chipPanel);
        page.Controls.Add(bar);
        return page;
    }

    /// <summary>The most chips seen on one weapon in a real save.</summary>
    private const int MaxChips = 4;

    private sealed record ChipChoice(byte Id, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>An empty slot, copied from one the game wrote ("None", category 7, slot index -1).</summary>
    private ItemRecord EmptyLike(ItemRecord used, int slot) =>
        _save!.Arrays.SelectMany(a => a.Slots).FirstOrDefault(s => s.IsEmpty)?.Clone()
        ?? throw new InvalidOperationException("No empty slot to copy.");

    private Control BuildAboutPage()
    {
        var page = Ui.Panel(DockStyle.Fill);
        int x = Ui.Pad * 2, y = 32;
        page.Controls.Add(Ui.Label(AppTitle, x, y, Ui.TitleFont)); y += 44;
        page.Controls.Add(Ui.Label(Ui.Credit, x, y, Ui.TitleFont, Ui.Accent)); y += 56;
        page.Controls.Add(Ui.TextBlock(
            "A free save editor for decrypted PS4 saves of Sword Art Online: Fatal Bullet.\n" +
            "It is free and always will be. If you paid for this editor or for an edited save, you were ripped off.",
            x, y, 900, 50, Ui.BigFont)); y += 70;
        page.Controls.Add(Ui.Label("Credits", x, y, Ui.HeadingFont)); y += 36;
        foreach (var (who, what, link) in new[]
                 {
                     (Ui.Author, "created the editor and mapped the save format", ""),
                     ("alfizari", "save signature key, item and chip name lists (Apache-2.0)", "https://github.com/alfizari/Sword-Art-Online-FB-Inventory-Editor"),
                     ("DevExpress WinForms", "the buttons, lists and tables", "https://www.devexpress.com/products/net/controls/winforms/"),
                     (".NET 8", "what the editor runs on", "https://dotnet.microsoft.com/"),
                     ("Sword Art Online: Fatal Bullet", "the game, by Dimps and Bandai Namco", ""),
                 })
        {
            page.Controls.Add(Ui.Label(who, x, y, new Font(Ui.BigFont, FontStyle.Bold), who == Ui.Author ? Ui.Accent : null));
            page.Controls.Add(Ui.Label(what, x + 330, y + 2));
            if (link.Length > 0)
            {
                var url = new HyperlinkLabelControl { Text = link, Location = new Point(x + 760, y + 2) };
                url.HyperlinkClick += (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { Log($"Could not open {link}: {ex.Message}"); }
                };
                page.Controls.Add(url);
            }
            y += 34;
        }
        y += 22;
        page.Controls.Add(Ui.TextBlock("This is a fan-made tool. It is not made, approved or supported by Bandai Namco or Dimps.", x, y, 900, 44));
        return page;
    }
}
