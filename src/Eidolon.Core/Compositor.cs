namespace Eidolon.Core;

/// <summary>CPU compositor with blend modes, dirty rect, and Pbgra display output.</summary>
public static class Compositor
{
    public static void CompositeToBgra(Document doc, Span<byte> bgra, int stride) =>
        CompositeToBgra(doc, bgra, stride, new IntRect(0, 0, doc.Width, doc.Height));

    public static void CompositeToBgra(Document doc, Span<byte> bgra, int stride, IntRect dirty)
    {
        int w = doc.Width;
        int h = doc.Height;
        dirty = dirty.ClampTo(w, h);
        if (dirty.IsEmpty) return;

        FillBackground(doc, bgra, stride, dirty);

        for (int i = 0; i < doc.Root.Children.Count; i++)
            CompositeNode(doc, i, doc.Root.Children[i], bgra, stride, dirty, 1f);
    }

    /// <summary>Composite then convert to premultiplied BGRA for WPF PixelFormats.Pbgra32 (avoids black fringes).</summary>
    public static void CompositeToPbgra(Document doc, Span<byte> pbgra, int stride, IntRect dirty)
    {
        CompositeToBgra(doc, pbgra, stride, dirty);
        dirty = dirty.ClampTo(doc.Width, doc.Height);
        if (dirty.IsEmpty) return;
        for (int y = dirty.Y; y < dirty.Bottom; y++)
        {
            int row = y * stride;
            for (int x = dirty.X; x < dirty.Right; x++)
            {
                int i = row + x * 4;
                byte ba = pbgra[i + 3];
                if (ba == 0 || ba == 255) continue;
                float a = ba / 255f;
                pbgra[i] = (byte)(pbgra[i] * a + 0.5f);
                pbgra[i + 1] = (byte)(pbgra[i + 1] * a + 0.5f);
                pbgra[i + 2] = (byte)(pbgra[i + 2] * a + 0.5f);
            }
        }
    }

    private static void FillBackground(Document doc, Span<byte> bgra, int stride, IntRect dirty)
    {
        byte r, g, b, a;
        switch (doc.Background.Kind)
        {
            case DocumentBackgroundKind.Transparent:
                r = g = b = a = 0;
                break;
            case DocumentBackgroundKind.Color:
                r = doc.Background.Color.R;
                g = doc.Background.Color.G;
                b = doc.Background.Color.B;
                a = 255;
                break;
            default:
                r = g = b = a = 255;
                break;
        }

        for (int y = dirty.Y; y < dirty.Bottom; y++)
        {
            int row = y * stride;
            for (int x = dirty.X; x < dirty.Right; x++)
            {
                int i = row + x * 4;
                bgra[i] = b;
                bgra[i + 1] = g;
                bgra[i + 2] = r;
                bgra[i + 3] = a;
            }
        }
    }

