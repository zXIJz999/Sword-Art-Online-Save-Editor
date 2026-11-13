using System.Drawing;
using DevExpress.XtraEditors;
using DevExpress.XtraTreeList;
using DevExpress.XtraTreeList.Columns;
using DevExpress.XtraTreeList.Nodes;
using SaoSave.Core;

namespace SaoSave.Editor;

/// <summary>
/// Advanced page: every value in the save as the game's own loader reads it, editable. Built from
/// <see cref="SaoTree"/>. Pending edits from the other pages are written first (Committed), so the
/// tree always matches the bytes.
/// </summary>
internal sealed partial class MainForm
{
    private const string AdvancedTitle = "Advanced (everything)";

    private SaoNode? _tree;
    private bool _treeStale = true;
    private bool _buildingTree;
    private string? _treeError;
    private string? _focusPath;
    private string _treeReturn = AdvancedTitle; // the tree page that asked for the parse
    private bool _showUnknown;

    /// <summary>Commits pending edits and parses the full layout, then shows it.</summary>
    private void EnsureTree()
    {
        if (_save == null || _buildingTree || (!_treeStale && _tree != null) || _treeError != null && !_treeStale) return;
        _buildingTree = true;
        try
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                _save = _save.Committed();
                _tree = _save.ParseTree();
                _treeError = null;
            }
            catch (Exception ex) when (ex is SaoParseException or InvalidDataException)
            {
                _tree = null;
                _treeError = ex.Message;
                Log("Advanced: the full layout could not be read: " + ex.Message);
            }
            _treeStale = false;
        }
        finally { Cursor = Cursors.Default; }

        // Rebuild after the tab change that asked for it has finished.
        BeginInvoke(() =>
        {
            try { ShowPages(); _tabs.SelectedTabPageIndex = TabIndexOf(_treeReturn); }
            finally { _buildingTree = false; }
        });
    }

    /// <summary>Replaces the save after an edit that rebuilt it, then reopens the Advanced page.</summary>
    private void SwapSave(SaoSaveFile next, string? focusPath, string what)
    {
        _save = next;
        _tree = next.ParseTree();
        _treeStale = false;
        _dirty = true;
        SetStatus("Changed (not saved yet)");
        Log(what);
        _focusPath = focusPath;
        BeginInvoke(() =>
        {
            _buildingTree = true;
            try { ShowPages(); _tabs.SelectedTabPageIndex = TabIndexOf(_treeReturn); }
            finally { _buildingTree = false; }
        });
    }

    private Control BuildAdvancedPage()
    {
        var page = Ui.Panel(DockStyle.Fill);
        if (_tree == null)
        {
            page.Controls.Add(Ui.TextBlock(_treeError == null
                ? "Open this page to load the whole save. It takes a moment."
                : "This save could not be read as the game's full layout, so the Advanced page is off. The other pages still work.\n\n" + _treeError,
                Ui.Pad, Ui.Pad, 1100, 120, color: _treeError == null ? Ui.Dim : Ui.Warning));
            return page;
        }

        var save = _save!;
        var tree = new TreeList { Dock = DockStyle.Fill };
        tree.OptionsBehavior.Editable = true;
        tree.OptionsView.ShowIndicator = false;
        tree.OptionsFind.AlwaysVisible = false;
        tree.RowHeight = 26;
        var cName = tree.Columns.Add(); cName.Caption = "What"; cName.FieldName = "Name"; cName.Visible = true; cName.Width = 430; cName.OptionsColumn.AllowEdit = false;
        var cValue = tree.Columns.Add(); cValue.Caption = "Value (click to change)"; cValue.FieldName = "Value"; cValue.Visible = true; cValue.Width = 300;
        var cInfo = tree.Columns.Add(); cInfo.Caption = "What it does"; cInfo.FieldName = "Info"; cInfo.Visible = true; cInfo.Width = 600; cInfo.OptionsColumn.AllowEdit = false;

        tree.VirtualTreeGetChildNodes += (_, e) =>
            e.Children = e.Node is SaoNode n ? n.Children.Where(c => _showUnknown || !AdvancedHelp.IsUnknown(c)).ToList() : new List<SaoNode>();
        tree.VirtualTreeGetCellValue += (_, e) =>
        {
            if (e.Node is not SaoNode n) return;
            e.CellData = e.Column == cName ? DisplayName(n)
                : e.Column == cValue ? (n.IsValue || n.Kind is NodeKind.Str ? SaoTreeEdit.Format(save, n) : n.Kind == NodeKind.List ? $"{n.Count} items" : "")
                : AdvancedHelp.Describe(n);
        };
        tree.ShowingEditor += (_, e) =>
        {
            if (tree.FocusedColumn != cValue || tree.GetDataRecordByNode(tree.FocusedNode) is not SaoNode n || !SaoTreeEdit.Editable(n)) e.Cancel = true;
        };
        tree.VirtualTreeSetCellValue += (_, e) =>
        {
            if (e.Node is not SaoNode n || e.Column != cValue) return;
            string text = e.NewCellData?.ToString() ?? "";
            if (text == SaoTreeEdit.Format(save, n)) return;
            if (AdvancedHelp.Risky(n) && XtraMessageBox.Show(this, $"\"{DisplayName(n)}\" is easy to break.\n\nChange it anyway?", AppTitle, MessageBoxButtons.YesNo) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            try
            {
                var next = SaoTreeEdit.Set(save, n, text);
                if (next == null)
                {
                    _dirty = true;
                    SetStatus("Changed (not saved yet)");
                    Log($"{n.Path} = {text}");
                }
                else SwapSave(next, n.Path, $"{n.Path} = {text}");
            }
            catch (Exception ex) when (ex is ArgumentException or SaoParseException or InvalidDataException)
            {
                e.Cancel = true;
                XtraMessageBox.Show(this, $"That value was not changed.\n\n{ex.Message}", AppTitle);
            }
        };
        tree.DataSource = _tree;

        // ---- explanation of the selected row ----
        var info = Ui.Panel(DockStyle.Bottom, 84);
        var infoText = Ui.TextBlock("Click a row to see what it is.", Ui.Pad, 10, 1300, 64, Ui.BigFont, Ui.Dim);
        info.Controls.Add(infoText);
        SaoNode? Selected() => tree.FocusedNode == null ? null : tree.GetDataRecordByNode(tree.FocusedNode) as SaoNode;
        tree.FocusedNodeChanged += (_, _) =>
        {
            var n = Selected();
            if (n == null) return;
            string value = n.IsValue || n.Kind == NodeKind.Str ? $"   Now: {SaoTreeEdit.Format(save, n)}" : "";
            infoText.Text = $"{DisplayName(n)}{value}\n{AdvancedHelp.Describe(n)}{(AdvancedHelp.Risky(n) ? "  Careful: this one is easy to break." : "")}";
            infoText.Appearance.ForeColor = AdvancedHelp.Risky(n) ? Ui.Warning : Ui.Accent;
        };

        // ---- toolbar ----
        var bar = Ui.Panel(DockStyle.Top, 196);
        bar.Controls.Add(Ui.Label("Every value in your save, in folders. For most things the other pages are easier.", Ui.Pad, 12, Ui.HeadingFont));
        bar.Controls.Add(Ui.TextBlock(
            "1. Open a folder with its arrow.   2. Click a row: the box at the bottom says what it is.   3. Click the Value cell, type a new number, press Enter.   4. Press Save at the top.\n" +
            "On/off values: type On or Off. A change the game could not read is refused, so you cannot break the file with a typo.",
            Ui.Pad, 44, 1300, 44, color: Ui.Dim));

        var find = new TextEdit { Location = new Point(Ui.Pad, 98), Size = new Size(320, 32) };
        find.Properties.NullValuePrompt = "Search (try: credits, play time, medals)";
        bar.Controls.Add(find);
        bar.Controls.Add(Ui.Button("Find next", Ui.Pad + 330, 110, (_, _) => FindNext(tree, save, find.Text, Selected()), 96));
        find.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { FindNext(tree, save, find.Text, Selected()); e.Handled = e.SuppressKeyPress = true; } };

        // quick jumps to the things people look for
        string slotName = Enumerable.Range(1, 16).Select(i => _tree.Find($"Slot {i}")).TakeWhile(s => s != null)
            .FirstOrDefault(s => s!.Find("Player/name/source")?.Text is { } nm && nm != "Player")?.Name ?? "Slot 1";
        int jx = Ui.Pad + 460;
        foreach (var (label, path, w) in new[]
        {
            ("Credits", $"{slotName}/Player/credits", 90), ("EXP", $"{slotName}/Player/experience", 70),
            ("Play time", $"{slotName}/Play record/play time (seconds)", 110), ("Medals", $"{slotName}/Play record/medals", 90),
            ("Partner money", $"{slotName}/ArFA-sys data/pocket money", 140),
        })
        {
            bar.Controls.Add(Ui.Button(label, jx, w, (_, _) => FocusPath(tree, "save/" + path), 96));
            jx += w + 8;
        }

        bar.Controls.Add(Ui.Button("Add a copy to the list", Ui.Pad, 200, (_, _) =>
        {
            var n = Selected();
            var list = n?.Kind == NodeKind.List ? n : n?.Parent;
            if (list == null) return;
            try { SwapSave(SaoTreeEdit.AddCopy(save, list), list.Path, $"{list.Path}: added a copy of the last entry."); }
            catch (Exception ex) when (ex is ArgumentException or SaoParseException or InvalidDataException) { XtraMessageBox.Show(this, ex.Message, AppTitle); }
        }, 146));
        bar.Controls.Add(Ui.Button("Remove from the list", Ui.Pad + 210, 190, (_, _) =>
        {
            var n = Selected();
            if (n?.Parent is not { Kind: NodeKind.List }) { XtraMessageBox.Show(this, "Select an entry of a list first.", AppTitle); return; }
            if (XtraMessageBox.Show(this, $"Remove {n.Path}?", AppTitle, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { SwapSave(SaoTreeEdit.Remove(save, n), n.Parent.Path, $"{n.Path}: removed."); }
            catch (Exception ex) when (ex is ArgumentException or SaoParseException or InvalidDataException) { XtraMessageBox.Show(this, ex.Message, AppTitle); }
        }, 146));
        var unknown = new CheckEdit { Text = "Also show fields nobody understands yet", Location = new Point(Ui.Pad + 420, 153), Width = 380, Checked = _showUnknown };
        unknown.CheckedChanged += (_, _) =>
        {
            _showUnknown = unknown.Checked;
            _focusPath = Selected()?.Path;
            BeginInvoke(() =>
            {
                _buildingTree = true;
                try { ShowPages(); _tabs.SelectedTabPageIndex = TabIndexOf(AdvancedTitle); }
                finally { _buildingTree = false; }
            });
        };
        bar.Controls.Add(unknown);

        page.Controls.Add(tree);
        page.Controls.Add(info);
        page.Controls.Add(bar);

        if (_focusPath != null)
        {
            string path = _focusPath;
            _focusPath = null;
            tree.HandleCreated += (_, _) => BeginInvoke(() => FocusPath(tree, path));
        }
        return page;
    }

    private static string DisplayName(SaoNode n)
    {
        // Show item names next to inventory entries.
        if (n.Kind == NodeKind.Group && n.Name.StartsWith('[') && n.Children.FirstOrDefault()?.Name == "item id" && n.Children[0].Text is { } id && id != ItemRecord.EmptyId)
            return $"{n.Name}  {SaoItems.NameOf(id)}";
        return AdvancedHelp.Friendly(n);
    }

    /// <summary>Expands the tree down to a path and focuses it.</summary>
    private static void FocusPath(TreeList tree, string path)
    {
        var parts = path.Split('/').Skip(1).ToArray();
        TreeListNodes nodes = tree.Nodes;
        TreeListNode? hit = null;
        foreach (var part in parts)
        {
            hit = nodes.Cast<TreeListNode>().FirstOrDefault(x => (tree.GetDataRecordByNode(x) as SaoNode)?.Name == part);
            if (hit == null) break;
            hit.Expanded = true;
            nodes = hit.Nodes;
        }
        if (hit != null) { tree.FocusedNode = hit; tree.MakeNodeVisible(hit); }
    }

    /// <summary>Finds the next entry (after the selected one) whose name or text contains the query.</summary>
    private void FindNext(TreeList tree, SaoSaveFile save, string query, SaoNode? from)
    {
        if (string.IsNullOrWhiteSpace(query) || _tree == null) return;
        bool started = from == null;
        foreach (var n in _tree.Descendants())
        {
            if (!started) { started = n == from; continue; }
            if (!_showUnknown && AdvancedHelp.IsUnknown(n)) continue;
            if (n.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || AdvancedHelp.Friendly(n).Contains(query, StringComparison.OrdinalIgnoreCase)
                || n.Kind == NodeKind.Str && (n.Text ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FocusPath(tree, n.Path);
                return;
            }
        }
        SetStatus($"\"{query}\" not found after the selected entry.");
    }
}
