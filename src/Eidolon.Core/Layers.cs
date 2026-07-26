namespace Eidolon.Core;

[Flags]
public enum LayerLocks
{
    None = 0,
    Transparency = 1,
    Pixels = 2,
    Position = 4,
    All = Transparency | Pixels | Position
}

public enum LayerKind
{
    Raster,
    Group,
    Vector,
    Text,
    Frame
}

public abstract class LayerNode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    public float Opacity { get; set; } = 1f;
    public BlendMode Blend { get; set; } = BlendMode.Normal;
    public LayerLocks Locks { get; set; } = LayerLocks.None;
    public bool ClippedToBelow { get; set; }
    public abstract LayerKind Kind { get; }
}

public sealed class RasterLayer : LayerNode
{
    public RasterLayer(int width, int height, string name = "Layer")
    {
        Name = name;
        Surface = new TileSurface(width, height);
    }

    public override LayerKind Kind => LayerKind.Raster;
    public TileSurface Surface { get; }
}

public sealed class GroupLayer : LayerNode
{
    public override LayerKind Kind => LayerKind.Group;
    /// <summary>Index 0 = bottom-most.</summary>
    public List<LayerNode> Children { get; } = new();
}