    private static void CompositeNode(Document doc, int indexInRoot, LayerNode node, Span<byte> bgra, int stride, IntRect dirty, float parentOpacity)
    {
        if (!node.Visible) return;
        float op = parentOpacity * Math.Clamp(node.Opacity, 0, 1);

        if (node is GroupLayer group)
        {
            for (int i = 0; i < group.Children.Count; i++)
                CompositeNode(doc, -1, group.Children[i], bgra, stride, dirty, op);
            return;
        }

        if (node is RasterLayer raster)
        {
            RasterLayer? clipBase = null;
            if (node.ClippedToBelow && indexInRoot > 0)
            {
                for (int j = indexInRoot - 1; j >= 0; j--)
                {
                    if (doc.Root.Children[j] is RasterLayer baseLayer && !baseLayer.ClippedToBelow)
                    {
                        clipBase = baseLayer;
                        break;
                    }
                    if (!doc.Root.Children[j].ClippedToBelow) break;
                }
            }
            CompositeRaster(raster, bgra, stride, dirty, op, node.Blend, clipBase);
            return;
        }

        if (node is VectorLayer vector)
        {
            if (vector.RasterCache is null || vector.CacheDirty)
            {
                var tmp = new TileSurface(doc.Width, doc.Height);
                foreach (var s in vector.Strokes)
                    VectorRasterizer.DrawStroke(tmp, s, 1f);
                vector.RasterCache = tmp;
                vector.CacheDirty = false;
            }
            VectorRasterizer.CompositeSurfaceToBgra(vector.RasterCache, bgra, stride, dirty, op, node.Blend);
            return;
        }

        if (node is FrameLayer frame)
        {
            if (frame.RasterCache is null || frame.CacheDirty)
            {
                var tmp = new TileSurface(doc.Width, doc.Height);
                foreach (var fr in frame.Frames)
                    VectorRasterizer.DrawFrameRect(tmp, fr.Bounds, frame.LineWidth, frame.LineColor, 1f);
                frame.RasterCache = tmp;
                frame.CacheDirty = false;
            }
            VectorRasterizer.CompositeSurfaceToBgra(frame.RasterCache, bgra, stride, dirty, op, node.Blend);
            return;
        }

        if (node is TextLayer text)
        {
            // Prefer cache if present
            if (text.RasterCache is { } cache && !text.CacheDirty)
            {
                VectorRasterizer.CompositeSurfaceToBgra(cache, bgra, stride, dirty, op, node.Blend);
            }
            // If no cache, skip (UI will rebuild cache via TextRasterCache)
            return;
        }
    }

    private static void CompositeRaster(RasterLayer raster, Span<byte> bgra, int stride, IntRect dirty, float op, BlendMode blend, RasterLayer? clipBase)
    {
        int w = raster.Surface.Width;
        int h = raster.Surface.Height;
        int ts = raster.Surface.TileSize;
        var surface = raster.Surface;

        int tx0 = dirty.X / ts;
        int ty0 = dirty.Y / ts;
        int tx1 = (dirty.Right - 1) / ts;
        int ty1 = (dirty.Bottom - 1) / ts;

        for (int ty = ty0; ty <= ty1; ty++)
        for (int tx = tx0; tx <= tx1; tx++)
        {
            if (!surface.TryGetTile(tx, ty, out var tile)) continue;
            int ox = tx * ts;
            int oy = ty * ts;
            int yStart = Math.Max(dirty.Y, oy);
            int yEnd = Math.Min(dirty.Bottom, oy + ts);
            int xStart = Math.Max(dirty.X, ox);
            int xEnd = Math.Min(dirty.Right, ox + ts);

            for (int y = yStart; y < yEnd; y++)
            {
                int ly = y - oy;
                int row = y * stride;
                for (int x = xStart; x < xEnd; x++)
                {
                    int lx = x - ox;
                    var src = tile.Pixels[ly * ts + lx];
                    if (src.A == 0) continue;

                    float sa = (src.A / 255f) * op;
                    if (clipBase != null)
                        sa *= clipBase.Surface.GetPixel(x, y).A / 255f;
                    if (sa <= 0.0001f) continue;

                    int i = row + x * 4;
                    float db = bgra[i] / 255f;
                    float dg = bgra[i + 1] / 255f;
                    float dr = bgra[i + 2] / 255f;
                    float da = bgra[i + 3] / 255f;

                    float sr = src.R / 255f;
                    float sg = src.G / 255f;
                    float sb = src.B / 255f;

                    Blend(blend, ref sr, ref sg, ref sb, dr, dg, db);

                    float outA = sa + da * (1 - sa);
                    float outR, outG, outB;
                    if (outA <= 1e-6f)
                    {
                        outR = outG = outB = 0;
                    }
                    else
                    {
                        outR = (sr * sa + dr * da * (1 - sa)) / outA;
                        outG = (sg * sa + dg * da * (1 - sa)) / outA;
                        outB = (sb * sa + db * da * (1 - sa)) / outA;
                    }

                    bgra[i] = ToByte(outB);
                    bgra[i + 1] = ToByte(outG);
                    bgra[i + 2] = ToByte(outR);
                    bgra[i + 3] = ToByte(outA);
                }
            }
        }
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);

