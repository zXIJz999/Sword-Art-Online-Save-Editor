using System.ComponentModel;
using System.Drawing;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using SaoSave.Core;

namespace SaoSave.Editor;

/// <summary>
/// Avatar page: every part of a character's look in one place, plus copying a whole avatar from
/// another character, a saved preset, another save file or an avatar file. Built on the full-layout
/// tree, so every change is checked against the game's own layout.
/// </summary>
internal sealed partial class MainForm
{
    private const string AvatarTitle = "Avatar";

    private int _avatarWho;                  // index in AvatarCharacters
    private SaoSaveFile? _extSave;           // another save file loaded for copying from
    private SaoNode? _extTree;
    private string _extName = "";

    private void ReopenTab(string title) => BeginInvoke(() =>
    {
        _buildingTree = true;
        try { ShowPages(); _tabs.SelectedTabPageIndex = TabIndexOf(title); }
        finally { _buildingTree = false; }
    });

    private Control BuildAvatarPage()
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
        var people = SaoAvatar.Characters(tree, save);
        if (people.Count == 0)
        {
            page.Controls.Add(Ui.TextBlock("No characters were found.", Ui.Pad, Ui.Pad, 600, 30, color: Ui.Warning));
            return page;
        }
        _avatarWho = Math.Clamp(_avatarWho, 0, people.Count - 1);
        var (slotNo, arfa, _) = people[_avatarWho];
        string who = arfa ? "ArFA-sys" : "Player";
        var ch = tree.Find($"Slot {slotNo}/{who}")!;
        var look = ch.Find("look");
        string label = people[_avatarWho].label;

        void Changed()
        {
            _dirty = true;
            SetStatus("Changed (not saved yet)");
        }

        // Replaces the save after a change that moved bytes, then shows this page again.
        void Swap(SaoSaveFile next, string what)
        {
            _save = next;
            _tree = next.ParseTree();
            _treeStale = false;
            Changed();
            Log($"Avatar: {what}");
            ReopenTab(AvatarTitle);
        }

        void Fail(Exception ex)
        {
            XtraMessageBox.Show(this, $"Nothing was changed.\n\n{ex.Message}", AppTitle);
            ReopenTab(AvatarTitle);
        }

        void Set(SaoNode? n, string text, string what)
        {
            if (n == null || Val(n) == text) return;
            try
            {
                var next = SaoTreeEdit.Set(save, n, text);
                if (next == null) { Changed(); Log($"Avatar {label}: {what}"); }
                else Swap(next, $"{label}: {what}");
            }
            catch (Exception ex) when (ex is ArgumentException or SaoParseException or InvalidDataException) { Fail(ex); }
        }

