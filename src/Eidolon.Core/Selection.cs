namespace Eidolon.Core;

public enum SelectionMode
{
    Replace,
    Add,
    Subtract,
    Intersect
}

/// <summary>Document selection as 0..255 coverage mask (A channel used).</summary>
public sealed class Selection
{
    public TileSurface Mask { get; }
    public bool OutlineVisible { get; set; } = true;
    public IntRect Bounds { get; private set; }
    public bool IsEmpty => Bounds.IsEmpty;

    public Selection(int width, int height)
    {
        Mask = new TileSurface(width, height);
        Bounds = default;
    }

    public void Clear()
    {
        Mask.Clear();
        Bounds = default;
    }

    public byte Get(int x, int y)
    {
        var p = Mask.GetPixel(x, y);
        return p.A;
    }

    public void Set(int x, int y, byte a)
    {
        if (a == 0)
        {
            // leave empty tile sparse: write transparent
            Mask.SetPixel(x, y, ColorRgba8.Transparent);
        }
        else
        {
            Mask.SetPixel(x, y, new ColorRgba8(255, 255, 255, a));
        }
    }

    public void RecalcBounds()
    {
        var b = Mask.GetDirtyBoundsHint();
        if (b is null) { Bounds = default; return; }
        // tighten by scanning tiles for non-zero alpha
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        bool any = false;
        int ts = Mask.TileSize;
        foreach (var (key, tile) in Mask.Tiles)
        {
            int tx = (int)(key >> 32);
            int ty = (int)(key & 0xFFFFFFFF);
            int ox = tx * ts, oy = ty * ts;
            for (int ly = 0; ly < ts; ly++)
            for (int lx = 0; lx < ts; lx++)
            {
                if (tile.Pixels[ly * ts + lx].A == 0) continue;
                int x = ox + lx, y = oy + ly;
                if ((uint)x >= (uint)Mask.Width || (uint)y >= (uint)Mask.Height) continue;
                any = true;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }
        Bounds = any ? IntRect.FromMinMax(minX, minY, maxX, maxY) : default;
    }

    public void ApplyRect(IntRect rect, SelectionMode mode)
    {
        rect = rect.ClampTo(Mask.Width, Mask.Height);
        if (rect.IsEmpty) return;

        if (mode == SelectionMode.Replace)
            Clear();

        for (int y = rect.Y; y < rect.Bottom; y++)
        for (int x = rect.X; x < rect.Right; x++)
        {
            byte cur = Get(x, y);
            byte next = mode switch
            {
                SelectionMode.Subtract => (byte)0,
                SelectionMode.Intersect => cur,
                _ => (byte)255 // Replace/Add
            };
            if (mode == SelectionMode.Intersect)
            {
                // outside rect already handled by only iterating rect — need clear outside
            }
            Set(x, y, next);
        }

        if (mode == SelectionMode.Intersect)
        {
            // zero everything outside rect
            var oldBounds = Bounds;
            if (!oldBounds.IsEmpty)
            {
                for (int y = oldBounds.Y; y < oldBounds.Bottom; y++)
                for (int x = oldBounds.X; x < oldBounds.Right; x++)
                {
                    if (!rect.Contains(x, y)) Set(x, y, 0);
                }
            }
        }

        RecalcBounds();
    }

    public void ApplyLasso(IReadOnlyList<(int x, int y)> points, SelectionMode mode)
    {
        if (points.Count < 3) return;
        int minX = points.Min(p => p.x);
        int maxX = points.Max(p => p.x);
        int minY = points.Min(p => p.y);
        int maxY = points.Max(p => p.y);
        minX = Math.Clamp(minX, 0, Mask.Width - 1);
        maxX = Math.Clamp(maxX, 0, Mask.Width - 1);
        minY = Math.Clamp(minY, 0, Mask.Height - 1);
        maxY = Math.Clamp(maxY, 0, Mask.Height - 1);

        if (mode == SelectionMode.Replace) Clear();

        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            if (!PointInPolygon(x + 0.5f, y + 0.5f, points)) continue;
            byte next = mode == SelectionMode.Subtract ? (byte)0 : (byte)255;
            if (mode == SelectionMode.Intersect)
                next = Get(x, y) > 0 ? (byte)255 : (byte)0;
            else if (mode == SelectionMode.Add || mode == SelectionMode.Replace)
                next = 255;
            Set(x, y, next);
        }
        if (mode == SelectionMode.Intersect)
        {
            // clear outside polygon bbox that was selected
            RecalcBounds();
            var b = Bounds;
            if (!b.IsEmpty)
            {
                for (int y = b.Y; y < b.Bottom; y++)
                for (int x = b.X; x < b.Right; x++)
                {
                    if (!PointInPolygon(x + 0.5f, y + 0.5f, points)) Set(x, y, 0);
                }
            }
        }
        RecalcBounds();
    }

    public void MagicWand(TileSurface source, int sx, int sy, int tolerance, bool contiguous, SelectionMode mode)
    {
        if ((uint)sx >= (uint)source.Width || (uint)sy >= (uint)source.Height) return;
        var target = source.GetPixel(sx, sy);
        if (mode == SelectionMode.Replace) Clear();

        if (!contiguous)
        {
            for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                if (!Near(source.GetPixel(x, y), target, tolerance)) continue;
                WriteMode(x, y, mode);
            }
            RecalcBounds();
            return;
        }

        var visited = new bool[source.Width * source.Height];
        var stack = new Stack<(int x, int y)>();
        stack.Push((sx, sy));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if ((uint)x >= (uint)source.Width || (uint)y >= (uint)source.Height) continue;
            int idx = y * source.Width + x;
            if (visited[idx]) continue;
            visited[idx] = true;
            if (!Near(source.GetPixel(x, y), target, tolerance)) continue;
            WriteMode(x, y, mode);
            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }
        RecalcBounds();
    }

    private void WriteMode(int x, int y, SelectionMode mode)
    {
        byte cur = Get(x, y);
        byte next = mode switch
        {
            SelectionMode.Subtract => (byte)0,
            SelectionMode.Intersect => cur > 0 ? (byte)255 : (byte)0,
            _ => (byte)255
        };
        Set(x, y, next);
    }

    private static bool Near(ColorRgba8 a, ColorRgba8 b, int tol) =>
        Math.Abs(a.R - b.R) <= tol && Math.Abs(a.G - b.G) <= tol &&
        Math.Abs(a.B - b.B) <= tol && Math.Abs(a.A - b.A) <= tol;

    private static bool PointInPolygon(float x, float y, IReadOnlyList<(int x, int y)> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            float xi = poly[i].x, yi = poly[i].y;
            float xj = poly[j].x, yj = poly[j].y;
            bool intersect = ((yi > y) != (yj > y)) &&
                             (x < (xj - xi) * (y - yi) / (yj - yi + 1e-12f) + xi);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    public float Coverage(int x, int y)
    {
        if (IsEmpty) return 1f; // no selection = paint everywhere
        return Get(x, y) / 255f;
    }
}
