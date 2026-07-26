using System.Collections.Generic;
using System.Windows.Input;

namespace Eidolon.App;

/// <summary>A bindable command exposed in the shortcut UI.</summary>
public sealed record ShortcutEntry(string Id, string NameKey, string Category);

/// <summary>Static registry of all shortcut-bindable commands.</summary>
public static class ShortcutRegistry
{
    public static readonly ShortcutEntry[] Entries = new ShortcutEntry[]
    {
        // File
        new("File.New",           "Menu.New",        "File"),
        new("File.Open",          "Menu.Open",       "File"),
        new("File.Save",          "Menu.Save",       "File"),
        new("File.SaveAs",        "Menu.SaveAs",     "File"),
        new("File.Export",        "Menu.Export",     "File"),
        // Edit
        new("Edit.Undo",          "Menu.Undo",       "Edit"),
        new("Edit.Redo",          "Menu.Redo",       "Edit"),
        new("Edit.SelectAll",     "Menu.SelectAll",  "Edit"),
        new("Edit.Deselect",      "Menu.Deselect",   "Edit"),
        new("Edit.InvertSel",     "Menu.InvertSel",  "Edit"),
        // View
        new("View.Reset",         "Menu.ResetView",  "View"),
        new("View.Mirror",        "Menu.Mirror",     "View"),
        // Modes
        new("Mode.Raster",        "Mode.Raster",     "Mode"),
        new("Mode.Vector",        "Mode.Vector",     "Mode"),
        new("Mode.Frame",         "Mode.Frame",      "Mode"),
        // Raster tools
        new("Tool.Brush",         "Tool.Brush",      "Tool"),
        new("Tool.Pencil",        "Tool.Pencil",     "Tool"),
        new("Tool.Airbrush",      "Tool.Airbrush",   "Tool"),
        new("Tool.Eraser",        "Tool.Eraser",     "Tool"),
        new("Tool.Watercolor",    "Tool.Watercolor", "Tool"),
        new("Tool.Marker",        "Tool.Marker",     "Tool"),
        new("Tool.Smudge",        "Tool.Smudge",     "Tool"),
        new("Tool.Fill",          "Tool.FillBucket", "Tool"),
        new("Tool.Gradient",      "Tool.Gradient",   "Tool"),
        // Selection tools
        new("Tool.Select",        "Tool.Select",     "Tool"),
        new("Tool.RectSelect",    "Tool.RectSelect", "Tool"),
        new("Tool.Lasso",         "Tool.Lasso",      "Tool"),
        new("Tool.MagicWand",     "Tool.MagicWand",  "Tool"),
        // Vector tools
        new("Tool.VectorPen",     "Tool.VectorPen",     "Tool"),
        new("Tool.VectorEraser",  "Tool.VectorEraser",  "Tool"),
        new("Tool.VectorNode",    "Tool.VectorNode",    "Tool"),
        new("Tool.VectorFill",    "Tool.VectorFill",    "Tool"),
        new("Tool.VectorSpline",  "Tool.VectorSpline",  "Tool"),
        // Frame
        new("Tool.FrameRect",     "Tool.FrameRect",  "Tool"),
        // Quick actions
        new("SwapColors",         "Tool.Swap",       "Quick"),
        new("StraightLine",       "Tool.StraightLine","Quick"),
        new("Settings",           "Menu.Settings",   "Quick"),
        // Ruler
        new("Ruler.None",             "Ruler.None",             "Ruler"),
        new("Ruler.Straight",         "Ruler.Straight",         "Ruler"),
        new("Ruler.Ellipse",          "Ruler.Ellipse",          "Ruler"),
        new("Ruler.Symmetry",         "Ruler.Symmetry",         "Ruler"),
        new("Ruler.VanishingPoint",   "Ruler.VanishingPoint",   "Ruler"),
        new("Ruler.Perspective1",     "Ruler.Perspective1",     "Ruler"),
        new("Ruler.Perspective2",     "Ruler.Perspective2",     "Ruler"),
        new("Ruler.Perspective3",     "Ruler.Perspective3",     "Ruler"),
        new("Ruler.Fisheye6",         "Ruler.Fisheye6",         "Ruler"),
        new("Ruler.ToggleVisible",    "Ruler.Visible",          "Ruler"),
        new("Ruler.ToggleSnap0",      "Ruler.LineSnap0",        "Ruler"),
        new("Ruler.ToggleSnap1",      "Ruler.LineSnap1",        "Ruler"),
        new("Ruler.ToggleSnap2",      "Ruler.LineSnap2",        "Ruler"),
        new("Ruler.PModeCycle",       "Ruler.PModeSnap",        "Ruler"),
        new("Ruler.Reset",            "Ruler.Reset",            "Ruler"),
    };

    /// <summary>Parse a gesture string like "Ctrl+N" into Key + ModifierKeys.</summary>
    public static (Key Key, ModifierKeys Mods)? ParseGesture(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return null;
        var s = gesture.Trim();
        ModifierKeys mods = ModifierKeys.None;
        Key key = Key.None;

        var parts = s.Split('+');
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Control;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Shift;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Alt;
            else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Windows;
            else
            {
                if (!Enum.TryParse(p, true, out key))
                    return null;
            }
        }

        return key == Key.None ? null : (key, mods);
    }

    /// <summary>Convert Key + ModifierKeys to a gesture string like "Ctrl+N".</summary>
    public static string FormatGesture(Key key, ModifierKeys mods)
    {
        var parts = new List<string>(4);
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}
