using System.Drawing;
using DevExpress.LookAndFeel;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using SaoSave.Core;

namespace SaoSave.Editor;

/// <summary>Small factories so every page builds its controls the same way.</summary>
internal static class Ui
{
    public const string Author = "zXIJz";
    public const string Credit = "Made by " + Author;

    public const int Pad = 24;
    public const int ButtonHeight = 36;

    public static readonly Font TitleFont = new("Segoe UI", 16F, FontStyle.Bold);
    public static readonly Font HeadingFont = new("Segoe UI", 12F, FontStyle.Bold);
    public static readonly Font CreditFont = new("Segoe UI", 11F, FontStyle.Bold);
    public static readonly Font BigFont = new("Segoe UI", 11F);

    /// <summary>Skin colours, so nothing here brings its own palette.</summary>
    public static Color Accent => DXSkinColors.ForeColors.Question;
    public static Color Warning => DXSkinColors.ForeColors.Warning;
    public static Color Dim => DXSkinColors.ForeColors.DisabledText;

    public static PanelControl Panel(DockStyle dock, int size = 0)
    {
        var p = new PanelControl { Dock = dock, BorderStyle = BorderStyles.NoBorder };
        if (dock is DockStyle.Top or DockStyle.Bottom) p.Height = size;
        if (dock is DockStyle.Left or DockStyle.Right) p.Width = size;
        return p;
    }

    public static SimpleButton Button(string text, int x, int width, EventHandler onClick, int y = 12)
    {
        var b = new SimpleButton { Text = text, Location = new Point(x, y), Size = new Size(width, ButtonHeight) };
        b.Click += onClick;
        return b;
    }

    public static LabelControl Label(string text, int x, int y, Font? font = null, Color? color = null)
    {
        var l = new LabelControl { Text = text, Location = new Point(x, y) };
        if (font != null) l.Appearance.Font = font;
        if (color != null) l.Appearance.ForeColor = color.Value;
        return l;
    }

    /// <summary>The credit line. Every page and the window frame carry one.</summary>
    public static LabelControl CreditLabel(int x, int y) => Label(Credit, x, y, CreditFont, Accent);

    /// <summary>A wrapped block of text with a fixed size.</summary>
    public static LabelControl TextBlock(string text, int x, int y, int width, int height, Font? font = null, Color? color = null)
    {
        var l = new LabelControl
        {
            Text = text,
            Location = new Point(x, y),
            AutoSizeMode = LabelAutoSizeMode.None,
            Size = new Size(width, height),
        };
        l.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
        l.Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Top;
        if (font != null) l.Appearance.Font = font;
        if (color != null) l.Appearance.ForeColor = color.Value;
        return l;
    }

    public static SpinEdit WholeNumber(int min = 0, int max = int.MaxValue)
    {
        var spin = new SpinEdit();
        spin.Properties.IsFloatValue = false;
        spin.Properties.Mask.EditMask = "N0";
        spin.Properties.Mask.UseMaskAsDisplayFormat = true;
        spin.Properties.MinValue = min;
        spin.Properties.MaxValue = max;
        return spin;
    }
}
