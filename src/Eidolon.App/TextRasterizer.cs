using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Eidolon.Core;

namespace Eidolon.App;

public static class TextRasterizer
{
    public static void RebuildCache(TextLayer layer, int docW, int docH)
    {
        var surface = new TileSurface(docW, docH);
        if (string.IsNullOrEmpty(layer.Content))
        {
            layer.RasterCache = surface;
            layer.CacheDirty = false;
            return;
        }

        var dpi = 96.0;
        var typeface = new Typeface(new FontFamily(layer.FontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var brush = new SolidColorBrush(Color.FromRgb(layer.Color.R, layer.Color.G, layer.Color.B));
        brush.Freeze();

        if (!layer.Vertical)
        {
            var ft = new FormattedText(
                layer.Content,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                layer.FontSize,
                brush,
                1.0);
            int tw = Math.Max(1, (int)Math.Ceiling(ft.Width) + 4);
            int th = Math.Max(1, (int)Math.Ceiling(ft.Height) + 4);
            var rtb = new RenderTargetBitmap(tw, th, dpi, dpi, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, tw, th));
                dc.DrawText(ft, new Point(2, 2));
            }
            rtb.Render(dv);
            BlitBitmapToSurface(rtb, surface, (int)layer.X, (int)layer.Y);
        }
        else
        {
            // Vertical: draw chars top-to-bottom
            float x = layer.X;
            float y = layer.Y;
            foreach (var ch in layer.Content)
            {
                if (ch == '\n') { x += layer.FontSize * 1.2f; y = layer.Y; continue; }
                var ft = new FormattedText(
                    ch.ToString(),
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    layer.FontSize,
                    brush,
                    1.0);
                int tw = Math.Max(1, (int)Math.Ceiling(ft.Width) + 2);
                int th = Math.Max(1, (int)Math.Ceiling(ft.Height) + 2);
                var rtb = new RenderTargetBitmap(tw, th, dpi, dpi, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                    dc.DrawText(ft, new Point(1, 1));
                rtb.Render(dv);
                BlitBitmapToSurface(rtb, surface, (int)x, (int)y);
                y += (float)ft.Height + 2;
            }
        }

        layer.RasterCache = surface;
        layer.CacheDirty = false;
    }

    private static void BlitBitmapToSurface(RenderTargetBitmap rtb, TileSurface surface, int ox, int oy)
    {
        int w = rtb.PixelWidth, h = rtb.PixelHeight;
        var pixels = new byte[w * h * 4];
        rtb.CopyPixels(pixels, w * 4, 0);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
            if (a == 0) continue;
            // Pbgra -> straight
            if (a < 255)
            {
                float af = a / 255f;
                r = (byte)Math.Clamp((int)(r / af + 0.5f), 0, 255);
                g = (byte)Math.Clamp((int)(g / af + 0.5f), 0, 255);
                b = (byte)Math.Clamp((int)(b / af + 0.5f), 0, 255);
            }
            int dx = ox + x, dy = oy + y;
            if ((uint)dx >= (uint)surface.Width || (uint)dy >= (uint)surface.Height) continue;
            var dst = surface.GetPixel(dx, dy);
            float sa = a / 255f;
            float da = dst.A / 255f;
            float sr = r / 255f, sg = g / 255f, sb = b / 255f;
            float dr = dst.R / 255f, dg = dst.G / 255f, db = dst.B / 255f;
            float oa = sa + da * (1 - sa);
            float or_ = oa <= 1e-6f ? 0 : (sr * sa + dr * da * (1 - sa)) / oa;
            float og = oa <= 1e-6f ? 0 : (sg * sa + dg * da * (1 - sa)) / oa;
            float ob = oa <= 1e-6f ? 0 : (sb * sa + db * da * (1 - sa)) / oa;
            surface.SetPixel(dx, dy, new ColorRgba8(
                (byte)(or_ * 255 + 0.5f),
                (byte)(og * 255 + 0.5f),
                (byte)(ob * 255 + 0.5f),
                (byte)Math.Clamp((int)(oa * 255 + 0.5f), 0, 255)));
        }
    }
}
