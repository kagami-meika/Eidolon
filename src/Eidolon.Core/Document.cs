namespace Eidolon.Core;

public enum DocumentBackgroundKind
{
    White,
    Transparent,
    Color
}

public sealed class DocumentBackground
{
    public DocumentBackgroundKind Kind { get; set; } = DocumentBackgroundKind.White;
    public ColorRgba8 Color { get; set; } = ColorRgba8.White;
}

public sealed class ColorState
{
    public ColorRgba8 Foreground { get; set; } = ColorRgba8.Black;
    public ColorRgba8 Background { get; set; } = ColorRgba8.White;

    public void Swap() => (Foreground, Background) = (Background, Foreground);
}

public sealed class Document
{
    public Document(int width, int height, float dpi = 72f)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        Dpi = dpi;
        Root = new GroupLayer { Name = "Root" };
        var layer = new RasterLayer(width, height, "Layer 1");
        Root.Children.Add(layer);
        ActiveLayerId = layer.Id;
        Selection = new Selection(width, height);
        Rulers = new RulerState();
        Rulers.ResetForDocument(width, height);
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string? FilePath { get; set; }
    public int Width { get; }
    public int Height { get; }
    public float Dpi { get; set; }
    public DocumentBackground Background { get; set; } = new();
    public GroupLayer Root { get; }
    public Guid? ActiveLayerId { get; set; }
    public ColorState Colors { get; } = new();
    public HistoryStack History { get; } = new();
    public Selection Selection { get; }
    public RulerState Rulers { get; }
    public bool IsDirty { get; set; }

    public LayerNode? FindLayer(Guid id) => FindLayer(Root, id);

    private static LayerNode? FindLayer(LayerNode node, Guid id)
    {
        if (node.Id == id) return node;
        if (node is GroupLayer g)
        {
            foreach (var c in g.Children)
            {
                var f = FindLayer(c, id);
                if (f != null) return f;
            }
        }
        return null;
    }

    public RasterLayer? ActiveRasterLayer =>
        ActiveLayerId is Guid id ? FindLayer(id) as RasterLayer : null;

    public IEnumerable<RasterLayer> EnumerateRasterLayers()
    {
        foreach (var n in Enumerate(Root))
            if (n is RasterLayer r) yield return r;
    }

    private static IEnumerable<LayerNode> Enumerate(LayerNode node)
    {
        yield return node;
        if (node is GroupLayer g)
        {
            foreach (var c in g.Children)
            foreach (var x in Enumerate(c))
                yield return x;
        }
    }

    public RasterLayer AddRasterLayer(string? name = null)
    {
        int n = Root.Children.Count + 1;
        var layer = new RasterLayer(Width, Height, name ?? $"Layer {n}");
        Root.Children.Add(layer);
        ActiveLayerId = layer.Id;
        IsDirty = true;
        return layer;
    }

    public VectorLayer AddVectorLayer(string? name = null)
    {
        int n = Root.Children.Count + 1;
        var layer = new VectorLayer { Name = name ?? $"Vector {n}" };
        Root.Children.Add(layer);
        ActiveLayerId = layer.Id;
        IsDirty = true;
        return layer;
    }

    public TextLayer AddTextLayer(string? name = null)
    {
        int n = Root.Children.Count + 1;
        var layer = new TextLayer
        {
            Name = name ?? $"Text {n}",
            X = Width * 0.1f,
            Y = Height * 0.1f
        };
        Root.Children.Add(layer);
        ActiveLayerId = layer.Id;
        IsDirty = true;
        return layer;
    }

    public FrameLayer AddFrameLayer(string? name = null)
    {
        int n = Root.Children.Count + 1;
        var layer = new FrameLayer { Name = name ?? $"Frame {n}" };
        Root.Children.Add(layer);
        ActiveLayerId = layer.Id;
        IsDirty = true;
        return layer;
    }

    public LayerNode? ActiveLayer =>
        ActiveLayerId is Guid id ? FindLayer(id) : null;
}

