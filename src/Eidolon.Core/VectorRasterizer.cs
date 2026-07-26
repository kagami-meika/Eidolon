namespace Eidolon.Core;

/// <summary>Rasterize vector strokes / frames into a tile surface or BGRA buffer.</summary>
public static class VectorRasterizer
{
    public static void DrawStroke(TileSurface surface, VectorStroke stroke, float opacity = 1f)
    {
        if (stroke.Points.Count == 0) return;

        if (stroke.Filled && stroke.Points.Count >= 3)
            FillPolygon(surface, stroke, opacity);

        var samples = SamplePath(stroke);
        if (samples.Count == 0) return;
        if (samples.Count == 1)
        {
            var p = samples[0];
            StampCircle(surface, p.X, p.Y, Math.Max(0.5f, p.Width * 0.5f), stroke.Color, opacity);
            return;
        }
        for (int i = 1; i < samples.Count; i++)
            DrawSegment(surface, samples[i - 1], samples[i], stroke.Color, opacity);
    }

    /// <summary>Expand stroke to drawable samples (polyline or Catmull-Rom spline).</summary>
    public static List<StrokePoint> SamplePath(VectorStroke stroke, int samplesPerSeg = 8)
    {
        var pts = stroke.Points;
        if (pts.Count == 0) return new List<StrokePoint>();
        if (pts.Count == 1 || stroke.PathMode == VectorPathMode.Polyline)
        {
            var list = new List<StrokePoint>(pts);
            if (stroke.Closed && pts.Count > 2)
                list.Add(pts[0]);
            return list;
        }

        // Catmull-Rom through nodes
        var result = new List<StrokePoint>();
        int n = pts.Count;
        int segs = stroke.Closed ? n : n - 1;
        if (segs < 1)
        {
            result.Add(pts[0]);
            return result;
        }

        for (int i = 0; i < segs; i++)
        {
            StrokePoint p0 = pts[Mod(i - 1, n, stroke.Closed)];
            StrokePoint p1 = pts[i];
            StrokePoint p2 = pts[Mod(i + 1, n, stroke.Closed)];
            StrokePoint p3 = pts[Mod(i + 2, n, stroke.Closed)];
            // open ends: clamp
            if (!stroke.Closed)
            {
                if (i == 0) p0 = p1;
                if (i == segs - 1) p3 = p2;
            }
            int steps = Math.Max(2, samplesPerSeg);
            int s0 = (i == 0) ? 0 : 1; // avoid duplicate joints
            for (int s = s0; s <= steps; s++)
            {
                float t = s / (float)steps;
                result.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
        return result;
    }

    private static int Mod(int i, int n, bool closed)
    {
        if (closed)
        {
            int m = i % n;
            return m < 0 ? m + n : m;
        }
        return Math.Clamp(i, 0, n - 1);
    }

    private static StrokePoint CatmullRom(StrokePoint p0, StrokePoint p1, StrokePoint p2, StrokePoint p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        float x = 0.5f * ((2 * p1.X) + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2
                          + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
        float y = 0.5f * ((2 * p1.Y) + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2
                          + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
        float w = 0.5f * ((2 * p1.Width) + (-p0.Width + p2.Width) * t
                          + (2 * p0.Width - 5 * p1.Width + 4 * p2.Width - p3.Width) * t2
                          + (-p0.Width + 3 * p1.Width - 3 * p2.Width + p3.Width) * t3);
        float pr = 0.5f * ((2 * p1.Pressure) + (-p0.Pressure + p2.Pressure) * t
                           + (2 * p0.Pressure - 5 * p1.Pressure + 4 * p2.Pressure - p3.Pressure) * t2
                           + (-p0.Pressure + 3 * p1.Pressure - 3 * p2.Pressure + p3.Pressure) * t3);
        return new StrokePoint(x, y, Math.Clamp(pr, 0.01f, 1f), Math.Max(0.5f, w));
    }

    public static void FillPolygon(TileSurface surface, VectorStroke stroke, float opacity = 1f)
    {
        var poly = SamplePath(stroke, samplesPerSeg: stroke.PathMode == VectorPathMode.Spline ? 12 : 1);
        if (poly.Count < 3) return;
        // drop closing duplicate if present
        if (poly.Count > 1)
        {
            var a = poly[0]; var b = poly[^1];
            if (MathF.Abs(a.X - b.X) < 1e-3f && MathF.Abs(a.Y - b.Y) < 1e-3f)
                poly.RemoveAt(poly.Count - 1);
        }
        if (poly.Count < 3) return;

        float minXf = poly[0].X, maxXf = poly[0].X, minYf = poly[0].Y, maxYf = poly[0].Y;
        foreach (var p in poly)
        {
            minXf = MathF.Min(minXf, p.X); maxXf = MathF.Max(maxXf, p.X);
            minYf = MathF.Min(minYf, p.Y); maxYf = MathF.Max(maxYf, p.Y);
        }
        int minX = Math.Max(0, (int)MathF.Floor(minXf));
        int maxX = Math.Min(surface.Width - 1, (int)MathF.Ceiling(maxXf));
        int minY = Math.Max(0, (int)MathF.Floor(minYf));
        int maxY = Math.Min(surface.Height - 1, (int)MathF.Ceiling(maxYf));

        var color = stroke.FillColor;
        float saBase = (color.A / 255f) * opacity;
        if (saBase <= 0.001f) return;

        int n = poly.Count;
        float[] xs = new float[n];
        for (int y = minY; y <= maxY; y++)
        {
            float yy = y + 0.5f;
            // scanline intersections
            int xc = 0;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % n];
                if (MathF.Abs(a.Y - b.Y) < 1e-6f) continue;
                if (yy < MathF.Min(a.Y, b.Y) || yy >= MathF.Max(a.Y, b.Y)) continue;
                float t = (yy - a.Y) / (b.Y - a.Y);
                float x = a.X + t * (b.X - a.X);
                if (xc < xs.Length) xs[xc++] = x;
            }
            // sort
            for (int i = 1; i < xc; i++)
            {
                float key = xs[i];
                int j = i - 1;
                while (j >= 0 && xs[j] > key) { xs[j + 1] = xs[j]; j--; }
                xs[j + 1] = key;
            }
            for (int k = 0; k + 1 < xc; k += 2)
            {
                int x0 = Math.Max(minX, (int)MathF.Ceiling(xs[k]));
                int x1 = Math.Min(maxX, (int)MathF.Floor(xs[k + 1]));
                for (int x = x0; x <= x1; x++)
                    BlendPixel(surface, x, y, color, saBase);
            }
        }
    }

    private static void BlendPixel(TileSurface surface, int x, int y, ColorRgba8 color, float sa)
    {
        var dst = surface.GetPixel(x, y);
        float da = dst.A / 255f;
        float sr = color.R / 255f, sg = color.G / 255f, sb = color.B / 255f;
        float dr = dst.R / 255f, dg = dst.G / 255f, db = dst.B / 255f;
        float oa = sa + da * (1 - sa);
        float or_ = oa <= 1e-6f ? 0 : (sr * sa + dr * da * (1 - sa)) / oa;
        float og = oa <= 1e-6f ? 0 : (sg * sa + dg * da * (1 - sa)) / oa;
        float ob = oa <= 1e-6f ? 0 : (sb * sa + db * da * (1 - sa)) / oa;
        surface.SetPixel(x, y, new ColorRgba8(
            (byte)(or_ * 255 + 0.5f),
            (byte)(og * 255 + 0.5f),
            (byte)(ob * 255 + 0.5f),
            (byte)Math.Clamp((int)(oa * 255 + 0.5f), 0, 255)));
    }

    private static void DrawSegment(TileSurface surface, StrokePoint a, StrokePoint b, ColorRgba8 color, float opacity)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float avgW = Math.Max(0.5f, (a.Width + b.Width) * 0.5f);
        int steps = Math.Max(1, (int)MathF.Ceiling(dist / Math.Max(0.4f, avgW * 0.35f)));
        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            float x = a.X + dx * t;
            float y = a.Y + dy * t;
            float w = a.Width + (b.Width - a.Width) * t;
            StampCircle(surface, x, y, Math.Max(0.5f, w * 0.5f), color, opacity);
        }
    }

