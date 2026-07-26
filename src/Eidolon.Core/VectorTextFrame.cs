namespace Eidolon.Core;

public readonly record struct StrokePoint(float X, float Y, float Pressure, float Width)
{
    public Float2 Pos => new(X, Y);
    public StrokePoint WithPos(float x, float y) => new(x, y, Pressure, Width);
    public StrokePoint WithPos(Float2 p) => new(p.X, p.Y, Pressure, Width);
}

public enum VectorPathMode
{
    Polyline = 0,
    Spline = 1
}

public sealed class VectorStroke
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public List<StrokePoint> Points { get; } = new();
    public ColorRgba8 Color { get; set; } = ColorRgba8.Black;
    public float BaseWidth { get; set; } = 2f;
    public bool Closed { get; set; }
    public bool Filled { get; set; }
    public ColorRgba8 FillColor { get; set; } = ColorRgba8.Black;
    public VectorPathMode PathMode { get; set; } = VectorPathMode.Polyline;

    public VectorStroke Clone()
    {
        var s = new VectorStroke
        {
            Id = Id,
            Color = Color,
            BaseWidth = BaseWidth,
            Closed = Closed,
            Filled = Filled,
            FillColor = FillColor,
            PathMode = PathMode
        };
        s.Points.AddRange(Points);
        return s;
    }
}

public sealed class VectorLayer : LayerNode
{
    public override LayerKind Kind => LayerKind.Vector;
    public List<VectorStroke> Strokes { get; } = new();
    public TileSurface? RasterCache { get; set; }
    public bool CacheDirty { get; set; } = true;

    public void InvalidateCache() => CacheDirty = true;

    public List<VectorStroke> CloneStrokes()
    {
        var list = new List<VectorStroke>(Strokes.Count);
        foreach (var s in Strokes)
            list.Add(s.Clone());
        return list;
    }

    public void ReplaceStrokes(IReadOnlyList<VectorStroke> strokes)
    {
        Strokes.Clear();
        foreach (var s in strokes)
            Strokes.Add(s.Clone());
        InvalidateCache();
    }
}

public sealed class TextLayer : LayerNode
{
    public override LayerKind Kind => LayerKind.Text;
    public string Content { get; set; } = "Text";
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public float FontSize { get; set; } = 24f;
    public bool Vertical { get; set; }
    public ColorRgba8 Color { get; set; } = ColorRgba8.Black;
    public float X { get; set; }
    public float Y { get; set; }
    public TileSurface? RasterCache { get; set; }
    public bool CacheDirty { get; set; } = true;
}

public sealed class FrameRect
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public IntRect Bounds { get; set; }
}

public sealed class FrameLayer : LayerNode
{
    public override LayerKind Kind => LayerKind.Frame;
    public List<FrameRect> Frames { get; } = new();
    public float LineWidth { get; set; } = 2f;
    public ColorRgba8 LineColor { get; set; } = ColorRgba8.Black;
    public TileSurface? RasterCache { get; set; }
    public bool CacheDirty { get; set; } = true;
    public void InvalidateCache() => CacheDirty = true;
}
