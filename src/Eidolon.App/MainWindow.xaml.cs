using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Eidolon.App.Controls;
using Eidolon.App.Localization;
using Eidolon.App.Logging;
using Eidolon.Brush;
using Eidolon.Core;
using Eidolon.IO;
using Microsoft.Win32;

namespace Eidolon.App;

public partial class MainWindow : Window
{
    private string? _currentPath;
    private bool _colorUiSilent;
    private bool _layerUiSilent;
    private int _colorModel = 3; // 0 RGB 1 HSV 2 HSL 3 OKLCH
    private int _statusFrame;
    private readonly Controls.TimelapseRecorder _timelapse = new();
    private HistoryStack? _historyBound;
                
    public MainWindow()
    {
        AppLog.Info("MainWindow ctor begin", "UI");
        InitializeComponent();
        AppLog.Info("MainWindow InitializeComponent done", "UI");
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => NewDocument()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => OpenDocument()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => SaveDocument(false)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Canvas.Undo()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => Canvas.Redo()));

        // Shortcuts are loaded from settings in ApplyShortcuts (called after settings load)

        Loaded += (_, _) =>
        {
            try
            {
            AppLog.Info("MainWindow Loaded", "UI");
            AppSettings.Load();
            ApplySettingsToUi();
            var st = AppSettings.Current;
            Canvas.NewDocument(st.DefaultCanvasWidth, st.DefaultCanvasHeight);
            if (Canvas.Document is not null) LocalizeNewDocument(Canvas.Document);
            ApplyToolsAndColorsFromSettings();
            ApplyShortcuts();
            Canvas.HistoryChanged += (_, _) => UpdateTitle();
            BindHistory(Canvas.Document);
            Canvas.DocumentChanged += (_, _) =>
            {
                BindHistory(Canvas.Document);
                if (_timelapse.IsRecording && Canvas.Document is not null)
                    _timelapse.BindDocument(Canvas.Document);
                RefreshLayerList();
                SyncColorUi();
                UpdateTitle();
                UpdateToolbarsForLayer();
            };
            Canvas.StatusChanged += (_, _) =>
            {
                SyncColorUi();
                UpdateMixBar();
                UpdateInputStatus();
            };
            RefreshLayerList();
            UpdateTitle();
            UpdateInputStatus();
            UpdateMixBar();
            UpdateColorLabels();
            SyncColorUi();
            UpdateToolbarsForLayer();
            ApplyTitleBarDpiScale();
            StateChanged += (_, _) =>
            {
                if (MaxBtn != null)
                    MaxBtn.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            };
            Canvas.Focus();
            AppLog.Info("MainWindow Loaded complete", "UI");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "MainWindow Loaded failed", "UI");
                ThemedMessageWindow.Show(this, "Eidolon", SR.Format("App.InitFailed", ex.Message, AppLog.LogFilePath), UiMessageKind.Error);
            }
        };

        Deactivated += (_, _) =>
        {
            try { Canvas?.NotifyHostDeactivated(); }
            catch (Exception ex) { AppLog.Warn("Deactivated cleanup: " + ex.Message, "UI"); }
        };

        Closing += (_, _) =>
        {
            try
            {
                CaptureToolsAndColorsToSettings();
                AppSettings.Save();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Failed to save tools/colors settings: " + ex.Message, "Settings");
            }
        };

        Activated += (_, _) =>
        {
            try
            {
                Canvas?.NotifyHostActivated();
                Canvas?.Focus();
            }
            catch (Exception ex) { AppLog.Warn("Activated recover: " + ex.Message, "UI"); }
        };

        _statusFrame = 0;
        CompositionTarget.Rendering += (_, _) =>
        {
            _statusFrame++;
            if (_statusFrame % 4 != 0) return;
            if (Canvas?.Document is null || ZoomText is null) return;
            try
            {
                float pp = Canvas.PointerPen.HasRecentSample ? Canvas.PointerPen.LastPressure
                    : Canvas.WinTab.HasRecentPacket ? Canvas.WinTab.LastPressure : 1f;
                ZoomText.Text = $"{Canvas.Viewport.Scale * 100:0}%  |  P={pp:F2}  |  {Canvas.PenStatus}";
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "Status render tick", "UI");
            }
        };
    }

    private void UpdateInputStatus()
    {
        string erase = Canvas.Preset.Params.EraseMode ? SR.Get("Status.EraseMode") : "";
        StatusText.Text = SR.Get("Status.Hint") + erase;
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e)
    {
        // Remove overflow grip overflow for cleaner look
        if (sender is ToolBar bar)
        {
            if (bar.Template.FindName("OverflowGrid", bar) is FrameworkElement overflow)
                overflow.Visibility = Visibility.Collapsed;
            if (bar.Template.FindName("MainPanelBorder", bar) is FrameworkElement main)
                main.Margin = new Thickness(0);
        }
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewDocument();
    private void Open_Click(object sender, RoutedEventArgs e) => OpenDocument();
    private void Save_Click(object sender, RoutedEventArgs e) => SaveDocument(false);
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveDocument(true);
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow { Owner = this };
        if (dlg.ShowDialog() == true)
            ApplySettingsToUi();
    }

    private void ApplySettingsToUi()
    {
        var s = AppSettings.Current;
        if (ColorModelCombo != null)
        {
            ColorModelCombo.SelectedIndex = Math.Clamp(s.DefaultColorModel, 0, 3);
            _colorModel = ColorModelCombo.SelectedIndex;
            UpdateColorLabels();
            SyncColorUi();
        }
        if (TimelapsePanel != null)
            TimelapsePanel.Visibility = s.TimelapseEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (TimelapseDirText != null && string.IsNullOrWhiteSpace(TimelapseDirText.Text))
            TimelapseDirText.Text = s.TimelapseDirectory;
        if (TimelapseFileNameText != null && (string.IsNullOrWhiteSpace(TimelapseFileNameText.Text) || TimelapseFileNameText.Text == "timelapse"))
            TimelapseFileNameText.Text = s.TimelapseFileName;
        if (TimelapseFpsSlider != null)
            TimelapseFpsSlider.Value = s.TimelapseFps;
    }

    private void ApplyToolsAndColorsFromSettings()
    {
        if (Canvas is null) return;
        var s = AppSettings.Current;
        var b = s.Brush ?? new BrushToolSettings();
        var c = s.Colors ?? new ColorToolSettings();
        var p = Canvas.Preset.Params;

        p.SizePx = b.SizePx;
        p.MinSizeRatio = b.MinSizeRatio;
        p.Opacity = b.Opacity;
        p.Flow = b.Flow;
        p.Hardness = b.Hardness;
        p.SoftEdge = b.SoftEdge;
        p.Blend = b.Blend;
        p.Spacing = b.Spacing;
        p.AntiAlias = b.AntiAlias;
        p.LockAlpha = b.LockAlpha;
        p.StabilizerStrength = b.StabilizerStrength;
        p.SizeByPressure = b.SizeByPressure;
        p.OpacityByPressure = b.OpacityByPressure;
        p.FlowByPressure = b.FlowByPressure;
        p.TextureStrength = b.TextureStrength;
        p.TextureScale = b.TextureScale;
        p.TextureSeed = b.TextureSeed;
        p.SmudgeStrength = b.SmudgeStrength;

        Canvas.BrushSize = p.SizePx;
        Canvas.Stabilizer = b.StabilizerStrength;
        Canvas.StraightLineMode = b.StraightLineMode;
        Canvas.LockAlphaBrush = p.LockAlpha;

        if (Canvas.Document is not null)
        {
            Canvas.Document.Colors.Foreground = new ColorRgba8(c.FgR, c.FgG, c.FgB, 255);
            Canvas.Document.Colors.Background = new ColorRgba8(c.BgR, c.BgG, c.BgB, 255);
        }

        SyncBrushParamsFromPreset();
        SyncColorUi();
        UpdateMixBar();
        UpdateColorLabels();

        // Apply ruler line snap toggles from settings
        if (Canvas.Document is not null)
        {
            var r = Canvas.Document.Rulers;
            r.PerspectiveLine0Enabled = s.RulerLineSnap0;
            r.PerspectiveLine1Enabled = s.RulerLineSnap1;
            r.PerspectiveLine2Enabled = s.RulerLineSnap2;
            // Reflect in UI if panel is visible
            bool isPersp = r.Kind is RulerKind.Perspective1 or RulerKind.Perspective2
                or RulerKind.Perspective3 or RulerKind.Fisheye6;
            if (RulerLineToggles is not null)
                RulerLineToggles.Visibility = isPersp ? Visibility.Visible : Visibility.Collapsed;
            if (isPersp)
            {
                if (RulerLine0Check is not null) RulerLine0Check.IsChecked = r.PerspectiveLine0Enabled;
                if (RulerLine1Check is not null) RulerLine1Check.IsChecked = r.PerspectiveLine1Enabled;
                if (RulerLine2Check is not null) RulerLine2Check.IsChecked = r.PerspectiveLine2Enabled;
            }

            // Apply Fisheye6 P settings
            r.FisheyePMode = s.FisheyePMode switch
            {
                "VisualOnly" => FisheyePMode.VisualOnly,
                "Snappable" => FisheyePMode.Snappable,
                _ => FisheyePMode.Off
            };
            r.FisheyeP = new Float2((float)s.FisheyePX, (float)s.FisheyePY);
            if (RulerPModeCombo is not null)
            {
                bool isFish = r.Kind == RulerKind.Fisheye6;
                RulerPModeCombo.Visibility = isFish ? Visibility.Visible : Visibility.Collapsed;
                if (isFish) RulerPModeCombo.SelectedIndex = (int)r.FisheyePMode;
            }
        }

        AppLog.Info("Applied tools/colors from settings", "Settings");
    }

    /// <summary>Register shortcut key bindings from settings (default: none).</summary>
    private void ApplyShortcuts()
    {
        InputBindings.Clear();
        var s = AppSettings.Current;
        s.Shortcuts ??= new();
        foreach (var entry in ShortcutRegistry.Entries)
        {
            if (!s.Shortcuts.TryGetValue(entry.Id, out var gesture) || string.IsNullOrWhiteSpace(gesture))
                continue;
            var parsed = ShortcutRegistry.ParseGesture(gesture);
            if (parsed is not ({ } key, { } mods)) continue;

            // Use a custom RoutedUICommand so we can route to ExecuteCommandById
            var cmd = new RoutedUICommand(entry.Id, entry.Id, typeof(MainWindow));
            InputBindings.Add(new KeyBinding(cmd, key, mods));
            CommandBindings.Add(new CommandBinding(cmd, (_, _) => ExecuteCommandById(entry.Id)));
        }
    }

    /// <summary>Execute a command by its shortcut registry ID.</summary>
    public void ExecuteCommandById(string id)
    {
        switch (id)
        {
            case "File.New":        NewDocument(); break;
            case "File.Open":       OpenDocument(); break;
            case "File.Save":       SaveDocument(false); break;
            case "File.SaveAs":     SaveDocument(true); break;
            case "File.Export":     ExportCurrent(); break;
            case "Edit.Undo":       Canvas?.Undo(); break;
            case "Edit.Redo":       Canvas?.Redo(); break;
            case "Edit.SelectAll":  SelectAll(); break;
            case "Edit.Deselect":   Deselect(); break;
            case "Edit.InvertSel":  InvertSelection(); break;
            case "View.Reset":      ResetView(); break;
            case "View.Mirror":     MirrorCanvas(); break;
            case "Mode.Raster":     SetMode("Raster"); break;
            case "Mode.Vector":     SetMode("Vector"); break;
            case "Mode.Frame":      SetMode("Frame"); break;
            case "Tool.Brush":      SelectTool("Brush"); break;
            case "Tool.Pencil":     SelectTool("Pencil"); break;
            case "Tool.Airbrush":   SelectTool("Airbrush"); break;
            case "Tool.Eraser":     SelectTool("Eraser"); break;
            case "Tool.Watercolor": SelectTool("Watercolor"); break;
            case "Tool.Marker":     SelectTool("Marker"); break;
            case "Tool.Smudge":     SelectTool("Smudge"); break;
            case "Tool.Fill":       SelectTool("Fill"); break;
            case "Tool.Gradient":   SelectTool("Gradient"); break;
            case "Tool.Select":     SelectTool("Select"); break;
            case "Tool.RectSelect": SelectTool("RectSelect"); break;
            case "Tool.Lasso":      SelectTool("Lasso"); break;
            case "Tool.MagicWand":  SelectTool("MagicWand"); break;
            case "Tool.VectorPen":    SelectTool("VectorPen"); break;
            case "Tool.VectorEraser": SelectTool("VectorEraser"); break;
            case "Tool.VectorNode":   SelectTool("VectorNode"); break;
            case "Tool.VectorFill":   SelectTool("VectorFill"); break;
            case "Tool.VectorSpline": SelectTool("VectorSpline"); break;
            case "Tool.FrameRect":  SelectTool("FrameRect"); break;
            case "SwapColors":      Canvas?.Document?.Colors.Swap(); Canvas?.InvalidateVisual(); break;
            case "StraightLine":    ToggleStraightLine(); break;
            case "Settings":        OpenSettings(); break;
        }
    }

    private void SelectTool(string name)
    {
        if (Canvas is null) return;
        Canvas.Tool = name switch
        {
            "Select" => CanvasTool.Select,
            "Brush" => CanvasTool.Brush,
            "Pencil" => CanvasTool.Brush, // same tool, different preset
            "Airbrush" => CanvasTool.Brush,
            "Eraser" => CanvasTool.Brush,
            "Watercolor" => CanvasTool.Brush,
            "Marker" => CanvasTool.Brush,
            "Smudge" => CanvasTool.Brush,
            "Fill" => CanvasTool.Fill,
            "Gradient" => CanvasTool.Gradient,
            "RectSelect" => CanvasTool.RectSelect,
            "Lasso" => CanvasTool.Lasso,
            "MagicWand" => CanvasTool.MagicWand,
            "VectorPen" => CanvasTool.VectorPen,
            "VectorEraser" => CanvasTool.VectorEraser,
            "VectorNode" => CanvasTool.VectorNode,
            "VectorFill" => CanvasTool.VectorCloseFill,
            "VectorSpline" => CanvasTool.VectorSpline,
            "FrameRect" => CanvasTool.FrameRect,
            _ => Canvas.Tool
        };
    }

    private void SetMode(string mode) { /* TODO */ }

    private void MirrorCanvas() { Canvas?.ToggleMirror(); }
    private void ResetView() { Canvas?.ResetView(); }
    private void SelectAll() { Canvas?.SelectAll(); }
    private void Deselect() { Canvas?.ClearSelection(); }
    private void InvertSelection() { Canvas?.InvertSelection(); }
    private void ExportCurrent() { SaveDocument(true); }
    private void ToggleStraightLine() { if (Canvas is not null) Canvas.StraightLineMode = !Canvas.StraightLineMode; }
    private void OpenSettings() { Settings_Click(this, new RoutedEventArgs()); }

    private void CaptureToolsAndColorsToSettings()
    {
        if (Canvas is null) return;
        var s = AppSettings.Current;
        s.Brush ??= new BrushToolSettings();
        s.Colors ??= new ColorToolSettings();

        var p = Canvas.Preset.Params;
        var b = s.Brush;
        b.SizePx = p.SizePx;
        b.MinSizeRatio = p.MinSizeRatio;
        b.Opacity = p.Opacity;
        b.Flow = p.Flow;
        b.Hardness = p.Hardness;
        b.SoftEdge = p.SoftEdge;
        b.Blend = p.Blend;
        b.Spacing = p.Spacing;
        b.AntiAlias = p.AntiAlias;
        b.LockAlpha = p.LockAlpha;
        b.StabilizerStrength = p.StabilizerStrength > 0 ? p.StabilizerStrength : Canvas.Stabilizer;
        b.SizeByPressure = p.SizeByPressure;
        b.OpacityByPressure = p.OpacityByPressure;
        b.FlowByPressure = p.FlowByPressure;
        b.TextureStrength = p.TextureStrength;
        b.TextureScale = p.TextureScale;
        b.TextureSeed = p.TextureSeed;
        b.SmudgeStrength = p.SmudgeStrength;
        b.StraightLineMode = Canvas.StraightLineMode;

        if (Canvas.Document is not null)
        {
            var fg = Canvas.Document.Colors.Foreground;
            var bg = Canvas.Document.Colors.Background;
            s.Colors.FgR = fg.R;
            s.Colors.FgG = fg.G;
            s.Colors.FgB = fg.B;
            s.Colors.BgR = bg.R;
            s.Colors.BgG = bg.G;
            s.Colors.BgB = bg.B;

            // Capture ruler line snap toggles
            var r = Canvas.Document.Rulers;
            s.RulerLineSnap0 = r.PerspectiveLine0Enabled;
            s.RulerLineSnap1 = r.PerspectiveLine1Enabled;
            s.RulerLineSnap2 = r.PerspectiveLine2Enabled;

            // Capture Fisheye6 P
            s.FisheyePMode = r.FisheyePMode switch
            {
                FisheyePMode.VisualOnly => "VisualOnly",
                FisheyePMode.Snappable => "Snappable",
                _ => "Off"
            };
            s.FisheyePX = r.FisheyeP.X;
            s.FisheyePY = r.FisheyeP.Y;
        }
    }

    private void ApplyTitleBarDpiScale()
    {
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double w = Math.Max(46, 46 * dpi);
        double h = Math.Max(32, 32 * dpi);
        double fs = Math.Max(14, 14 * dpi);
        foreach (var btn in new[] { MinBtn, MaxBtn, CloseBtn })
        {
            if (btn is null) continue;
            btn.Width = w;
            btn.Height = h;
            btn.FontSize = fs;
        }
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e) => Canvas.SelectAll();
    private void Deselect_Click(object sender, RoutedEventArgs e) => Canvas.ClearSelection();
    private void InvertSel_Click(object sender, RoutedEventArgs e) => Canvas.InvertSelection();
    private void Undo_Click(object sender, RoutedEventArgs e) => Canvas.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Canvas.Redo();
    private void ResetView_Click(object sender, RoutedEventArgs e) => Canvas.ResetView();
    private void Mirror_Click(object sender, RoutedEventArgs e) => Canvas.ToggleMirror();
    private void RotateLeft_Click(object sender, RoutedEventArgs e) => Canvas.RotateView(-15);
    private void RotateRight_Click(object sender, RoutedEventArgs e) => Canvas.RotateView(15);

    private void NewDocument()
    {
        var dlg = new NewDocumentDialog { Owner = this };
        // seed dialog defaults from settings via public fields if available
        if (dlg.ShowDialog() != true) return;
        var doc = new Document(dlg.DocWidth, dlg.DocHeight);
        doc.Background.Kind = dlg.BackgroundKind;
        LocalizeNewDocument(doc);
        Canvas.SetDocument(doc);
        _currentPath = null;
        RefreshLayerList();
        UpdateTitle();
        StatusText.Text = SR.Format("Status.NewDoc", dlg.DocWidth, dlg.DocHeight);
    }

    private static void LocalizeNewDocument(Document doc)
    {
        doc.Root.Name = SR.Get("Layer.Root");
        for (int i = 0; i < doc.Root.Children.Count; i++)
            doc.Root.Children[i].Name = SR.Format("Layer.DefaultN", i + 1);
    }

    private void OpenDocument()
    {
        var dlg = new OpenFileDialog { Filter = SR.Get("Dialog.FilterEidolon") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var doc = EidolonFileStore.Load(dlg.FileName);
            Canvas.SetDocument(doc);
            _currentPath = dlg.FileName;
            RefreshLayerList();
            UpdateTitle();
            StatusText.Text = SR.Format("Status.Opened", dlg.FileName);
        }
        catch (Exception ex)
        {
            ThemedMessageWindow.Show(this, SR.Get("Dialog.OpenFailed"), ex.Message, UiMessageKind.Info);
        }
    }

    private void SaveDocument(bool saveAs)
    {
        if (Canvas?.Document is null) return;
        string? path = _currentPath;
        if (saveAs || path is null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = SR.Get("Dialog.FilterEidolonSave"),
                FileName = SR.Get("Dialog.DefaultEidolon")
            };
            if (dlg.ShowDialog(this) != true) return;
            path = dlg.FileName;
        }
        try
        {
            EidolonFileStore.Save(Canvas.Document, path);
            _currentPath = path;
            UpdateTitle();
            StatusText.Text = SR.Format("Status.Saved", path);
        }
        catch (Exception ex)
        {
            ThemedMessageWindow.Show(this, SR.Get("Dialog.SaveFailed"), ex.Message, UiMessageKind.Info);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Canvas?.Document is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = SR.Get("Dialog.FilterExport"),
            FileName = SR.Get("Dialog.DefaultPng"),
            FilterIndex = 1
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var fmt = ImageExport.DetectFormat(dlg.FileName);
            ImageExport.Export(Canvas.Document, dlg.FileName, fmt, AppSettings.Current);
            StatusText.Text = SR.Format("Status.Exported", dlg.FileName);
        }
        catch (Exception ex)
        {
            ThemedMessageWindow.Show(this, SR.Get("Dialog.ExportFailed"), ex.Message, UiMessageKind.Info);
        }
    }

    private void NewVectorLayer_Click(object sender, RoutedEventArgs e)
    {
        if (Canvas.Document is null) return;
        Canvas.Document.AddVectorLayer(SR.Format("Layer.VectorN", Canvas.Document.Root.Children.Count + 1));
        Canvas.FullRedraw();
        RefreshLayerList();
        UpdateToolbarsForLayer();
        UpdateTitle();
    }

    private void NewFrameLayer_Click(object sender, RoutedEventArgs e)
    {
        if (Canvas.Document is null) return;
        Canvas.Document.AddFrameLayer(SR.Format("Layer.FrameN", Canvas.Document.Root.Children.Count + 1));
        Canvas.FullRedraw();
        RefreshLayerList();
        UpdateToolbarsForLayer();
        UpdateTitle();
    }

    private void NewLayer_Click(object sender, RoutedEventArgs e)
    {
        if (Canvas?.Document is null) return;
        Canvas.Document.AddRasterLayer(SR.Format("Layer.DefaultN", Canvas.Document.Root.Children.Count + 1));
        Canvas.FullRedraw();
        RefreshLayerList();
        UpdateToolbarsForLayer();
        UpdateTitle();
    }

    private void DeleteLayer_Click(object sender, RoutedEventArgs e)
    {
        if (Canvas?.Document is null || LayerList.SelectedItem is not LayerItem item) return;
        if (Canvas.Document.Root.Children.Count <= 1)
        {
            ThemedMessageWindow.Show(this, SR.Get("Panel.Layers"), SR.Get("Layer.KeepOne"), UiMessageKind.Info);
            return;
        }
        Canvas.Document.Root.Children.Remove(item.Layer);
        Canvas.Document.ActiveLayerId = Canvas.Document.Root.Children[^1].Id;
        Canvas.Document.IsDirty = true;
        Canvas.FullRedraw();
        RefreshLayerList();
        UpdateTitle();
    }

    private void RefreshLayers_Click(object sender, RoutedEventArgs e) => RefreshLayerList();

    private void RefreshLayerList()
    {
        _layerUiSilent = true;
        LayerList.Items.Clear();
        if (Canvas.Document is null) { _layerUiSilent = false; return; }
        for (int i = Canvas.Document.Root.Children.Count - 1; i >= 0; i--)
        {
            var layer = Canvas.Document.Root.Children[i];
            LayerList.Items.Add(new LayerItem(layer));
        }
        foreach (LayerItem item in LayerList.Items)
        {
            if (item.Layer.Id == Canvas.Document.ActiveLayerId)
            {
                LayerList.SelectedItem = item;
                LoadLayerProps(item.Layer);
                break;
            }
        }
        _layerUiSilent = false;
    }

    private void LayerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (_layerUiSilent) return;
        if (LayerList.SelectedItem is LayerItem item && Canvas.Document is not null)
        {
            Canvas.Document.ActiveLayerId = item.Layer.Id;
            LoadLayerProps(item.Layer);
            UpdateToolbarsForLayer();
            StatusText.Text = SR.Format("Status.CurrentLayer", item.Layer.Name);
            AppLog.Debug($"Active layer -> {item.Layer.Name} ({item.Layer.Kind})", "UI");
        }
    }


    private void LoadLayerProps(LayerNode layer)
    {
        _layerUiSilent = true;
        LayerOpacity.Value = layer.Opacity * 100;
        if (LayerOpacityVal != null) LayerOpacityVal.Text = $"{layer.Opacity * 100:0}%";
        LayerVisible.IsChecked = layer.Visible;
        LayerClip.IsChecked = layer.ClippedToBelow;
        LayerLockAlpha.IsChecked = (layer.Locks & LayerLocks.Transparency) != 0;
        BlendCombo.SelectedIndex = BlendToIndex(layer.Blend);
        _layerUiSilent = false;
    }

    private static int BlendToIndex(BlendMode m) => m switch
    {
        BlendMode.Multiply => 1,
        BlendMode.Screen => 2,
        BlendMode.Overlay => 3,
        BlendMode.Darken => 4,
        BlendMode.Lighten => 5,
        BlendMode.ColorDodge => 6,
        BlendMode.ColorBurn => 7,
        BlendMode.LinearDodge => 8,
        BlendMode.LinearBurn => 9,
        BlendMode.HardLight => 10,
        BlendMode.SoftLight => 11,
        BlendMode.Difference => 12,
        BlendMode.Exclusion => 13,
        _ => 0
    };

    private static BlendMode IndexToBlend(int i) => i switch
    {
        1 => BlendMode.Multiply,
        2 => BlendMode.Screen,
        3 => BlendMode.Overlay,
        4 => BlendMode.Darken,
        5 => BlendMode.Lighten,
        6 => BlendMode.ColorDodge,
        7 => BlendMode.ColorBurn,
        8 => BlendMode.LinearDodge,
        9 => BlendMode.LinearBurn,
        10 => BlendMode.HardLight,
        11 => BlendMode.SoftLight,
        12 => BlendMode.Difference,
        13 => BlendMode.Exclusion,
        _ => BlendMode.Normal
    };

    private void LayerOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        if (_layerUiSilent || Canvas.Document?.ActiveLayerId is not Guid id) return;
        if (Canvas.Document.FindLayer(id) is { } layer)
        {
            layer.Opacity = (float)(LayerOpacity.Value / 100.0);
            if (LayerOpacityVal != null) LayerOpacityVal.Text = $"{LayerOpacity.Value:0}%";
            Canvas.Document.IsDirty = true;
            Canvas.FullRedraw();
        }
    }

    private void LayerVisible_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (_layerUiSilent || Canvas.Document?.ActiveLayerId is not Guid id) return;
        if (Canvas.Document.FindLayer(id) is { } layer)
        {
            layer.Visible = LayerVisible.IsChecked == true;
            Canvas.Document.IsDirty = true;
            Canvas.FullRedraw();
            RefreshLayerList();
        }
    }

    private void LayerClip_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (_layerUiSilent || Canvas.Document?.ActiveLayerId is not Guid id) return;
        if (Canvas.Document.FindLayer(id) is { } layer)
        {
            layer.ClippedToBelow = LayerClip.IsChecked == true;
            Canvas.Document.IsDirty = true;
            Canvas.FullRedraw();
        }
    }

    private void LayerLockAlpha_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (_layerUiSilent || Canvas.Document?.ActiveLayerId is not Guid id) return;
        if (Canvas.Document.FindLayer(id) is { } layer)
        {
            if (LayerLockAlpha.IsChecked == true)
                layer.Locks |= LayerLocks.Transparency;
            else
                layer.Locks &= ~LayerLocks.Transparency;
            Canvas.Document.IsDirty = true;
        }
    }

    private void BlendCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (_layerUiSilent || Canvas.Document?.ActiveLayerId is not Guid id) return;
        if (Canvas.Document.FindLayer(id) is { } layer)
        {
            layer.Blend = IndexToBlend(BlendCombo.SelectedIndex);
            Canvas.Document.IsDirty = true;
            Canvas.FullRedraw();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        if (MaxBtn != null) MaxBtn.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private bool IsVectorContext()
    {
        var layer = Canvas?.Document?.ActiveLayer;
        return layer is VectorLayer or FrameLayer;
    }

    private void ToolCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Canvas is null || ToolCombo is null) return;
        if (ToolCombo.SelectedItem is not ComboBoxItem item) return;
        string tag = item.Tag as string ?? "";
        AppLog.Debug($"Tool -> {tag} vec={IsVectorContext()}", "UI");

        // reset option panels
        if (BrushParamsPanel != null) BrushParamsPanel.Visibility = Visibility.Collapsed;
        if (ToolOptionsPanel != null) ToolOptionsPanel.Visibility = Visibility.Collapsed;
        if (GradientOptions != null) GradientOptions.Visibility = Visibility.Collapsed;
        if (SelectOptions != null) SelectOptions.Visibility = Visibility.Collapsed;
        if (FillOptions != null) FillOptions.Visibility = Visibility.Collapsed;
        if (FrameOptions != null) FrameOptions.Visibility = Visibility.Collapsed;
        if (VectorExtraOptions != null) VectorExtraOptions.Visibility = Visibility.Collapsed;

        switch (tag)
        {
            case "select":
                Canvas.Tool = CanvasTool.Select;
                if (ToolOptionsPanel != null) ToolOptionsPanel.Visibility = Visibility.Visible;
                if (ToolOptionsTitle != null) ToolOptionsTitle.Text = SR.Get("Tool.Select");
                StatusText.Text = SR.Get("Status.SelectRuler");
                break;

            case "pencil":
            case "air":
            case "eraser":
            case "brush":
            case "water":
            case "marker":
            case "smudge":
                Canvas.Tool = CanvasTool.Brush;
                Canvas.Preset = tag switch
                {
                    "air" => BrushPreset.DefaultAirbrush(),
                    "eraser" => BrushPreset.DefaultEraser(),
                    "brush" => BrushPreset.DefaultBrush(),
                    "water" => BrushPreset.DefaultWatercolor(),
                    "marker" => BrushPreset.DefaultMarker(),
                    "smudge" => BrushPreset.DefaultSmudge(),
                    _ => BrushPreset.DefaultPencil()
                };
                Canvas.BrushSize = Canvas.Preset.Params.SizePx;
                if (BrushParamsPanel != null) BrushParamsPanel.Visibility = Visibility.Visible;
                SyncBrushParamsFromPreset();
                StatusText.Text = SR.Format("Status.Raster", LocalizeBrushName(Canvas.Preset.Name));
                break;

            case "fill":
                Canvas.Tool = CanvasTool.Fill;
                if (ToolOptionsPanel != null) ToolOptionsPanel.Visibility = Visibility.Visible;
                if (FillOptions != null) FillOptions.Visibility = Visibility.Visible;
                if (ToolOptionsTitle != null) ToolOptionsTitle.Text = SR.Get("Tool.Fill");
                StatusText.Text = SR.Get("Status.RasterFill");
                break;

            case "grad":
                Canvas.Tool = CanvasTool.Gradient;
                if (ToolOptionsPanel != null) ToolOptionsPanel.Visibility = Visibility.Visible;
                if (GradientOptions != null) GradientOptions.Visibility = Visibility.Visible;
                if (ToolOptionsTitle != null) ToolOptionsTitle.Text = SR.Get("Tool.Gradient");
                ApplyNonBrushOptions();
                StatusText.Text = SR.Get("Status.RasterGrad");
                break;

            case "selrect":
            case "lasso":
            case "wand":
                Canvas.Tool = tag switch
                {
                    "lasso" => CanvasTool.Lasso,
                    "wand" => CanvasTool.MagicWand,
                    _ => CanvasTool.RectSelect
                };
                if (ToolOptionsPanel != null) ToolOptionsPanel.Visibility = Visibility.Visible;
                if (SelectOptions != null) SelectOptions.Visibility = Visibility.Visible;
                if (ToolOptionsTitle != null) ToolOptionsTitle.Text = SR.Get("Tool.Selection");
                ApplyNonBrushOptions();
                StatusText.Text = SR.Format("Status.Raster", item.Content);
                break;

            case "vpen":
                Canvas.Tool = CanvasTool.VectorPen;
                if (Canvas.Document?.ActiveLayer is not VectorLayer)
                {
                    Canvas.Document?.AddVectorLayer(SR.Format("Layer.VectorN", (Canvas.Document?.Root.Children.Count ?? 0) + 1));
                    RefreshLayerList();
                }
                if (VectorWidthSlider != null)
                    Canvas.VectorBaseWidth = (float)VectorWidthSlider.Value;
                if (VectorQuickOpts != null) VectorQuickOpts.Visibility = Visibility.Visible;
                StatusText.Text = SR.Get("Status.VectorPen");
                break;

            case "veraser":
                Canvas.Tool = CanvasTool.VectorEraser;
                if (Canvas.Document?.ActiveLayer is not VectorLayer)
                {
                    Canvas.Document?.AddVectorLayer(SR.Format("Layer.VectorN", (Canvas.Document?.Root.Children.Count ?? 0) + 1));
                    RefreshLayerList();
                }
                if (VectorWidthSlider != null)
                    Canvas.VectorBaseWidth = (float)VectorWidthSlider.Value;
                if (VectorQuickOpts != null) VectorQuickOpts.Visibility = Visibility.Visible;
                StatusText.Text = SR.Get("Status.VectorEraser");
                break;

            case "vnode":
                Canvas.Tool = CanvasTool.VectorNode;
                if (Canvas.Document?.ActiveLayer is not VectorLayer)
                {
                    Canvas.Document?.AddVectorLayer(SR.Format("Layer.VectorN", (Canvas.Document?.Root.Children.Count ?? 0) + 1));
                    RefreshLayerList();
                }
                if (ToolOptionsPanel != null) ToolOptionsPanel.Visibility = Visibility.Visible;
                if (VectorExtraOptions != null) VectorExtraOptions.Visibility = Visibility.Visible;
                if (ToolOptionsTitle != null) ToolOptionsTitle.Text = SR.Get("Tool.VectorNode");
                StatusText.Text = SR.Get("Status.VectorNode");
                Canvas.InvalidateVisual();
                break;

            case "vfill":
                Canvas.Tool = CanvasTool.VectorCloseFill;
                if (Canvas.Document?.ActiveLayer is not VectorLayer)
                {
                    Canvas.Document?.AddVectorLayer(SR.Format("Layer.VectorN", (Canvas.Document?.Root.Children.Count ?? 0) + 1));
                    RefreshLayerList();
                }
                if (ToolOptionsPanel != null) ToolOptionsPanel.Visibility = Visibility.Visible;
                if (VectorExtraOptions != null) VectorExtraOptions.Visibility = Visibility.Visible;
                if (ToolOptionsTitle != null) ToolOptionsTitle.Text = SR.Get("Tool.VectorFill");
                StatusText.Text = SR.Get("Status.VectorFill");
                Canvas.InvalidateVisual();
                break;

            case "vspline":
                Canvas.Tool = CanvasTool.VectorSpline;
                if (Canvas.Document?.ActiveLayer is not VectorLayer)
                {
                    Canvas.Document?.AddVectorLayer(SR.Format("Layer.VectorN", (Canvas.Document?.Root.Children.Count ?? 0) + 1));
                    RefreshLayerList();
                }
                if (VectorWidthSlider != null)
                    Canvas.VectorBaseWidth = (float)VectorWidthSlider.Value;
                if (VectorQuickOpts != null) VectorQuickOpts.Visibility = Visibility.Visible;
                if (ToolOptionsPanel != null) ToolOptionsPanel.Visibility = Visibility.Visible;
                if (VectorExtraOptions != null) VectorExtraOptions.Visibility = Visibility.Visible;
                if (ToolOptionsTitle != null) ToolOptionsTitle.Text = SR.Get("Tool.VectorSpline");
                StatusText.Text = SR.Get("Status.VectorSpline");
                break;

            case "frame":
                Canvas.Tool = CanvasTool.FrameRect;
                if (Canvas.Document?.ActiveLayer is not FrameLayer)
                {
                    Canvas.Document?.AddFrameLayer(SR.Format("Layer.FrameN", (Canvas.Document?.Root.Children.Count ?? 0) + 1));
                    RefreshLayerList();
                }
                if (ToolOptionsPanel != null) ToolOptionsPanel.Visibility = Visibility.Visible;
                if (FrameOptions != null) FrameOptions.Visibility = Visibility.Visible;
                if (ToolOptionsTitle != null) ToolOptionsTitle.Text = SR.Get("Tool.Frame");
                StatusText.Text = SR.Get("Status.FrameDrag");
                AppLog.Info("Frame tool selected", "Frame");
                break;
        }
    }

    private void VectorWidth_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || Canvas is null) return;
        Canvas.VectorBaseWidth = (float)e.NewValue;
        if (VectorWidthVal != null) VectorWidthVal.Text = $"{e.NewValue:0.#}";
    }

    private void FrameWidth_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || Canvas?.Document?.ActiveLayer is not FrameLayer fl) return;
        fl.LineWidth = (float)e.NewValue;
        fl.InvalidateCache();
        Canvas.FullRedraw();
    }

    private void UpdateToolbarsForLayer()
    {
        if (!IsLoaded) return;
        bool vec = IsVectorContext();
        // Frame layer uses "vector mode" chrome (line width) but tool list includes frame
        if (ToolModeLabel != null)
            ToolModeLabel.Text = Canvas?.Document?.ActiveLayer is FrameLayer ? SR.Get("Mode.Frame")
                : vec ? SR.Get("Mode.Vector") : SR.Get("Mode.Raster");
        if (RasterQuickOpts != null)
            RasterQuickOpts.Visibility = (vec || Canvas?.Document?.ActiveLayer is FrameLayer) ? Visibility.Collapsed : Visibility.Visible;
        if (VectorQuickOpts != null)
            VectorQuickOpts.Visibility = (vec || Canvas?.Document?.ActiveLayer is FrameLayer) ? Visibility.Visible : Visibility.Collapsed;

        if (ToolCombo is null) return;
        string? prefer = null;
        if (ToolCombo.SelectedItem is ComboBoxItem cur)
            prefer = cur.Tag as string;

        _colorUiSilent = true;
        try
        {
            ToolCombo.Items.Clear();
            if (Canvas?.Document?.ActiveLayer is FrameLayer)
            {
                ToolCombo.Items.Add(new ComboBoxItem { Content = SR.Get("Tool.Select"), Tag = "select" });
                ToolCombo.Items.Add(new ComboBoxItem { Content = SR.Get("Tool.FrameRect"), Tag = "frame" });
                ToolCombo.Items.Add(new ComboBoxItem { Content = SR.Get("Tool.VectorPen"), Tag = "vpen" });
                ToolCombo.SelectedIndex = 1;
            }
            else if (vec)
            {
                ToolCombo.Items.Add(new ComboBoxItem { Content = SR.Get("Tool.Select"), Tag = "select" });
                ToolCombo.Items.Add(new ComboBoxItem { Content = SR.Get("Tool.VectorPen"), Tag = "vpen" });
                ToolCombo.Items.Add(new ComboBoxItem { Content = SR.Get("Tool.VectorEraser"), Tag = "veraser" });
                ToolCombo.Items.Add(new ComboBoxItem { Content = SR.Get("Tool.VectorNode"), Tag = "vnode" });
                ToolCombo.Items.Add(new ComboBoxItem { Content = SR.Get("Tool.VectorFill"), Tag = "vfill" });
                ToolCombo.Items.Add(new ComboBoxItem { Content = SR.Get("Tool.VectorSpline"), Tag = "vspline" });
                ToolCombo.Items.Add(new ComboBoxItem { Content = SR.Get("Tool.FrameRect"), Tag = "frame" });
                ToolCombo.SelectedIndex = 1;
                if (prefer != null)
                {
                    for (int i = 0; i < ToolCombo.Items.Count; i++)
                        if (ToolCombo.Items[i] is ComboBoxItem ci && (ci.Tag as string) == prefer)
                        { ToolCombo.SelectedIndex = i; break; }
                }
            }
            else
            {
                var items = new (string, string)[]
                {
                    ("Tool.Select","select"),
                    ("Tool.Pencil","pencil"),("Tool.Airbrush","air"),("Tool.Eraser","eraser"),("Tool.Brush","brush"),
                    ("Tool.Watercolor","water"),("Tool.Marker","marker"),("Tool.Smudge","smudge"),
                    ("Tool.FillBucket","fill"),("Tool.Gradient","grad"),("Tool.RectSelect","selrect"),
                    ("Tool.Lasso","lasso"),("Tool.MagicWand","wand")
                };
                foreach (var (key, tag) in items)
                    ToolCombo.Items.Add(new ComboBoxItem { Content = SR.Get(key), Tag = tag });
                ToolCombo.SelectedIndex = 0;
                if (prefer != null)
                {
                    for (int i = 0; i < ToolCombo.Items.Count; i++)
                        if (ToolCombo.Items[i] is ComboBoxItem ci && (ci.Tag as string) == prefer)
                        { ToolCombo.SelectedIndex = i; break; }
                }
            }
        }
        finally { _colorUiSilent = false; }

        ToolCombo_Changed(ToolCombo, null!);
    }

    private void ApplyNonBrushOptions()
    {
        if (Canvas is null) return;
        if (GradientTypeCombo != null)
            Canvas.GradientKind = GradientTypeCombo.SelectedIndex == 1 ? GradientType.Radial : GradientType.Linear;
        if (GradTransparentCheck != null)
            Canvas.GradientToTransparent = GradTransparentCheck.IsChecked == true;
        if (SelectModeCombo != null)
        {
            Canvas.SelectMode = SelectModeCombo.SelectedIndex switch
            {
                1 => Eidolon.Core.SelectionMode.Add,
                2 => Eidolon.Core.SelectionMode.Subtract,
                3 => Eidolon.Core.SelectionMode.Intersect,
                _ => Eidolon.Core.SelectionMode.Replace
            };
        }
    }

    private void GradientType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Canvas is null) return;
        Canvas.GradientKind = GradientTypeCombo.SelectedIndex == 1 ? GradientType.Radial : GradientType.Linear;
    }

    private void GradTransparent_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Canvas is null) return;
        Canvas.GradientToTransparent = GradTransparentCheck?.IsChecked == true;
    }

    private void SelectMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Canvas is null) return;
        ApplyNonBrushOptions();
    }

    private void SyncBrushParamsFromPreset()
    {
        if (HardnessSlider is null || Canvas is null) return;
        _colorUiSilent = true;
        var p = Canvas.Preset.Params;
        if (BrushSizeSlider != null) BrushSizeSlider.Value = p.SizePx;
        if (MinSizeSlider != null) MinSizeSlider.Value = p.MinSizeRatio;
        HardnessSlider.Value = p.Hardness;
        OpacitySlider.Value = p.Opacity;
        if (FlowSlider != null) FlowSlider.Value = p.Flow;
        if (SoftEdgeSlider != null) SoftEdgeSlider.Value = p.SoftEdge;
        BlendSlider.Value = p.Blend;
        if (SpacingSlider != null) SpacingSlider.Value = p.Spacing;
        if (BrushStabSlider != null)
        {
            float stab = p.StabilizerStrength > 0 ? p.StabilizerStrength : Canvas.Stabilizer;
            BrushStabSlider.Value = stab;
            Canvas.Stabilizer = stab;
        }
        if (SizeByPressureCheck != null) SizeByPressureCheck.IsChecked = p.SizeByPressure;
        if (OpacityByPressureCheck != null) OpacityByPressureCheck.IsChecked = p.OpacityByPressure;
        if (FlowByPressureCheck != null) FlowByPressureCheck.IsChecked = p.FlowByPressure;
        if (AntiAliasCheck != null) AntiAliasCheck.IsChecked = p.AntiAlias;
        if (LockAlphaBrushCheck != null) LockAlphaBrushCheck.IsChecked = p.LockAlpha;
        if (TextureSlider != null) TextureSlider.Value = p.TextureStrength;
        if (SmudgeSlider != null) SmudgeSlider.Value = p.SmudgeStrength;
        if (StraightLineBrushCheck != null) StraightLineBrushCheck.IsChecked = Canvas.StraightLineMode;
        UpdateBrushValueLabels();
        _colorUiSilent = false;
    }

    private void UpdateBrushValueLabels()
    {
        if (Canvas is null) return;
        var p = Canvas.Preset.Params;
        if (BrushSizeVal != null) BrushSizeVal.Text = $"{p.SizePx:0}";
        if (MinSizeVal != null) MinSizeVal.Text = $"{p.MinSizeRatio:0.00}";
        if (OpacityVal != null) OpacityVal.Text = $"{p.Opacity:0.00}";
        if (FlowVal != null) FlowVal.Text = $"{p.Flow:0.00}";
        if (HardnessVal != null) HardnessVal.Text = $"{p.Hardness:0.00}";
        if (SoftEdgeVal != null) SoftEdgeVal.Text = $"{p.SoftEdge:0.00}";
        if (BlendVal != null) BlendVal.Text = $"{p.Blend:0.00}";
        if (SpacingVal != null) SpacingVal.Text = $"{p.Spacing:0.00}";
        if (StabVal != null) StabVal.Text = $"{Canvas.Stabilizer:0.00}";
        if (TextureVal != null) TextureVal.Text = $"{p.TextureStrength:0.00}";
        if (SmudgeVal != null) SmudgeVal.Text = $"{p.SmudgeStrength:0.00}";
    }

    private void ApplyBrushParam(Action<BrushParameters> edit)
    {
        if (_colorUiSilent || Canvas is null) return;
        if (Canvas.Tool != CanvasTool.Brush) return; // never touch brush params for other tools
        edit(Canvas.Preset.Params);
        Canvas.BrushSize = Canvas.Preset.Params.SizePx;
        UpdateBrushValueLabels();
    }

    private void BrushSizeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyBrushParam(p => p.SizePx = (float)e.NewValue);

    private void MinSizeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyBrushParam(p => p.MinSizeRatio = (float)e.NewValue);

    private void FlowSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyBrushParam(p => p.Flow = (float)e.NewValue);

    private void SoftEdgeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyBrushParam(p => p.SoftEdge = (float)e.NewValue);

    private void SpacingSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyBrushParam(p => p.Spacing = (float)e.NewValue);

    private void BrushStabSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_colorUiSilent || Canvas is null) return;
        if (Canvas.Tool != CanvasTool.Brush) return;
        Canvas.Stabilizer = (float)e.NewValue;
        Canvas.Preset.Params.StabilizerStrength = (float)e.NewValue;
        UpdateBrushValueLabels();
    }

    private void PressureFlags_Changed(object sender, RoutedEventArgs e)
    {
        if (_colorUiSilent || Canvas is null || Canvas.Tool != CanvasTool.Brush) return;
        var p = Canvas.Preset.Params;
        p.SizeByPressure = SizeByPressureCheck?.IsChecked == true;
        p.OpacityByPressure = OpacityByPressureCheck?.IsChecked == true;
        p.FlowByPressure = FlowByPressureCheck?.IsChecked == true;
    }

    private void AntiAlias_Changed(object sender, RoutedEventArgs e)
    {
        if (_colorUiSilent || Canvas is null || Canvas.Tool != CanvasTool.Brush) return;
        Canvas.Preset.Params.AntiAlias = AntiAliasCheck?.IsChecked == true;
    }

    private void LockAlphaBrush_Changed(object sender, RoutedEventArgs e)
    {
        if (_colorUiSilent || Canvas is null || Canvas.Tool != CanvasTool.Brush) return;
        Canvas.Preset.Params.LockAlpha = LockAlphaBrushCheck?.IsChecked == true;
        Canvas.LockAlphaBrush = Canvas.Preset.Params.LockAlpha;
    }

    private void HardnessSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyBrushParam(p => p.Hardness = (float)e.NewValue);

    private void OpacitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyBrushParam(p => p.Opacity = (float)e.NewValue);

    private void BlendSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyBrushParam(p => p.Blend = (float)e.NewValue);

    private void TextureSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyBrushParam(p => p.TextureStrength = (float)e.NewValue);

    private void SmudgeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyBrushParam(p => p.SmudgeStrength = (float)e.NewValue);

    private void StraightLine_Changed(object sender, RoutedEventArgs e)
    {
        if (Canvas is null) return;
        bool on = (StraightLineCheck?.IsChecked == true) || (StraightLineBrushCheck?.IsChecked == true);
        // keep both in sync if present
        if (!_colorUiSilent)
        {
            _colorUiSilent = true;
            if (StraightLineCheck != null && !ReferenceEquals(sender, StraightLineCheck))
                StraightLineCheck.IsChecked = on;
            if (StraightLineBrushCheck != null && !ReferenceEquals(sender, StraightLineBrushCheck))
                StraightLineBrushCheck.IsChecked = on;
            _colorUiSilent = false;
        }
        Canvas.StraightLineMode = on;
    }

    private void ColorModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Fires during InitializeComponent before Canvas/Document exist.
        if (!IsLoaded || ColorModelCombo is null) return;
        // Source of truth is document RGB; never re-apply channels during model switch.
        // Changing Maximum can fire ValueChanged and corrupt FG if silent is off.
        _colorUiSilent = true;
        try
        {
            _colorModel = ColorModelCombo.SelectedIndex;
            AppLog.Debug($"Color model -> {_colorModel}", "UI");
            UpdateColorLabels();
        }
        finally
        {
            _colorUiSilent = false;
        }
        // Re-project current FG into the new model (read-only conversion)
        SyncColorUi();
    }

    private void UpdateColorLabels()
    {
        if (LabelA is null || SliderA is null) return;
        // Caller should hold _colorUiSilent when changing Maximum.
        switch (_colorModel)
        {
            case 1:
                LabelA.Text = "H"; LabelB.Text = "S"; LabelC.Text = "V";
                SliderA.Minimum = 0; SliderB.Minimum = 0; SliderC.Minimum = 0;
                SliderA.Maximum = 360; SliderB.Maximum = 100; SliderC.Maximum = 100;
                break;
            case 2:
                LabelA.Text = "H"; LabelB.Text = "S"; LabelC.Text = "L";
                SliderA.Minimum = 0; SliderB.Minimum = 0; SliderC.Minimum = 0;
                SliderA.Maximum = 360; SliderB.Maximum = 100; SliderC.Maximum = 100;
                break;
            case 3:
                LabelA.Text = "L"; LabelB.Text = "C"; LabelC.Text = "H";
                SliderA.Minimum = 0; SliderB.Minimum = 0; SliderC.Minimum = 0;
                SliderA.Maximum = 100; SliderB.Maximum = 40; SliderC.Maximum = 360;
                break;
            default:
                LabelA.Text = "R"; LabelB.Text = "G"; LabelC.Text = "B";
                SliderA.Minimum = 0; SliderB.Minimum = 0; SliderC.Minimum = 0;
                SliderA.Maximum = 255; SliderB.Maximum = 255; SliderC.Maximum = 255;
                break;
        }
    }

    private void ColorChannel_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_colorUiSilent || Canvas?.Document is null || ColorPreview is null) return;
        ChannelsToRgb(out float r, out float g, out float b);
        var col = ColorModels.FromFloatRgb(r, g, b);
        Canvas.Document.Colors.Foreground = col;
        ColorPreview.Fill = new SolidColorBrush(Color.FromRgb(col.R, col.G, col.B));
        FgSwatch.Fill = ColorPreview.Fill;
        if (ColorValueText != null) ColorValueText.Text = $"sRGB {col.R} {col.G} {col.B}";
        if (ValA != null) ValA.Text = $"{SliderA.Value:0}";
        if (ValB != null) ValB.Text = $"{SliderB.Value:0}";
        if (ValC != null) ValC.Text = $"{SliderC.Value:0}";
        UpdateColorRamps();
        UpdateMixBar();
    }

    private void UpdateColorRamps()
    {
        if (RampA is null || SliderA is null) return;
        int mode = _colorModel;
        RampA.Mode = mode; RampB.Mode = mode; RampC.Mode = mode;
        RampA.Channel = 0; RampB.Channel = 1; RampC.Channel = 2;
        double a = SliderA.Value, b = SliderB.Value, cch = SliderC.Value;
        RampA.V0 = a; RampA.V1 = b; RampA.V2 = cch;
        RampB.V0 = a; RampB.V1 = b; RampB.V2 = cch;
        RampC.V0 = a; RampC.V1 = b; RampC.V2 = cch;
        RampA.Rebuild(); RampB.Rebuild(); RampC.Rebuild();
    }

    private void ChannelsToRgb(out float r, out float g, out float b)
    {
        switch (_colorModel)
        {
            case 1:
                ColorModels.HsvToRgb((float)(SliderA.Value / 360.0), (float)(SliderB.Value / 100.0), (float)(SliderC.Value / 100.0), out r, out g, out b);
                break;
            case 2:
                ColorModels.HslToRgb((float)(SliderA.Value / 360.0), (float)(SliderB.Value / 100.0), (float)(SliderC.Value / 100.0), out r, out g, out b);
                break;
            case 3:
                ColorModels.OklchToRgb((float)(SliderA.Value / 100.0), (float)(SliderB.Value / 100.0), (float)(SliderC.Value * Math.PI / 180.0), out r, out g, out b);
                break;
            default:
                r = (float)(SliderA.Value / 255.0);
                g = (float)(SliderB.Value / 255.0);
                b = (float)(SliderC.Value / 255.0);
                break;
        }
    }

    private void FgSwatch_Click(object sender, MouseButtonEventArgs e) { }

    private void BgSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (Canvas?.Document is null) return;
        Canvas.Document.Colors.Swap();
        SyncColorUi();
    }

    private void SyncColorUi()
    {
        if (Canvas?.Document is null || SliderA is null || ColorPreview is null || FgSwatch is null || BgSwatch is null) return;
        _colorUiSilent = true;
        try
        {
        var fg = Canvas.Document.Colors.Foreground;
        var bg = Canvas.Document.Colors.Background;
        // Display only: convert document sRGB -> current model sliders. Never write back FG here.
        float r = fg.R / 255f, g = fg.G / 255f, b = fg.B / 255f;
        switch (_colorModel)
        {
            case 1:
                ColorModels.RgbToHsv(r, g, b, out float h1, out float s1, out float v1);
                SetSliderSafe(SliderA, h1 * 360);
                SetSliderSafe(SliderB, s1 * 100);
                SetSliderSafe(SliderC, v1 * 100);
                break;
            case 2:
                ColorModels.RgbToHsl(r, g, b, out float h2, out float s2, out float l2);
                SetSliderSafe(SliderA, h2 * 360);
                SetSliderSafe(SliderB, s2 * 100);
                SetSliderSafe(SliderC, l2 * 100);
                break;
            case 3:
                ColorModels.RgbToOklch(r, g, b, out float L, out float Cch, out float H);
                SetSliderSafe(SliderA, L * 100);
                SetSliderSafe(SliderB, Math.Min(Cch * 100.0, SliderB.Maximum));
                SetSliderSafe(SliderC, H * 180.0 / Math.PI);
                break;
            default:
                SetSliderSafe(SliderA, fg.R);
                SetSliderSafe(SliderB, fg.G);
                SetSliderSafe(SliderC, fg.B);
                break;
        }
        ColorPreview.Fill = new SolidColorBrush(Color.FromRgb(fg.R, fg.G, fg.B));
        FgSwatch.Fill = ColorPreview.Fill;
        BgSwatch.Fill = new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B));
        if (ColorValueText != null) ColorValueText.Text = $"sRGB {fg.R} {fg.G} {fg.B}";
        if (ValA != null) ValA.Text = $"{SliderA.Value:0.#}";
        if (ValB != null) ValB.Text = $"{SliderB.Value:0.#}";
        if (ValC != null) ValC.Text = $"{SliderC.Value:0.#}";
        UpdateColorRamps();
        UpdateMixBar();
        }
        finally
        {
            _colorUiSilent = false;
        }
    }

    private static void SetSliderSafe(Slider s, double value)
    {
        if (s is null) return;
        double min = s.Minimum, max = s.Maximum;
        if (max < min) (min, max) = (max, min);
        s.Value = Math.Clamp(value, min, max);
    }


    // ========== M4 Color UX ==========















    private void UpdateMixBar()
    {
        if (MixBar is null || Canvas?.Document is null) return;
        var fg = Canvas.Document.Colors.Foreground;
        var bg = Canvas.Document.Colors.Background;
        var g = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(fg.R, fg.G, fg.B), 0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(bg.R, bg.G, bg.B), 1));
        MixBar.Fill = g;
    }

    private void MixBar_Click(object sender, MouseButtonEventArgs e) => SampleMixBar(e);
    private void MixBar_Move(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) SampleMixBar(e);
    }

    private void SampleMixBar(MouseEventArgs e)
    {
        if (MixBar is null || Canvas?.Document is null) return;
        var pos = e.GetPosition(MixBar);
        double t = MixBar.ActualWidth <= 1 ? 0 : Math.Clamp(pos.X / MixBar.ActualWidth, 0, 1);
        var fg = Canvas.Document.Colors.Foreground;
        var bg = Canvas.Document.Colors.Background;
        // sample mix without changing ends permanently: set FG to mix
        // use BG and previous FG from swatches: actually ends are FG/BG, result becomes FG
        // Keep ends: re-read current FG/BG then lerp — but FG changes each time would crawl.
        // Store ends from ColorState: use FG as A, BG as B, result to FG is ok for click; for drag crawl is expected like SAI mix.
        byte r = (byte)(fg.R * (1 - t) + bg.R * t + 0.5);
        byte g = (byte)(fg.G * (1 - t) + bg.G * t + 0.5);
        byte b = (byte)(fg.B * (1 - t) + bg.B * t + 0.5);
        // Better: mix fixed ends captured at mouse down - simple version: mix current FG/BG
        // Use palette of ends: left=FG right=BG from before apply
        // Fix crawl: read BG and a "mixLeft" - for simplicity mix using history of ends stored on panel Tag
        if (MixBar.Tag is not ColorRgba8[])
        {
            MixBar.Tag = new[] { fg, bg };
        }
        if (e.LeftButton != MouseButtonState.Pressed)
            MixBar.Tag = new[] { fg, bg };
        var ends = (ColorRgba8[])MixBar.Tag;
        // refresh ends when not dragging start
        var a = ends[0]; var bb = ends[1];
        r = (byte)(a.R * (1 - t) + bb.R * t + 0.5);
        g = (byte)(a.G * (1 - t) + bb.G * t + 0.5);
        b = (byte)(a.B * (1 - t) + bb.B * t + 0.5);
        Canvas.Document.Colors.Foreground = new ColorRgba8(r, g, b, 255);
        SyncColorUi();
    }




    private void About_Click(object sender, RoutedEventArgs e)
    {
        ThemedMessageWindow.Show(this, SR.Get("App.AboutTitle"), SR.Get("App.AboutBody"), UiMessageKind.Info);
    }

    private static string LocalizeBrushName(string name) => name switch
    {
        "Pencil" or "铅笔" => SR.Get("Tool.Pencil"),
        "Eraser" or "橡皮" => SR.Get("Tool.Eraser"),
        "Airbrush" or "喷枪" => SR.Get("Tool.Airbrush"),
        "Brush" or "画笔" => SR.Get("Tool.Brush"),
        "Watercolor" or "水彩" => SR.Get("Tool.Watercolor"),
        "Marker" or "马克笔" => SR.Get("Tool.Marker"),
        "Smudge" or "涂抹" => SR.Get("Tool.Smudge"),
        _ => name
    };

    private void BindHistory(Document? doc)
    {
        if (_historyBound is not null)
            _historyBound.OperationPushed -= OnOperationPushed;
        _historyBound = doc?.History;
        if (_historyBound is not null)
            _historyBound.OperationPushed += OnOperationPushed;
    }

    private void OnOperationPushed(object? sender, EventArgs e)
    {
        if (_timelapse.IsRecording && Canvas.Document is not null)
            _timelapse.CaptureFrame(Canvas.Document);
    }

    /// <summary>Call BEFORE applying a discrete ruler change, returns snapshot.</summary>
    private RulerState? CaptureRulerBefore()
    {
        return Canvas?.Document?.Rulers.Clone();
    }

    /// <summary>Call AFTER applying a discrete ruler change, pushes undo.</summary>
    private void PushRulerUndo(RulerState before)
    {
        if (Canvas?.Document is null || before is null) return;
        var after = Canvas.Document.Rulers.Clone();
        Canvas.Document.History.PushAlreadyDone(new RulerEditCommand(before, after), Canvas.Document);
    }

    private void RulerKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Canvas?.Document is null) return;
        if (RulerKindCombo.SelectedItem is not ComboBoxItem item) return;
        string tag = item.Tag as string ?? "None";
        var kind = tag switch
        {
            "Straight" => RulerKind.Straight,
            "Ellipse" => RulerKind.Ellipse,
            "Symmetry" => RulerKind.Symmetry,
            "VanishingPoint" => RulerKind.VanishingPoint,
            "Perspective1" => RulerKind.Perspective1,
            "Perspective2" => RulerKind.Perspective2,
            "Perspective3" => RulerKind.Perspective3,
            "Fisheye6" => RulerKind.Fisheye6,
            _ => RulerKind.None
        };
        var before = CaptureRulerBefore();
        Canvas.Document.Rulers.Kind = kind;
        PushRulerUndo(before!);
        // Show line-snap toggles only for perspective-type rulers
        bool isPersp = kind is RulerKind.Perspective1 or RulerKind.Perspective2
            or RulerKind.Perspective3 or RulerKind.Fisheye6;
        if (RulerLineToggles is not null)
            RulerLineToggles.Visibility = isPersp ? Visibility.Visible : Visibility.Collapsed;
        if (isPersp)
        {
            var r = Canvas.Document.Rulers;
            if (RulerLine0Check is not null) RulerLine0Check.IsChecked = r.PerspectiveLine0Enabled;
            if (RulerLine1Check is not null) RulerLine1Check.IsChecked = r.PerspectiveLine1Enabled;
            if (RulerLine2Check is not null) RulerLine2Check.IsChecked = r.PerspectiveLine2Enabled;
        }
        bool isFish = kind == RulerKind.Fisheye6;
        if (RulerPModeCombo is not null)
            RulerPModeCombo.Visibility = isFish ? Visibility.Visible : Visibility.Collapsed;
        if (isFish && RulerPModeCombo is not null)
            RulerPModeCombo.SelectedIndex = (int)Canvas.Document.Rulers.FisheyePMode;
        Canvas.InvalidateVisual();
    }

    private void RulerLineSnap_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Canvas?.Document is null) return;
        var before = CaptureRulerBefore();
        var r = Canvas.Document.Rulers;
        if (RulerLine0Check is not null) r.PerspectiveLine0Enabled = RulerLine0Check.IsChecked == true;
        if (RulerLine1Check is not null) r.PerspectiveLine1Enabled = RulerLine1Check.IsChecked == true;
        if (RulerLine2Check is not null) r.PerspectiveLine2Enabled = RulerLine2Check.IsChecked == true;
        PushRulerUndo(before!);
    }

    private void RulerPMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Canvas?.Document is null || RulerPModeCombo is null) return;
        var before = CaptureRulerBefore();
        Canvas.Document.Rulers.FisheyePMode = (FisheyePMode)RulerPModeCombo.SelectedIndex;
        PushRulerUndo(before!);
        Canvas.InvalidateVisual();
    }

    private void RulerFlags_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Canvas?.Document is null) return;
        var before = CaptureRulerBefore();
        Canvas.Document.Rulers.Visible = RulerVisibleCheck.IsChecked == true;
        PushRulerUndo(before!);
        Canvas.UpdateRulerPreviewFromUi();
        Canvas.InvalidateVisual();
    }

    private void RulerSnap_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || Canvas?.Document is null) return;
        var before = CaptureRulerBefore();
        Canvas.Document.Rulers.SnapStrength = (float)e.NewValue;
        Canvas.Document.Rulers.SnapEnabled = e.NewValue > 0.001;
        Canvas.Document.Rulers.ForceSnap = e.NewValue >= RulerSnapSlider.Maximum - 0.001;
        PushRulerUndo(before!);
        if (RulerSnapVal != null) RulerSnapVal.Text = $"{e.NewValue:0}";
    }

    private void RulerReset_Click(object sender, RoutedEventArgs e)
    {
        if (Canvas?.Document is null) return;
        var before = CaptureRulerBefore();
        Canvas.Document.Rulers.ResetForDocument(Canvas.Document.Width, Canvas.Document.Height);
        PushRulerUndo(before!);
        Canvas.InvalidateVisual();
    }

    private void TimelapseBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            ValidateNames = false,
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Folder"
        };
        if (dlg.ShowDialog(this) == true)
        {
            string dir = System.IO.Path.GetDirectoryName(dlg.FileName) ?? "";
            if (string.IsNullOrEmpty(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            TimelapseDirText.Text = dir;
        }
    }

    private void TimelapseFps_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        TimelapseFpsVal.Text = $"{e.NewValue:0}";
    }

    private async void TimelapseToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_timelapse.IsEncoding) return;

        if (_timelapse.IsRecording)
        {
            TimelapseToggleBtn.IsEnabled = false;
            TimelapseStatusText.Text = SR.Get("Timelapse.StatusEncoding");
            string result = await _timelapse.StopAsync();
            TimelapseToggleBtn.Content = SR.Get("Timelapse.Start");
            TimelapseToggleBtn.IsEnabled = true;
            if (string.IsNullOrEmpty(result))
                TimelapseStatusText.Text = _timelapse.LastError;
            else if (_timelapse.FfmpegFound && result.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                TimelapseStatusText.Text = SR.Format("Timelapse.StatusDone", result);
            else
                TimelapseStatusText.Text = SR.Format("Timelapse.StatusNoFfmpeg", result);
            return;
        }

        string dir = TimelapseDirText.Text.Trim();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            ThemedMessageWindow.Show(this, SR.Get("Timelapse.Title"), SR.Get("Timelapse.NeedDir"));
            return;
        }
        string name = TimelapseFileNameText.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = "timelapse";
        int fps = (int)TimelapseFpsSlider.Value;
        if (Canvas?.Document is null) return;

        _timelapse.Start(Canvas.Document, dir, name, fps);
        TimelapseToggleBtn.Content = SR.Get("Timelapse.Stop");
        TimelapseStatusText.Text = SR.Format("Timelapse.StatusRecording", 0);
        _ = UpdateTimelapseStatusLoop();
    }

    private async Task UpdateTimelapseStatusLoop()
    {
        while (_timelapse.IsRecording)
        {
            await Task.Delay(500);
            if (!_timelapse.IsRecording) break;
            TimelapseStatusText.Text = SR.Format("Timelapse.StatusRecording", _timelapse.FrameCount);
        }
    }

    private void UpdateTitle()
    {
        string name = _currentPath is null ? SR.Get("Common.Unnamed") : System.IO.Path.GetFileName(_currentPath);
        string dirty = Canvas.Document?.IsDirty == true ? "*" : "";
        Title = $"{dirty}{name} — Eidolon {SR.Get("App.Version")}";
    }

    private sealed class LayerItem
    {
        public LayerItem(LayerNode layer) => Layer = layer;
        public LayerNode Layer { get; }
        public override string ToString()
        {
            string v = Layer.Visible ? "" : SR.Get("Layer.Hidden");
            string c = Layer.ClippedToBelow ? SR.Get("Layer.Clipped") : "";
            string k = Layer.Kind switch
            {
                LayerKind.Vector => SR.Get("Layer.KindVector"),
                LayerKind.Text => SR.Get("Layer.KindText"),
                LayerKind.Frame => SR.Get("Layer.KindFrame"),
                LayerKind.Group => SR.Get("Layer.KindGroup"),
                _ => ""
            };
            return $"{v}{k}{Layer.Name}{c}";
        }
    }
}
