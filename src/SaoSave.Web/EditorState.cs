using SaoSave.Core;

namespace SaoSave.Web;

/// <summary>
/// The open save and everything the pages share. Every write goes through Core (SaoSaveFile,
/// SaoTreeEdit, SaoEdits, SaoAvatar), the same code the desktop editor uses.
/// </summary>
public sealed class EditorState
{
    public const string AppTitle = "Sword Art Online: Fatal Bullet Save Editor";
    public const string Author = "zXIJz";
    public const string Credit = "Made by " + Author;

    public SaoSaveFile? Save { get; private set; }
    public byte[]? OriginalBytes { get; private set; }
    public string FileName { get; private set; } = "";
    public bool Dirty { get; private set; }
    public List<string> LogLines { get; } = new();
    public string Status { get; private set; } = "No save open";

    /// <summary>Bumped on every open, undo and rebuilt save so pages start from the new data.</summary>
    public int Generation { get; private set; }

    public event Action? Changed;

    // ---- the whole-save layout (Looks, Avatar, Advanced) ----

    public SaoNode? Tree { get; private set; }
    public string? TreeError { get; private set; }
    private bool _treeStale = true;

    // Page choices that survive a rebuild.
    public int LooksWho, SkillsWho, AvatarWho;
    public HashSet<string> Expanded { get; } = new();
    public string? Selected { get; set; }
    public bool ShowUnknown { get; set; }

    public void Log(string message)
    {
        LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        Status = message;
        Changed?.Invoke();
    }

    /// <summary>An edit of the item lists, characters or fields: the layout must be read again before it is used.</summary>
    public void MarkDirty()
    {
        _treeStale = true;
        Dirty = true;
        Changed?.Invoke();
    }

    /// <summary>An edit written straight into the data (the layout is still right).</summary>
    public void MarkDirtyInPlace()
    {
        Dirty = true;
        Changed?.Invoke();
    }

    public void Open(byte[] data, string fileName)
    {
        var save = SaoSaveFile.Load(data, fileName);
        Save = save;
        OriginalBytes = data;
        FileName = fileName;
        Dirty = false;
        Tree = null;
        TreeError = null;
        _treeStale = true;
        Generation++;
        Log($"Opened {fileName} ({data.Length:N0} bytes). Player {save.PlayerName}. {save.Arrays.Count} item lists; bag {save.Inventory?.Used ?? 0} of {save.Inventory?.Slots.Count ?? 0} slots used.");
        if (!save.SignatureValid) Log("WARNING: the file's signature does not match. Saving will write a fresh one.");
        int fixedTypes = SaoEdits.FixWeaponTypes(save);
        if (fixedTypes > 0)
        {
            Dirty = true;
            _treeStale = true;
            Log($"Fixed the weapon type of {fixedTypes} guns (they were all filed as handguns, so they could not be equipped). Save to keep it.");
        }
    }

    public void Undo()
    {
        if (OriginalBytes != null) Open(OriginalBytes, FileName);
    }

    /// <summary>Writes pending edits and reads the whole layout, when that has not been done since the last edit.</summary>
    public void EnsureTree()
    {
        if (Save == null || (!_treeStale && Tree != null) || (TreeError != null && !_treeStale)) return;
        try
        {
            Save = Save.Committed();
            Tree = Save.ParseTree();
            TreeError = null;
        }
        catch (Exception ex) when (ex is SaoParseException or InvalidDataException)
        {
            Tree = null;
            TreeError = ex.Message;
            Log("The full layout could not be read: " + ex.Message);
        }
        _treeStale = false;
    }

    /// <summary>Takes a save the layout editor rebuilt (sizes changed) and shows it.</summary>
    public void Replace(SaoSaveFile next, string what)
    {
        Save = next;
        Tree = next.ParseTree();
        TreeError = null;
        _treeStale = false;
        Dirty = true;
        Generation++;
        Log(what);
    }

    public void Rebuild()
    {
        Generation++;
        Changed?.Invoke();
    }

    public byte[] Export()
    {
        var bytes = Save!.Export();
        Dirty = false;
        Log($"Saved {FileName} ({bytes.Length:N0} bytes). Your browser downloads it; your original file is not changed.");
        return bytes;
    }

    // ---- in-page confirmation (no browser alert/confirm boxes) ----

    public string? Question { get; private set; }
    private Action? _onYes;
    public bool NoticeOnly { get; private set; }

    public void Ask(string question, Action onYes)
    {
        Question = question;
        _onYes = onYes;
        NoticeOnly = false;
        Changed?.Invoke();
    }

    public void Notice(string message)
    {
        Question = message;
        _onYes = null;
        NoticeOnly = true;
        Changed?.Invoke();
    }

    public void Answer(bool yes)
    {
        var act = yes ? _onYes : null;
        Question = null;
        _onYes = null;
        act?.Invoke();
        Changed?.Invoke();
    }

    // ---- shared helpers ----

    public string Val(SaoNode? n) => n == null || Save == null ? "" : SaoTreeEdit.Format(Save, n);

    /// <summary>
    /// Writes one value of the layout. A same-size value is written in place; anything else rebuilds
    /// the save. A value the game's layout would not accept is refused and nothing changes.
    /// </summary>
    public bool Apply(SaoNode? n, string text, string what)
    {
        if (n == null || Save == null || Val(n) == text) return false;
        try
        {
            var next = SaoTreeEdit.Set(Save, n, text);
            if (next == null) { MarkDirtyInPlace(); Log(what); }
            else Replace(next, what);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or SaoParseException or InvalidDataException)
        {
            Log($"Not changed: {ex.Message}");
            Generation++;
            Notice($"That value was not changed.\n\n{ex.Message}");
            return false;
        }
    }
}
