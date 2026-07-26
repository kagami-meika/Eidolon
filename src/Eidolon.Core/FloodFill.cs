using Eidolon.Core;

namespace Eidolon.Core;

/// <summary>Flood fill on a raster layer.</summary>
public static class FloodFill
{
    public static IntRect Fill(RasterLayer layer, int sx, int sy, ColorRgba8 color, int tolerance, bool lockAlpha, Selection? selection = null)
    {
        var surface = layer.Surface;
        if ((uint)sx >= (uint)surface.Width || (uint)sy >= (uint)surface.Height)
            return default;
        if ((layer.Locks & LayerLocks.Pixels) != 0) return default;

        var target = surface.GetPixel(sx, sy);
        if (lockAlpha && target.A == 0) return default;
        if (ColorsEqual(target, color, 0)) return default;

        var visited = new bool[surface.Width * surface.Height];
        var stack = new Stack<(int x, int y)>();
        stack.Push((sx, sy));
        int minX = sx, maxX = sx, minY = sy, maxY = sy;

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if ((uint)x >= (uint)surface.Width || (uint)y >= (uint)surface.Height) continue;
            int idx = y * surface.Width + x;
            if (visited[idx]) continue;
            var p = surface.GetPixel(x, y);
            if (!ColorsEqual(p, target, tolerance)) continue;
            if (lockAlpha && p.A == 0) continue;
            float cov = selection?.Coverage(x, y) ?? 1f;
            if (cov <= 0.001f) continue;
            visited[idx] = true;
            var dst = surface.GetPixel(x, y);
            // simple replace with selection coverage
            byte na = lockAlpha ? dst.A : (byte)Math.Clamp((int)(color.A * cov + 0.5f), 0, 255);
            if (!lockAlpha && cov < 0.999f)
            {
                // blend over
                float sa = cov * (color.A / 255f);
                float da = dst.A / 255f;
                float oa = sa + da * (1 - sa);
                float r = (color.R / 255f * sa + dst.R / 255f * da * (1 - sa)) / Math.Max(oa, 1e-6f);
                float g = (color.G / 255f * sa + dst.G / 255f * da * (1 - sa)) / Math.Max(oa, 1e-6f);
                float b = (color.B / 255f * sa + dst.B / 255f * da * (1 - sa)) / Math.Max(oa, 1e-6f);
                surface.SetPixel(x, y, new ColorRgba8((byte)(r*255+0.5f),(byte)(g*255+0.5f),(byte)(b*255+0.5f),(byte)(oa*255+0.5f)));
            }
            else
                surface.SetPixel(x, y, color with { A = na });
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }
        return IntRect.FromMinMax(minX, minY, maxX, maxY);
    }

    private static bool ColorsEqual(ColorRgba8 a, ColorRgba8 b, int tol)
    {
        return Math.Abs(a.R - b.R) <= tol
            && Math.Abs(a.G - b.G) <= tol
            && Math.Abs(a.B - b.B) <= tol
            && Math.Abs(a.A - b.A) <= tol;
    }
}