    public static void Blend(BlendMode mode, ref float sr, ref float sg, ref float sb, float dr, float dg, float db)
    {
        switch (mode)
        {
            case BlendMode.Normal:
            case BlendMode.Erase:
                break;
            case BlendMode.Multiply:
                sr *= dr; sg *= dg; sb *= db;
                break;
            case BlendMode.Screen:
                sr = 1 - (1 - sr) * (1 - dr);
                sg = 1 - (1 - sg) * (1 - dg);
                sb = 1 - (1 - sb) * (1 - db);
                break;
            case BlendMode.Overlay:
                sr = Overlay(dr, sr); sg = Overlay(dg, sg); sb = Overlay(db, sb);
                break;
            case BlendMode.Darken:
                sr = MathF.Min(sr, dr); sg = MathF.Min(sg, dg); sb = MathF.Min(sb, db);
                break;
            case BlendMode.Lighten:
                sr = MathF.Max(sr, dr); sg = MathF.Max(sg, dg); sb = MathF.Max(sb, db);
                break;
            case BlendMode.ColorDodge:
                sr = ColorDodge(dr, sr); sg = ColorDodge(dg, sg); sb = ColorDodge(db, sb);
                break;
            case BlendMode.ColorBurn:
                sr = ColorBurn(dr, sr); sg = ColorBurn(dg, sg); sb = ColorBurn(db, sb);
                break;
            case BlendMode.LinearDodge:
                sr = MathF.Min(1, dr + sr);
                sg = MathF.Min(1, dg + sg);
                sb = MathF.Min(1, db + sb);
                break;
            case BlendMode.LinearBurn:
                sr = MathF.Max(0, dr + sr - 1);
                sg = MathF.Max(0, dg + sg - 1);
                sb = MathF.Max(0, db + sb - 1);
                break;
            case BlendMode.HardLight:
                sr = Overlay(sr, dr); sg = Overlay(sg, dg); sb = Overlay(sb, db);
                break;
            case BlendMode.SoftLight:
                sr = SoftLight(dr, sr); sg = SoftLight(dg, sg); sb = SoftLight(db, sb);
                break;
            case BlendMode.Difference:
                sr = MathF.Abs(dr - sr); sg = MathF.Abs(dg - sg); sb = MathF.Abs(db - sb);
                break;
            case BlendMode.Exclusion:
                sr = dr + sr - 2 * dr * sr;
                sg = dg + sg - 2 * dg * sg;
                sb = db + sb - 2 * db * sb;
                break;
            case BlendMode.Hue:
            case BlendMode.Saturation:
            case BlendMode.Color:
            case BlendMode.Luminosity:
                ApplyHslBlend(mode, ref sr, ref sg, ref sb, dr, dg, db);
                break;
        }
    }

    private static float Overlay(float b, float s) =>
        b < 0.5f ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s);

    private static float ColorDodge(float b, float s) =>
        s >= 1f ? 1f : MathF.Min(1f, b / (1f - s));

    private static float ColorBurn(float b, float s) =>
        s <= 0f ? 0f : MathF.Max(0f, 1f - (1f - b) / s);

    private static float SoftLight(float b, float s)
    {
        if (s <= 0.5f)
            return b - (1 - 2 * s) * b * (1 - b);
        float d = b <= 0.25f ? ((16 * b - 12) * b + 4) * b : MathF.Sqrt(b);
        return b + (2 * s - 1) * (d - b);
    }

    private static void ApplyHslBlend(BlendMode mode, ref float sr, ref float sg, ref float sb, float dr, float dg, float db)
    {
        ColorModels.RgbToHsl(sr, sg, sb, out float sh, out float ss, out float sl);
        ColorModels.RgbToHsl(dr, dg, db, out float dh, out float ds, out float dl);
        float h = dh, s = ds, l = dl;
        switch (mode)
        {
            case BlendMode.Hue: h = sh; break;
            case BlendMode.Saturation: s = ss; break;
            case BlendMode.Color: h = sh; s = ss; break;
            case BlendMode.Luminosity: l = sl; break;
        }
        ColorModels.HslToRgb(h, s, l, out sr, out sg, out sb);
    }
}