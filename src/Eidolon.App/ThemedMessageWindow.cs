using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Eidolon.App;

public enum UiMessageKind { Info, Warn, Error, Question }

public class ThemedMessageWindow : Window
{
    private bool? _result;

    private ThemedMessageWindow(string title, string message, bool okOnly)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;
        FontSize = 12;

        var bgBrush = TryFindResource("Eid.Bg") as System.Windows.Media.Brush ?? Brushes.White;
        var borderBrush = TryFindResource("Eid.Border") as System.Windows.Media.Brush ?? Brushes.Black;
        var fgBrush = TryFindResource("Eid.Text") as System.Windows.Media.Brush ?? Brushes.Black;

        var border = new Border
        {
            Background = bgBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(2),
            MinWidth = 260,
            MaxWidth = 480,
            SnapsToDevicePixels = true
        };

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(new TextBlock { Text = title, FontFamily = new FontFamily("Georgia, serif"), FontSize = 14,
            Foreground = fgBrush, Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap,
            Foreground = fgBrush, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 18) });

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = Localization.SR.Get("Common.Ok"), MinWidth = 80, Height = 28, Margin = new Thickness(6, 0, 0, 0) };
        okBtn.Click += (_, _) => { _result = true; Close(); };
        btnPanel.Children.Add(okBtn);
        if (!okOnly)
        {
            var cancelBtn = new Button { Content = Localization.SR.Get("Common.Cancel"), MinWidth = 80, Height = 28, Margin = new Thickness(6, 0, 0, 0) };
            cancelBtn.Click += (_, _) => { _result = false; Close(); };
            btnPanel.Children.Add(cancelBtn);
        }
        root.Children.Add(btnPanel);
        border.Child = root;
        Content = border;
    }

    public static void Show(Window? owner, string title, string message, UiMessageKind kind = UiMessageKind.Info) =>
        new ThemedMessageWindow(title, message, true) { Owner = owner }.ShowDialog();

    public static bool ShowQuestion(Window? owner, string title, string message)
    {
        var w = new ThemedMessageWindow(title, message, false) { Owner = owner };
        w.ShowDialog();
        return w._result == true;
    }
}
