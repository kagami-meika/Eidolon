using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Eidolon.App.Localization;

namespace Eidolon.App;

public sealed class ShortcutSettingsWindow : Window
{
    private readonly Dictionary<string, TextBlock> _bindingLabels = new();
    private string? _listeningId;
    private TextBlock? _listeningLabel;

    public ShortcutSettingsWindow()
    {
        Title = SR.Get("Shortcut.Title");
        Width = 520;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Background = TryFindResource("Eid.Bg") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White;
        Foreground = TryFindResource("Eid.Text") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black;
        FontSize = 12;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        var borderBrush = TryFindResource("Eid.Border") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black;
        var outer = new Border { BorderBrush = borderBrush, BorderThickness = new Thickness(1), Background = Background };
        var root = new DockPanel();

        // Title bar
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
            Text = SR.Get("Shortcut.Title"),
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

        // Hint
        var hint = new TextBlock
        {
            Text = SR.Get("Shortcut.Hint"),
            Opacity = 0.55,
            FontSize = 11,
            Margin = new Thickness(14, 8, 14, 4)
        };
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        // Scrollable list
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(8, 0, 8, 8)
        };
        var list = new StackPanel();
        scroll.Content = list;

        var s = AppSettings.Current;
        s.Shortcuts ??= new Dictionary<string, string>();

        string? lastCat = null;
        foreach (var entry in ShortcutRegistry.Entries)
        {
            if (entry.Category != lastCat)
            {
                lastCat = entry.Category;
                list.Children.Add(new TextBlock
                {
                    Text = SR.Get("Panel." + entry.Category),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(6, 10, 0, 2),
                    Opacity = 0.7
                });
            }

            var row = new Grid
            {
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameLabel = new TextBlock
            {
                Text = LocalizeCommandName(entry),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(nameLabel);

            var bindLabel = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Background = TryFindResource("Eid.Bg") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White,
                Opacity = 0.85
            };
            s.Shortcuts.TryGetValue(entry.Id, out var current);
            bindLabel.Text = string.IsNullOrWhiteSpace(current) ? SR.Get("Shortcut.Unbound") : current;
            bindLabel.Cursor = Cursors.Hand;
            bindLabel.MouseDown += (_, _) => StartListening(entry.Id, bindLabel);
            Grid.SetColumn(bindLabel, 1);
            row.Children.Add(bindLabel);

            var clearBtn = new Button
            {
                Content = "\u2715",
                Width = 26,
                Height = 22,
                FontSize = 10,
                Padding = new Thickness(0),
                ToolTip = SR.Get("Shortcut.Clear")
            };
            clearBtn.Click += (_, _) => ClearBinding(entry.Id, bindLabel);
            Grid.SetColumn(clearBtn, 2);
            row.Children.Add(clearBtn);

            _bindingLabels[entry.Id] = bindLabel;
            list.Children.Add(row);
        }

        root.Children.Add(scroll);

        // Bottom buttons
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(14, 0, 14, 12)
        };
        var resetAll = new Button
        {
            Content = SR.Get("Shortcut.ResetAll"),
            MinWidth = 80,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        resetAll.Click += (_, _) =>
        {
            AppSettings.Current.Shortcuts.Clear();
            foreach (var (id, label) in _bindingLabels)
                label.Text = SR.Get("Shortcut.Unbound");
        };
        buttons.Children.Add(resetAll);

        var ok = new Button
        {
            Content = SR.Get("Common.Ok"),
            MinWidth = 80,
            Padding = new Thickness(14, 7, 14, 7),
            IsDefault = true
        };
        ok.Click += (_, _) => { AppSettings.Save(); DialogResult = true; };
        buttons.Children.Add(ok);

        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        outer.Child = root;
        Content = outer;

        PreviewKeyDown += OnWindowKeyDown;
    }

    private static string LocalizeCommandName(ShortcutEntry entry)
    {
        var key = entry.NameKey;
        // Try direct localization first
        var name = SR.Get(key);
        if (name != key) return name;
        // Fallback: use the ID
        return entry.Id;
    }

    private void StartListening(string id, TextBlock label)
    {
        if (_listeningId == id) return; // already listening
        _listeningId = id;
        _listeningLabel = label;
        label.Text = SR.Get("Shortcut.Listening");
        label.FontWeight = FontWeights.Bold;
        label.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4));
    }

    private void StopListening()
    {
        if (_listeningLabel is not null)
        {
            _listeningLabel.FontWeight = FontWeights.Normal;
            _listeningLabel.Foreground = Foreground;
        }
        _listeningId = null;
        _listeningLabel = null;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_listeningId is null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Escape or Back with no modifier = cancel / clear
        if (key == Key.Escape)
        {
            StopListening();
            e.Handled = true;
            return;
        }

        // Ignore modifier-only presses
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        var mods = Keyboard.Modifiers;
        var gesture = ShortcutRegistry.FormatGesture(key, mods);

        // Apply binding
        AppSettings.Current.Shortcuts[_listeningId] = gesture;
        if (_listeningLabel is not null)
            _listeningLabel.Text = gesture;

        StopListening();
        e.Handled = true;
    }

    private void ClearBinding(string id, TextBlock label)
    {
        AppSettings.Current.Shortcuts.Remove(id);
        label.Text = SR.Get("Shortcut.Unbound");
    }
}
