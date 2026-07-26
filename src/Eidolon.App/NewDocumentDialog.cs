using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Eidolon.App.Localization;
using Eidolon.Core;

namespace Eidolon.App;

public partial class NewDocumentDialog : Window
{
    public int DocWidth { get; private set; } = AppSettings.Current.DefaultCanvasWidth;
    public int DocHeight { get; private set; } = AppSettings.Current.DefaultCanvasHeight;
    public DocumentBackgroundKind BackgroundKind { get; private set; } = DocumentBackgroundKind.White;

    public NewDocumentDialog()
    {
        Title = SR.Get("NewDoc.Title");
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Background = TryFindResource("Eid.Bg") as System.Windows.Media.Brush ?? new SolidColorBrush(Color.FromRgb(0xF9, 0xF8, 0xF6));
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
            Text = SR.Get("NewDoc.Header"),
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
        body.Children.Add(new TextBlock { Text = SR.Get("NewDoc.Width"), Opacity = 0.55, Margin = new Thickness(0, 0, 0, 4) });
        var wBox = new TextBox { Text = DocWidth.ToString(), Height = 28, Padding = new Thickness(6, 4, 6, 4) };
        ImeInput.ConfigureForTextInput(wBox);
        body.Children.Add(wBox);
        body.Children.Add(new TextBlock { Text = SR.Get("NewDoc.Height"), Opacity = 0.55, Margin = new Thickness(0, 10, 0, 4) });
        var hBox = new TextBox { Text = DocHeight.ToString(), Height = 28, Padding = new Thickness(6, 4, 6, 4) };
        ImeInput.ConfigureForTextInput(hBox);
        body.Children.Add(hBox);
        body.Children.Add(new TextBlock { Text = SR.Get("NewDoc.Background"), Opacity = 0.55, Margin = new Thickness(0, 10, 0, 4) });
        var bg = new ComboBox { Height = 28 };
        bg.Items.Add(new ComboBoxItem { Content = SR.Get("NewDoc.BgWhite"), IsSelected = true });
        bg.Items.Add(new ComboBoxItem { Content = SR.Get("NewDoc.BgTransparent") });
        body.Children.Add(bg);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 4)
        };
        var ok = new Button { Content = SR.Get("Common.Ok"), MinWidth = 80, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        var cancel = new Button { Content = SR.Get("Common.Cancel"), MinWidth = 80, Padding = new Thickness(14, 7, 14, 7), IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(wBox.Text, out int w) || w < 1 || w > 100000)
            { ThemedMessageWindow.Show(this, SR.Get("NewDoc.Title"), SR.Get("NewDoc.WidthInvalid"), UiMessageKind.Warn); return; }
            if (!int.TryParse(hBox.Text, out int h) || h < 1 || h > 100000)
            { ThemedMessageWindow.Show(this, SR.Get("NewDoc.Title"), SR.Get("NewDoc.HeightInvalid"), UiMessageKind.Warn); return; }
            DocWidth = w;
            DocHeight = h;
            BackgroundKind = bg.SelectedIndex == 1 ? DocumentBackgroundKind.Transparent : DocumentBackgroundKind.White;
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        root.Children.Add(body);
        outer.Child = root;
        Content = outer;
    }
}
