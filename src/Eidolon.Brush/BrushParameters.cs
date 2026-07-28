namespace Eidolon.Brush;

public enum BrushToolKind
{
    Pencil,
    Airbrush,
    Brush,
    Watercolor,
    Marker,
    Binary,
    Eraser,
    Smudge,
    WillowLeaf
}

public sealed class BrushParameters
{
    public float SizePx { get; set; } = 10f;
    public float MinSizeRatio { get; set; } = 0.05f;
    public float Opacity { get; set; } = 1f;
    public float Flow { get; set; } = 1f;
    public float Hardness { get; set; } = 0.9f;
    public float SoftEdge { get; set; } = 0.05f;
    public float Blend { get; set; }
    public float Spacing { get; set; } = 0.12f;
    public bool AntiAlias { get; set; } = true;
    public bool EraseMode { get; set; }
    public bool LockAlpha { get; set; }
    public float StabilizerStrength { get; set; }
    public bool SizeByPressure { get; set; } = true;
    public bool OpacityByPressure { get; set; } = true;
    public bool FlowByPressure { get; set; }
    public float TextureStrength { get; set; } // 0..1 fly-white / grain
    public float TextureScale { get; set; } = 1f;
    public int TextureSeed { get; set; } = 1;
    public float SmudgeStrength { get; set; } = 0.55f;

    public BrushParameters Clone() => new()
    {
        SizePx = SizePx,
        MinSizeRatio = MinSizeRatio,
        Opacity = Opacity,
        Flow = Flow,
        Hardness = Hardness,
        SoftEdge = SoftEdge,
        Blend = Blend,
        Spacing = Spacing,
        AntiAlias = AntiAlias,
        EraseMode = EraseMode,
        LockAlpha = LockAlpha,
        StabilizerStrength = StabilizerStrength,
        SizeByPressure = SizeByPressure,
        OpacityByPressure = OpacityByPressure,
        FlowByPressure = FlowByPressure,
        TextureStrength = TextureStrength,
        TextureScale = TextureScale,
        TextureSeed = TextureSeed,
        SmudgeStrength = SmudgeStrength
    };
}

public sealed class BrushPreset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Pencil";
    public BrushToolKind Kind { get; set; } = BrushToolKind.Pencil;
    public BrushParameters Params { get; set; } = new();

    public BrushPreset Clone() => new()
    {
        Id = Guid.NewGuid(),
        Name = Name,
        Kind = Kind,
        Params = Params.Clone()
    };

    public static BrushPreset DefaultPencil() => new()
    {
        Name = "Pencil",
        Kind = BrushToolKind.Pencil,
        Params = new BrushParameters
        {
            SizePx = 8f,
            MinSizeRatio = 0.08f,
            Opacity = 1f,
            Hardness = 0.94f,
            SoftEdge = 0.04f,
            Spacing = 0.1f,
            AntiAlias = true,
            SizeByPressure = true,
            OpacityByPressure = false
        }
    };

    public static BrushPreset DefaultEraser() => new()
    {
        Name = "Eraser",
        Kind = BrushToolKind.Eraser,
        Params = new BrushParameters
        {
            SizePx = 28f,
            MinSizeRatio = 0.25f,
            Opacity = 1f,
            Hardness = 0.88f,
            Spacing = 0.12f,
            EraseMode = true,
            AntiAlias = true,
            SizeByPressure = true,
            OpacityByPressure = true
        }
    };

    public static BrushPreset DefaultAirbrush() => new()
    {
        Name = "Airbrush",
        Kind = BrushToolKind.Airbrush,
        Params = new BrushParameters
        {
            SizePx = 48f,
            MinSizeRatio = 0.35f,
            Opacity = 0.35f,
            Flow = 0.28f,
            Hardness = 0.12f,
            SoftEdge = 0.55f,
            Spacing = 0.07f,
            AntiAlias = true,
            SizeByPressure = true,
            OpacityByPressure = true,
            FlowByPressure = true
        }
    };

    public static BrushPreset DefaultBrush() => new()
    {
        Name = "Brush",
        Kind = BrushToolKind.Brush,
        Params = new BrushParameters
        {
            SizePx = 22f,
            MinSizeRatio = 0.12f,
            Opacity = 0.9f,
            Hardness = 0.72f,
            SoftEdge = 0.12f,
            Blend = 0.12f,
            Spacing = 0.14f,
            AntiAlias = true,
            SizeByPressure = true,
            OpacityByPressure = true
        }
    };

    public static BrushPreset DefaultWatercolor() => new()
    {
        Name = "Watercolor",
        Kind = BrushToolKind.Watercolor,
        Params = new BrushParameters
        {
            SizePx = 28f,
            MinSizeRatio = 0.2f,
            Opacity = 0.65f,
            Hardness = 0.42f,
            SoftEdge = 0.22f,
            Blend = 0.55f,
            Spacing = 0.12f,
            AntiAlias = true,
            SizeByPressure = true,
            OpacityByPressure = true
        }
    };

    public static BrushPreset DefaultMarker() => new()
    {
        Name = "Marker",
        Kind = BrushToolKind.Marker,
        Params = new BrushParameters
        {
            SizePx = 26f,
            MinSizeRatio = 0.7f,
            Opacity = 0.85f,
            Hardness = 0.9f,
            SoftEdge = 0.03f,
            Blend = 0.04f,
            Spacing = 0.1f,
            AntiAlias = true,
            SizeByPressure = false,
            OpacityByPressure = false
        }
    };

    public static BrushPreset DefaultSmudge() => new()
    {
        Name = "Smudge",
        Kind = BrushToolKind.Smudge,
        Params = new BrushParameters
        {
            SizePx = 30f,
            MinSizeRatio = 0.3f,
            Opacity = 1f,
            Hardness = 0.5f,
            SoftEdge = 0.25f,
            Spacing = 0.08f,
            SmudgeStrength = 0.65f,
            TextureStrength = 0f,
            AntiAlias = true,
            SizeByPressure = true,
            OpacityByPressure = false
        }
    };

    public static BrushPreset DefaultWillowLeaf() => new()
    {
        Name = "WillowLeaf",
        Kind = BrushToolKind.WillowLeaf,
        Params = new BrushParameters
        {
            SizePx = 14f,
            MinSizeRatio = 0.06f,
            Opacity = 0.92f,
            Hardness = 0.88f,
            SoftEdge = 0.08f,
            Spacing = 0.06f,
            AntiAlias = true,
            SizeByPressure = true,
            OpacityByPressure = true
        }
    };
}
