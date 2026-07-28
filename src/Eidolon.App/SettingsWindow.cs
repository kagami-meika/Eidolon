using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Eidolon.App.Localization;
using Eidolon.App.Logging;
using Microsoft.Win32;

namespace Eidolon.App;

public sealed class SettingsWindow : Window
{
    private readonly TextBox _wBox;
    private readonly TextBox _hBox;
    private readonly ComboBox _colorModel;
    private readonly CheckBox _tlEnable;
    private readonly TextBox _tlDir;
    private readonly TextBox _tlName;
    private readonly Slider _tlFps;
    private readonly TextBlock _tlFpsVal;
    private readonly CheckBox _jpegCompress;
    private readonly Slider _jpegQuality;
    private readonly TextBlock _jpegQualityVal;
    private readonly CheckBox _webpLossless;
    private readonly Slider _webpQuality;
    private readonly TextBlock _webpQualityVal;
    private readonly CheckBox _exportAlpha;
    private readonly ComboBox _language;
    private readonly ComboBox _logLevel;
    private readonly CheckBox _willowOverlap;

    public SettingsWindow()
    {
        Title = SR.Get("Settings.Title");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Background = TryFindResource("Eid.Bg") as System.Windows.Media.Brush ?? Brushes.White;
        Foreground = TryFindResource("Eid.Text") as System.Windows.Media.Brush ?? Brushes.Black;
        FontSize = 12;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        var borderBrush = TryFindResource("Eid.Border") as System.Windows.Media.Brush ?? Brushes.Black;
        var outer = new Border { BorderBrush = borderBrush, BorderThickness = new Thickness(1), Background = Background };
        var root = new DockPanel();

        // title
        var titleBar = new Border
        {
            Height = 40,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Background
        };
        var tg = new Grid();
        tg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tg.Children.Add(new TextBlock
        {
            Text = SR.Get("Settings.Title"),
            FontFamily = new FontFamily("Georgia, Cambria, Times New Roman, serif"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        });
        var close = new Button
        {
            Content = "\u2715",
            Style = (Style)FindResource("TitleBarCloseButton")
        };
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => { DialogResult = false; Close(); };
        Grid.SetColumn(close, 1);
        tg.Children.Add(close);
        titleBar.Child = tg;
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);

        var body = new StackPanel { Margin = new Thickness(16) };
        var s = AppSettings.Current;

        // Canvas defaults
        body.Children.Add(Section(SR.Get("Settings.Canvas")));
        body.Children.Add(Label(SR.Get("Settings.DefaultWidth")));
        _wBox = Field(s.DefaultCanvasWidth.ToString());
        body.Children.Add(_wBox);
        body.Children.Add(Label(SR.Get("Settings.DefaultHeight")));
        _hBox = Field(s.DefaultCanvasHeight.ToString());
        body.Children.Add(_hBox);

        // Color model
        body.Children.Add(Section(SR.Get("Settings.Color")));
        body.Children.Add(Label(SR.Get("Settings.ColorModel")));
        _colorModel = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
        _colorModel.Items.Add(new ComboBoxItem { Content = "RGB", Tag = 0 });
        _colorModel.Items.Add(new ComboBoxItem { Content = "HSV", Tag = 1 });
        _colorModel.Items.Add(new ComboBoxItem { Content = "HSL", Tag = 2 });
        _colorModel.Items.Add(new ComboBoxItem { Content = "OKLCH", Tag = 3 });
        _colorModel.SelectedIndex = Math.Clamp(s.DefaultColorModel, 0, 3);
        body.Children.Add(_colorModel);

        // Language (cold-switch)
        body.Children.Add(Section(SR.Get("Settings.Language")));
        _language = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
        _language.Items.Add(new ComboBoxItem { Content = "中文", Tag = "cn" });
        _language.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        foreach (ComboBoxItem item in _language.Items)
        {
            if ((string)item.Tag == (s.Language ?? "cn"))
            { _language.SelectedItem = item; break; }
        }
        _language.SelectionChanged += (_, _) =>
        {
            if (_language.SelectedItem is ComboBoxItem sel)
            {
                var lang = (string)sel.Tag;
                if (lang != AppSettings.Current.Language)
                {
                    AppSettings.Current.Language = lang;
                    AppSettings.Save();
                }
            }
        };
        body.Children.Add(_language);

        // Log level (cold-switch)
        body.Children.Add(Section(SR.Get("Settings.LogLevel")));
        _logLevel = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
        _logLevel.Items.Add(new ComboBoxItem { Content = SR.Get("LogLevel.Info"), Tag = "Info" });
        _logLevel.Items.Add(new ComboBoxItem { Content = SR.Get("LogLevel.Debug"), Tag = "Debug" });
        _logLevel.Items.Add(new ComboBoxItem { Content = SR.Get("LogLevel.Trace"), Tag = "Trace" });
        _logLevel.Items.Add(new ComboBoxItem { Content = SR.Get("LogLevel.Warn"), Tag = "Warn" });
        _logLevel.Items.Add(new ComboBoxItem { Content = SR.Get("LogLevel.Error"), Tag = "Error" });
        foreach (ComboBoxItem item in _logLevel.Items)
        {
            if ((string)item.Tag == (s.LogLevel ?? "Info"))
            { _logLevel.SelectedItem = item; break; }
        }
        _logLevel.SelectionChanged += (_, _) =>
        {
            if (_logLevel.SelectedItem is ComboBoxItem sel && Enum.TryParse<LogLevel>((string)sel.Tag, true, out var lv))
            {
                AppSettings.Current.LogLevel = lv.ToString();
                AppSettings.Save();
            }
        };
        body.Children.Add(_logLevel);

        // Brush global settings
        body.Children.Add(Section(SR.Get("Settings.Brush")));
        _willowOverlap = new CheckBox
        {
            Content = SR.Get("Settings.WillowOverlap"),
            IsChecked = s.WillowOverlap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        body.Children.Add(_willowOverlap);

        // Timelapse
        body.Children.Add(Section(SR.Get("Settings.Timelapse")));
        _tlEnable = new CheckBox
        {
            Content = SR.Get("Settings.TimelapseEnable"),
            IsChecked = s.TimelapseEnabled,
            Margin = new Thickness(0, 0, 0, 8)
        };
        body.Children.Add(_tlEnable);
        body.Children.Add(Label(SR.Get("Settings.TimelapseDir")));
        var dirRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _tlDir = Field(s.TimelapseDirectory);
        dirRow.Children.Add(_tlDir);
        var browse = new Button { Content = SR.Get("Timelapse.Browse"), Height = 28, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) =>
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
                string dir = Path.GetDirectoryName(dlg.FileName) ?? "";
                if (!string.IsNullOrEmpty(dir)) _tlDir.Text = dir;
            }
        };
        Grid.SetColumn(browse, 1);
        dirRow.Children.Add(browse);
        body.Children.Add(dirRow);
        body.Children.Add(Label(SR.Get("Settings.TimelapseFile")));
        _tlName = Field(s.TimelapseFileName);
        body.Children.Add(_tlName);

