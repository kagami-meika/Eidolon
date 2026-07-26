using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Eidolon.Core;

namespace Eidolon.App.Controls;

/// <summary>
/// Channel gradient strip under color sliders (OKLCH-style preview).
/// Mode: 0 RGB, 1 HSV, 2 HSL, 3 OKLCH. Channel 0/1/2 varies across width.
/// V0/V1/V2 are the three slider values in UI units.
/// </summary>
public sealed class ColorRampBar : FrameworkElement
{
    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(int), typeof(ColorRampBar),
            new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsRender, OnNeed));

    public static readonly DependencyProperty ChannelProperty =
        DependencyProperty.Register(nameof(Channel), typeof(int), typeof(ColorRampBar),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnNeed));

    public static readonly DependencyProperty V0Property =
        DependencyProperty.Register(nameof(V0), typeof(double), typeof(ColorRampBar),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnNeed));

    public static readonly DependencyProperty V1Property =
        DependencyProperty.Register(nameof(V1), typeof(double), typeof(ColorRampBar),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnNeed));

    public static readonly DependencyProperty V2Property =
        DependencyProperty.Register(nameof(V2), typeof(double), typeof(ColorRampBar),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnNeed));

    public int Mode { get => (int)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public int Channel { get => (int)GetValue(ChannelProperty); set => SetValue(ChannelProperty, value); }
    public double V0 { get => (double)GetValue(V0Property); set => SetValue(V0Property, value); }
    public double V1 { get => (double)GetValue(V1Property); set => SetValue(V1Property, value); }
    public double V2 { get => (double)GetValue(V2Property); set => SetValue(V2Property, value); }

    private WriteableBitmap? _bmp;
    private int _bw, _bh;
    private byte[]? _px;

    private static void OnNeed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorRampBar b) b.Rebuild();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
    }

    public void Rebuild()
    {
        int w = Math.Max(2, (int)Math.Ceiling(ActualWidth));
        int h = Math.Max(2, (int)Math.Ceiling(ActualHeight));
        if (double.IsNaN(ActualWidth) || ActualWidth < 1) return;

        if (_bmp is null || _bw != w || _bh != h)
        {
            _bw = w; _bh = h;
            _px = new byte[w * h * 4];
            _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            RenderOptions.SetBitmapScalingMode(_bmp, BitmapScalingMode.NearestNeighbor);
        }

        Fill(_px!, w, h, Mode, Channel, V0, V1, V2);
        _bmp.WritePixels(new Int32Rect(0, 0, w, h), _px, w * 4, 0);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_bmp is null) Rebuild();
        if (_bmp is null) return;
        dc.DrawImage(_bmp, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x1C, 0x1C, 0x1C)), 1),
            new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)));
    }

    public static void Fill(byte[] bgra, int w, int h, int mode, int channel, double v0, double v1, double v2)
    {
        for (int x = 0; x < w; x++)
        {
            float t = w <= 1 ? 0f : x / (float)(w - 1);
            Sample(mode, channel, t, v0, v1, v2, out float r, out float g, out float b, out bool gamut);
            if (!gamut) { r *= 0.5f; g *= 0.5f; b *= 0.5f; }
            byte R = (byte)(Math.Clamp(r, 0, 1) * 255f + 0.5f);
            byte G = (byte)(Math.Clamp(g, 0, 1) * 255f + 0.5f);
            byte B = (byte)(Math.Clamp(b, 0, 1) * 255f + 0.5f);
            for (int y = 0; y < h; y++)
            {
                int i = (y * w + x) * 4;
                bgra[i] = B; bgra[i + 1] = G; bgra[i + 2] = R; bgra[i + 3] = 255;
            }
        }
    }

    /// <summary>UI units match MainWindow sliders.</summary>
    public static void Sample(int mode, int channel, float t, double v0, double v1, double v2,
        out float r, out float g, out float b, out bool inGamut)
    {
        inGamut = true;
        switch (mode)
        {
            case 0: // RGB 0..255
            {
                float R = channel == 0 ? t * 255f : (float)v0;
                float G = channel == 1 ? t * 255f : (float)v1;
                float B = channel == 2 ? t * 255f : (float)v2;
                r = R / 255f; g = G / 255f; b = B / 255f;
                break;
            }
            case 1: // HSV H0..360 S0..100 V0..100
            {
                float H = (channel == 0 ? t * 360f : (float)v0) / 360f;
                float S = (channel == 1 ? t * 100f : (float)v1) / 100f;
                float V = (channel == 2 ? t * 100f : (float)v2) / 100f;
                ColorModels.HsvToRgb(H, S, V, out r, out g, out b);
                break;
            }
            case 2: // HSL
            {
                float H = (channel == 0 ? t * 360f : (float)v0) / 360f;
                float S = (channel == 1 ? t * 100f : (float)v1) / 100f;
                float L = (channel == 2 ? t * 100f : (float)v2) / 100f;
                ColorModels.HslToRgb(H, S, L, out r, out g, out b);
                break;
            }
            default: // OKLCH L0..100, C0..40 (maps to 0..0.4), H0..360
            {
                float L = (channel == 0 ? t * 100f : (float)v0) / 100f;
                float C = (channel == 1 ? t * 40f : (float)v1) / 100f; // 40/100=0.4 max chroma UI
                float Hue = channel == 2 ? t * 360f : (float)v2;
                float Hrad = Hue * (MathF.PI / 180f);
                inGamut = ColorModels.OklchToRgbChecked(L, C, Hrad, out r, out g, out b);
                break;
            }
        }
    }
}