        void Paste(AvatarData a, string from, bool gender)
        {
            try
            {
                if (XtraMessageBox.Show(this, $"Replace the whole avatar of {label} with {from}?", AppTitle, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                var next = SaoAvatar.Apply(save, tree, slotNo, arfa, a, gender);
                Swap(next, $"{label} now uses {from}.");
            }
            catch (Exception ex) when (ex is ArgumentException or SaoParseException or InvalidDataException) { Fail(ex); }
        }

        // ---- top bar ----
        var bar = Ui.Panel(DockStyle.Top, 112);
        var pick = new ComboBoxEdit { Location = new Point(Ui.Pad, 14), Size = new Size(380, 32) };
        pick.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
        foreach (var p in people) pick.Properties.Items.Add(p.label);
        pick.SelectedIndex = _avatarWho;
        pick.SelectedIndexChanged += (_, _) => { _avatarWho = pick.SelectedIndex; ReopenTab(AvatarTitle); };
        bar.Controls.Add(pick);
        bar.Controls.Add(Ui.TextBlock(
            "Replace the whole avatar of the character picked above with one from somewhere else. " +
            "A whole-avatar copy changes the gender too unless you untick it: costumes end in F or M, so check the Looks and costumes page afterwards.\n" +
            "Everything is checked against the game's layout before it is accepted. Save, then test in game.",
            Ui.Pad, 56, 1300, 52, color: Ui.Dim));

        var body = Ui.Panel(DockStyle.Fill);
        body.AutoScroll = true;
        page.Controls.Add(bar);
        page.Controls.Add(body);
        page.Controls.SetChildIndex(body, 0); // docks last, so the bar never covers it

        // ---- left: edit ----
        int x2 = Ui.Pad;
        // ---- right: replace the whole avatar ----
        int yr = 16;
        body.Controls.Add(Ui.Label($"Replace the whole avatar of {label}", x2, yr, Ui.HeadingFont)); yr += 44;
        var copyGender = new CheckEdit { Text = "Also copy the gender", Location = new Point(x2, yr), Width = 300, Checked = true };
        body.Controls.Add(copyGender); yr += 44;

        // 1. another character in this save
        body.Controls.Add(Ui.Label("From another character in this save", x2, yr, color: Ui.Dim)); yr += 26;
        var from = new ComboBoxEdit { Location = new Point(x2, yr), Size = new Size(360, 32) };
        from.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
        foreach (var p in people) from.Properties.Items.Add(p.label);
        from.SelectedIndex = Math.Min(_avatarWho == 0 ? 1 : 0, people.Count - 1);
        body.Controls.Add(from);
        body.Controls.Add(Ui.Button("Copy it here", x2 + 372, 170, (_, _) =>
        {
            var src = people[from.SelectedIndex];
            if (SaoAvatar.Read(save, tree, src.slot, src.arfa) is { } a) Paste(a, src.label, copyGender.Checked);
            else XtraMessageBox.Show(this, "That character's avatar could not be read.", AppTitle);
        }, yr - 2)); yr += 56;

        // 2. a saved avatar preset of this slot
        body.Controls.Add(Ui.Label("From a saved avatar of this save slot (the game's own avatar presets)", x2, yr, color: Ui.Dim)); yr += 26;
        var presets = SaoAvatar.Presets(save, tree, slotNo);
        var preset = new ComboBoxEdit { Location = new Point(x2, yr), Size = new Size(360, 32) };
        preset.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
        foreach (var p in presets) preset.Properties.Items.Add(p.label);
        if (presets.Count > 0) preset.SelectedIndex = 0; else preset.Properties.NullText = "(none saved in this slot)";
        body.Controls.Add(preset);
        body.Controls.Add(Ui.Button("Use it here", x2 + 372, 170, (_, _) =>
        {
            if (preset.SelectedIndex < 0) { XtraMessageBox.Show(this, "This slot has no saved avatars.", AppTitle); return; }
            Paste(presets[preset.SelectedIndex].data, presets[preset.SelectedIndex].label, false);
        }, yr - 2)); yr += 56;

        // 3. another save file
        body.Controls.Add(Ui.Label("From another save file (a backup, or a friend's save)", x2, yr, color: Ui.Dim)); yr += 26;
        var extPeople = _extSave != null && _extTree != null ? SaoAvatar.Characters(_extTree, _extSave) : new();
        var ext = new ComboBoxEdit { Location = new Point(x2, yr), Size = new Size(360, 32) };
        ext.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
        foreach (var p in extPeople) ext.Properties.Items.Add(p.label);
        if (extPeople.Count > 0) ext.SelectedIndex = 0; else ext.Properties.NullText = "(no file loaded)";
        body.Controls.Add(ext);
        body.Controls.Add(Ui.Button("Copy it here", x2 + 372, 170, (_, _) =>
        {
            if (ext.SelectedIndex < 0 || _extSave == null || _extTree == null) { XtraMessageBox.Show(this, "Load a save file first.", AppTitle); return; }
            var src = extPeople[ext.SelectedIndex];
            if (SaoAvatar.Read(_extSave, _extTree, src.slot, src.arfa) is { } a) Paste(a, $"{src.label} from {_extName}", copyGender.Checked);
            else XtraMessageBox.Show(this, "That character's avatar could not be read.", AppTitle);
        }, yr - 2)); yr += 44;
        body.Controls.Add(Ui.Button("Load another save file...", x2, 230, (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Save files (*.sav)|*.sav|All files (*.*)|*.*", Title = "Pick a decrypted Fatal Bullet save to copy an avatar from" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var other = SaoSaveFile.Load(dlg.FileName).Committed();
                _extTree = other.ParseTree();
                _extSave = other;
                _extName = Path.GetFileName(dlg.FileName);
                Log($"Avatar: loaded {dlg.FileName} to copy avatars from.");
                ReopenTab(AvatarTitle);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or SaoParseException)
            {
                XtraMessageBox.Show(this, $"That file could not be read as a Fatal Bullet save.\n\n{ex.Message}", AppTitle);
            }
        }, yr)); yr += 64;

        // 4. avatar files
        body.Controls.Add(Ui.Label("Avatar files (share one avatar on its own)", x2, yr, color: Ui.Dim)); yr += 30;
        body.Controls.Add(Ui.Button("Save this avatar to a file...", x2, 230, (_, _) =>
        {
            if (SaoAvatar.Read(save, tree, slotNo, arfa) is not { } a) return;
            using var dlg = new SaveFileDialog { Filter = "Avatar files (*.saoavatar)|*.saoavatar", FileName = $"{Val(ch.Find("name/source"))}.saoavatar" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllBytes(dlg.FileName, SaoAvatar.ToFile(a));
            Log($"Avatar: saved {label} to {dlg.FileName}.");
            SetStatus($"Avatar saved to {dlg.FileName}");
        }, yr));
        body.Controls.Add(Ui.Button("Use an avatar file here...", x2 + 242, 230, (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Avatar files (*.saoavatar)|*.saoavatar|All files (*.*)|*.*" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { Paste(SaoAvatar.FromFile(File.ReadAllBytes(dlg.FileName)), Path.GetFileName(dlg.FileName), copyGender.Checked); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { XtraMessageBox.Show(this, ex.Message, AppTitle); }
        }, yr));
        return page;
    }
}