    public static void StampCircle(TileSurface surface, float cx, float cy, float radius, ColorRgba8 color, float opacity)
    {
        int minX = Math.Max(0, (int)MathF.Floor(cx - radius - 1));
        int maxX = Math.Min(surface.Width - 1, (int)MathF.Ceiling(cx + radius + 1));
        int minY = Math.Max(0, (int)MathF.Floor(cy - radius - 1));
        int maxY = Math.Min(surface.Height - 1, (int)MathF.Ceiling(cy + radius + 1));
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            float ddx = x + 0.5f - cx;
            float ddy = y + 0.5f - cy;
            float d2 = ddx * ddx + ddy * ddy;
            if (d2 > (radius + 1) * (radius + 1)) continue;
            float cover = 1f;
            float d = MathF.Sqrt(d2);
            if (d > radius - 1f)
                cover = Math.Clamp(1f - (d - (radius - 1f)), 0f, 1f);
            if (cover <= 0.001f) continue;
            float sa = (color.A / 255f) * opacity * cover;
            BlendPixel(surface, x, y, color, sa);
        }
    }

    public static void DrawFrameRect(TileSurface surface, IntRect rect, float lineWidth, ColorRgba8 color, float opacity = 1f)
    {
        if (rect.IsEmpty) return;
        float half = Math.Max(0.5f, lineWidth * 0.5f);
        for (float x = rect.X; x <= rect.Right; x += Math.Max(0.5f, half * 0.5f))
        {
            StampCircle(surface, x, rect.Y, half, color, opacity);
            StampCircle(surface, x, rect.Bottom - 1, half, color, opacity);
        }
        for (float y = rect.Y; y <= rect.Bottom; y += Math.Max(0.5f, half * 0.5f))
        {
            StampCircle(surface, rect.X, y, half, color, opacity);
            StampCircle(surface, rect.Right - 1, y, half, color, opacity);
        }
    }

    public static void CompositeSurfaceToBgra(TileSurface surface, Span<byte> bgra, int stride, IntRect dirty, float opacity, BlendMode blend)
    {
        int ts = surface.TileSize;
        int tx0 = dirty.X / ts, ty0 = dirty.Y / ts;
        int tx1 = (dirty.Right - 1) / ts, ty1 = (dirty.Bottom - 1) / ts;
        for (int ty = ty0; ty <= ty1; ty++)
        for (int tx = tx0; tx <= tx1; tx++)
        {
            if (!surface.TryGetTile(tx, ty, out var tile)) continue;
            int ox = tx * ts, oy = ty * ts;
            int y0 = Math.Max(dirty.Y, oy);
            int y1 = Math.Min(dirty.Bottom, oy + ts);
            int x0 = Math.Max(dirty.X, ox);
            int x1 = Math.Min(dirty.Right, ox + ts);
            for (int y = y0; y < y1; y++)
            {
                int ly = y - oy;
                int row = y * stride;
                for (int x = x0; x < x1; x++)
                {
                    int lx = x - ox;
                    var src = tile.Pixels[ly * ts + lx];
                    if (src.A == 0) continue;
                    float sa = (src.A / 255f) * opacity;
                    int i = row + x * 4;
                    float db = bgra[i] / 255f, dg = bgra[i + 1] / 255f, dr = bgra[i + 2] / 255f, da = bgra[i + 3] / 255f;
                    float sr = src.R / 255f, sg = src.G / 255f, sb = src.B / 255f;
                    if (blend != BlendMode.Normal)
                        Compositor.Blend(blend, ref sr, ref sg, ref sb, dr, dg, db);
                    float outA = sa + da * (1 - sa);
                    float outR = outA <= 1e-6f ? 0 : (sr * sa + dr * da * (1 - sa)) / outA;
                    float outG = outA <= 1e-6f ? 0 : (sg * sa + dg * da * (1 - sa)) / outA;
                    float outB = outA <= 1e-6f ? 0 : (sb * sa + db * da * (1 - sa)) / outA;
                    bgra[i] = (byte)(outB * 255 + 0.5f);
                    bgra[i + 1] = (byte)(outG * 255 + 0.5f);
                    bgra[i + 2] = (byte)(outR * 255 + 0.5f);
                    bgra[i + 3] = (byte)Math.Clamp((int)(outA * 255 + 0.5f), 0, 255);
                }
            }
        }
    }

    /// <summary>Hit-test nearest node within radius; returns stroke index and point index.</summary>
    public static bool HitTestNode(VectorLayer layer, Float2 doc, float radius, out int strokeIndex, out int pointIndex)
    {
        strokeIndex = -1;
        pointIndex = -1;
        float best = radius * radius;
        for (int si = layer.Strokes.Count - 1; si >= 0; si--)
        {
            var s = layer.Strokes[si];
            for (int pi = 0; pi < s.Points.Count; pi++)
            {
                var p = s.Points[pi];
                float dx = p.X - doc.X, dy = p.Y - doc.Y;
                float d2 = dx * dx + dy * dy;
                if (d2 <= best)
                {
                    best = d2;
                    strokeIndex = si;
                    pointIndex = pi;
                }
            }
        }
        return strokeIndex >= 0;
    }

    /// <summary>Hit-test nearest stroke (distance to path samples).</summary>
    public static bool HitTestStroke(VectorLayer layer, Float2 doc, float radius, out int strokeIndex)
    {
        strokeIndex = -1;
        float best = radius;
        for (int si = layer.Strokes.Count - 1; si >= 0; si--)
        {
            var samples = SamplePath(layer.Strokes[si], 4);
            for (int i = 1; i < samples.Count; i++)
            {
                float d = DistToSegment(doc, samples[i - 1].Pos, samples[i].Pos);
                if (d < best)
                {
                    best = d;
                    strokeIndex = si;
                }
            }
            if (samples.Count == 1)
            {
                float dx = samples[0].X - doc.X, dy = samples[0].Y - doc.Y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d < best) { best = d; strokeIndex = si; }
            }
        }
        return strokeIndex >= 0;
    }

    private static float DistToSegment(Float2 p, Float2 a, Float2 b)
    {
        var ab = b - a;
        float len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 1e-6f)
        {
            float dx = p.X - a.X, dy = p.Y - a.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
        float t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0f, 1f);
        float qx = a.X + t * ab.X, qy = a.Y + t * ab.Y;
        float ddx = p.X - qx, ddy = p.Y - qy;
        return MathF.Sqrt(ddx * ddx + ddy * ddy);
    }
}