        var fpsRow = new Grid();
        fpsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fpsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fpsRow.Children.Add(Label(SR.Get("Settings.TimelapseFps")));
        _tlFpsVal = new TextBlock { Text = s.TimelapseFps.ToString(), Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_tlFpsVal, 1);
        fpsRow.Children.Add(_tlFpsVal);
        body.Children.Add(fpsRow);
        _tlFps = new Slider { Minimum = 1, Maximum = 60, Value = s.TimelapseFps, Margin = new Thickness(0, 0, 0, 8) };
        _tlFps.ValueChanged += (_, e) => _tlFpsVal.Text = $"{e.NewValue:0}";
        body.Children.Add(_tlFps);

        // Export
        body.Children.Add(Section(SR.Get("Settings.Export")));
        _exportAlpha = new CheckBox
        {
            Content = SR.Get("Settings.ExportAlpha"),
            IsChecked = s.ExportPreserveTransparency,
            Margin = new Thickness(0, 0, 0, 8)
        };
        body.Children.Add(_exportAlpha);
        _jpegCompress = new CheckBox
        {
            Content = SR.Get("Settings.JpegCompress"),
            IsChecked = s.JpegCompress,
            Margin = new Thickness(0, 0, 0, 4)
        };
        body.Children.Add(_jpegCompress);
        var jqRow = new Grid();
        jqRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        jqRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        jqRow.Children.Add(Label(SR.Get("Settings.JpegQuality")));
        _jpegQualityVal = new TextBlock { Text = s.JpegQuality.ToString(), Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_jpegQualityVal, 1);
        jqRow.Children.Add(_jpegQualityVal);
        body.Children.Add(jqRow);
        _jpegQuality = new Slider { Minimum = 1, Maximum = 100, Value = s.JpegQuality, Margin = new Thickness(0, 0, 0, 8) };
        _jpegQuality.ValueChanged += (_, e) => _jpegQualityVal.Text = $"{e.NewValue:0}";
        body.Children.Add(_jpegQuality);

