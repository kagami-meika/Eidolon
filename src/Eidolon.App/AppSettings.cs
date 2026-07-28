using System.IO;
using System.Text.Json;
using Eidolon.App.Logging;
using Eidolon.Core;

namespace Eidolon.App;

/// <summary>Persisted brush / tool options (tools panel).</summary>
public sealed class BrushToolSettings
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
    public bool LockAlpha { get; set; }
    public float StabilizerStrength { get; set; }
    public bool SizeByPressure { get; set; } = true;
    public bool OpacityByPressure { get; set; } = true;
    public bool FlowByPressure { get; set; }
    public float TextureStrength { get; set; }
    public float TextureScale { get; set; } = 1f;
    public int TextureSeed { get; set; } = 1;
    public float SmudgeStrength { get; set; } = 0.55f;
    public bool StraightLineMode { get; set; }
}

/// <summary>Persisted foreground / background painting colors.</summary>
public sealed class ColorToolSettings
{
    public byte FgR { get; set; }
    public byte FgG { get; set; }
    public byte FgB { get; set; }
    public byte BgR { get; set; } = 255;
    public byte BgG { get; set; } = 255;
    public byte BgB { get; set; } = 255;
}

public sealed class AppSettings
{
    public int DefaultCanvasWidth { get; set; } = 1920;
    public int DefaultCanvasHeight { get; set; } = 1080;
    /// <summary>0 RGB 1 HSV 2 HSL 3 OKLCH</summary>
    public int DefaultColorModel { get; set; } = 3;
    public bool TimelapseEnabled { get; set; } = true;
    public string TimelapseDirectory { get; set; } = "";
    public string TimelapseFileName { get; set; } = "timelapse";
    public int TimelapseFps { get; set; } = 30;
    public string Language { get; set; } = "cn";
    /// <summary>Minimum log level (cold-switchable from Settings).</summary>
    public string LogLevel { get; set; } = "Info";

    // Brush
    /// <summary>WillowLeaf: when true, closed region is solid-filled (non-zero winding).
    /// When false, self-overlap inverts vs pre-stroke (even-odd / XOR membership:
    /// blank → paint, double-covered → restore pre-stroke).</summary>
    public bool WillowOverlap { get; set; } = true;

    // Export
    /// <summary>true = use JPEG quality compression; false = max quality (~100)</summary>
    public bool JpegCompress { get; set; } = true;
    /// <summary>JPEG quality 1-100</summary>
    public int JpegQuality { get; set; } = 90;
    /// <summary>WebP quality 1-100 (lossy). 100 may still be lossy depending on encoder.</summary>
    public int WebpQuality { get; set; } = 90;
    public bool WebpLossless { get; set; } = false;
    /// <summary>Export with transparency when format supports it</summary>
    public bool ExportPreserveTransparency { get; set; } = true;

    /// <summary>Tools panel brush parameters (persisted under %APPDATA%/Eidolon/).</summary>
    public BrushToolSettings Brush { get; set; } = new();
    /// <summary>Foreground / background colors (persisted under %APPDATA%/Eidolon/).</summary>
    public ColorToolSettings Colors { get; set; } = new();

    /// <summary>Perspective ruler per-line snap toggles (channel 0/1/2).</summary>
    public bool RulerLineSnap0 { get; set; } = true;
    public bool RulerLineSnap1 { get; set; } = true;
    public bool RulerLineSnap2 { get; set; } = true;

    /// <summary>Fisheye6 reference point P mode: "Off" / "VisualOnly" / "Snappable".</summary>
    public string FisheyePMode { get; set; } = "Off";
    /// <summary>Fisheye6 reference point P position (doc coords).</summary>
    public double FisheyePX { get; set; } = 400;
    public double FisheyePY { get; set; } = 200;

    /// <summary>Persisted ruler geometry (JSON-serialized RulerState, excludes transient fields).</summary>
    public string RulerGeometry { get; set; } = "";

    private static readonly JsonSerializerOptions RulerJsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = false
    };

    /// <summary>Serialize persistent ruler state to JSON string.</summary>
    public static string SerializeRulerState(RulerState r)
    {
        try { return JsonSerializer.Serialize(r.Clone(), RulerJsonOpts); }
        catch { return ""; }
    }

    /// <summary>Apply persisted ruler geometry onto a RulerState (non-destructive for missing keys).</summary>
    public static void DeserializeRulerState(string json, RulerState target)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var src = JsonSerializer.Deserialize<RulerState>(json, RulerJsonOpts);
            if (src is not null)
                target.CopyFrom(src);
        }
        catch { }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Eidolon");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "setting.json");

    public static AppSettings Current { get; private set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                if (s is not null)
                {
                    Current = s;
                    Normalize(Current);
                    AppLog.Info($"Settings loaded: {SettingsPath}", "Settings");
                    return Current;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Settings load failed", "Settings");
        }

        Current = new AppSettings();
        Normalize(Current);
        return Current;
    }

    public static void Save(AppSettings? settings = null)
    {
        settings ??= Current;
        Normalize(settings);
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
            Current = settings;
            AppLog.Info($"Settings saved: {SettingsPath}", "Settings");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Settings save failed", "Settings");
        }
    }

    private static void Normalize(AppSettings s)
    {
        s.DefaultCanvasWidth = Math.Clamp(s.DefaultCanvasWidth, 1, 100000);
        s.DefaultCanvasHeight = Math.Clamp(s.DefaultCanvasHeight, 1, 100000);
        s.DefaultColorModel = Math.Clamp(s.DefaultColorModel, 0, 3);
        s.TimelapseFps = Math.Clamp(s.TimelapseFps, 1, 60);
        s.JpegQuality = Math.Clamp(s.JpegQuality, 1, 100);
        s.WebpQuality = Math.Clamp(s.WebpQuality, 1, 100);
        if (string.IsNullOrWhiteSpace(s.TimelapseFileName))
            s.TimelapseFileName = "timelapse";
        if (string.IsNullOrWhiteSpace(s.TimelapseDirectory))
            s.TimelapseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Eidolon", "Timelapse");
        if (s.Language is not ("cn" or "en"))
            s.Language = "cn";
        if (s.LogLevel is not ("Trace" or "Debug" or "Info" or "Warn" or "Error"))
            s.LogLevel = "Info";

        s.Brush ??= new BrushToolSettings();
        s.Colors ??= new ColorToolSettings();
        NormalizeBrush(s.Brush);
    }

    private static void NormalizeBrush(BrushToolSettings b)
    {
        b.SizePx = Math.Clamp(b.SizePx, 1f, 500f);
        b.MinSizeRatio = Math.Clamp(b.MinSizeRatio, 0f, 1f);
        b.Opacity = Math.Clamp(b.Opacity, 0f, 1f);
        b.Flow = Math.Clamp(b.Flow, 0f, 1f);
        b.Hardness = Math.Clamp(b.Hardness, 0f, 1f);
        b.SoftEdge = Math.Clamp(b.SoftEdge, 0f, 1f);
        b.Blend = Math.Clamp(b.Blend, 0f, 1f);
        b.Spacing = Math.Clamp(b.Spacing, 0.01f, 2f);
        b.StabilizerStrength = Math.Clamp(b.StabilizerStrength, 0f, 1f);
        b.TextureStrength = Math.Clamp(b.TextureStrength, 0f, 1f);
        b.TextureScale = Math.Clamp(b.TextureScale, 0.01f, 16f);
        b.SmudgeStrength = Math.Clamp(b.SmudgeStrength, 0f, 1f);
    }
}