        _webpLossless = new CheckBox
        {
            Content = SR.Get("Settings.WebpLossless"),
            IsChecked = s.WebpLossless,
            Margin = new Thickness(0, 0, 0, 4)
        };
        body.Children.Add(_webpLossless);
        var wqRow = new Grid();
        wqRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        wqRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        wqRow.Children.Add(Label(SR.Get("Settings.WebpQuality")));
        _webpQualityVal = new TextBlock { Text = s.WebpQuality.ToString(), Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_webpQualityVal, 1);
        wqRow.Children.Add(_webpQualityVal);
        body.Children.Add(wqRow);
        _webpQuality = new Slider { Minimum = 1, Maximum = 100, Value = s.WebpQuality, Margin = new Thickness(0, 0, 0, 8) };
        _webpQuality.ValueChanged += (_, e) => _webpQualityVal.Text = $"{e.NewValue:0}";
        body.Children.Add(_webpQuality);
        body.Children.Add(new TextBlock
        {
            Text = SR.Get("Settings.WebpHint"),
            Opacity = 0.45,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // buttons
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = new Button { Content = SR.Get("Common.Cancel"), MinWidth = 80, Padding = new Thickness(14, 7, 14, 7), IsCancel = true };
        var ok = new Button { Content = SR.Get("Common.Ok"), MinWidth = 80, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(_wBox.Text, out int w) || w < 1 || w > 100000)
            {
                ThemedMessageWindow.Show(this, SR.Get("Settings.Title"), SR.Get("NewDoc.WidthInvalid"), UiMessageKind.Warn);
                return;
            }
            if (!int.TryParse(_hBox.Text, out int h) || h < 1 || h > 100000)
            {
                ThemedMessageWindow.Show(this, SR.Get("Settings.Title"), SR.Get("NewDoc.HeightInvalid"), UiMessageKind.Warn);
                return;
            }
            var next = new AppSettings
            {
                DefaultCanvasWidth = w,
                DefaultCanvasHeight = h,
                DefaultColorModel = _colorModel.SelectedIndex,
                TimelapseEnabled = _tlEnable.IsChecked == true,
                TimelapseDirectory = _tlDir.Text.Trim(),
                TimelapseFileName = string.IsNullOrWhiteSpace(_tlName.Text) ? "timelapse" : _tlName.Text.Trim(),
                TimelapseFps = (int)_tlFps.Value,
                Language = AppSettings.Current.Language,
                JpegCompress = _jpegCompress.IsChecked == true,
                JpegQuality = (int)_jpegQuality.Value,
                WebpLossless = _webpLossless.IsChecked == true,
                WebpQuality = (int)_webpQuality.Value,
                ExportPreserveTransparency = _exportAlpha.IsChecked == true,
                WillowOverlap = _willowOverlap.IsChecked == true,
                Brush = AppSettings.Current.Brush,
                Colors = AppSettings.Current.Colors,
                LogLevel = AppSettings.Current.LogLevel
            };
            AppSettings.Save(next);
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);

        root.Children.Add(body);
        outer.Child = root;
        Content = outer;
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Georgia, Cambria, Times New Roman, serif"),
        FontSize = 13,
        Margin = new Thickness(0, 4, 0, 8)
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Opacity = 0.55,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static TextBox Field(string text)
    {
        var tb = new TextBox { Text = text, Height = 28, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 8) };
        ImeInput.ConfigureForTextInput(tb);
        return tb;
    }
}
