using Eidolon.Brush;
using Eidolon.Core;
using Eidolon.IO;

namespace Eidolon.Tests;

public class CoreTests
{
    [Fact]
    public void TileSurface_SetGetPixel()
    {
        var s = new TileSurface(100, 100);
        s.SetPixel(10, 20, ColorRgba8.FromRgb(255, 0, 0));
        var p = s.GetPixel(10, 20);
        Assert.Equal(255, p.R);
        Assert.Equal(0, p.G);
        Assert.Equal(255, p.A);
    }

    [Fact]
    public void Blend_Multiply()
    {
        float sr = 0.8f, sg = 0.2f, sb = 0.1f;
        Compositor.Blend(BlendMode.Multiply, ref sr, ref sg, ref sb, 0.4f, 0.5f, 0.6f);
        Assert.InRange(sr, 0.31f, 0.33f);
        Assert.InRange(sg, 0.09f, 0.11f);
        Assert.InRange(sb, 0.05f, 0.07f);
    }

    [Fact]
    public void Stroke_DrawsPixels()
    {
        var doc = new Document(64, 64);
        var layer = doc.ActiveRasterLayer!;
        var preset = BrushPreset.DefaultPencil();
        preset.Params.SizePx = 8;
        var session = new StrokeSession(doc, layer, preset, ColorRgba8.Black, 0);
        session.Begin(new PointerSample(0, new Float2(32, 32), 1, PointerPhase.Press));
        session.Move(new PointerSample(0.01, new Float2(40, 32), 1, PointerPhase.Move));
        var cmd = session.End();
        Assert.NotNull(cmd);
        Assert.True(layer.Surface.Tiles.Count > 0);
        Assert.True(layer.Surface.GetPixel(32, 32).A > 0);
    }

    [Fact]
    public void History_UndoRedo()
    {
        var doc = new Document(32, 32);
        var layer = doc.ActiveRasterLayer!;
        layer.Surface.SetPixel(5, 5, ColorRgba8.Black);
        var key = TileSurface.Key(0, 0);
        var after = layer.Surface.SnapshotTiles(new[] { key });
        var beforeMap = new Dictionary<long, Tile> { [key] = new Tile(Tile.DefaultSize) };
        var cmd = new TileEditCommand(layer.Id, beforeMap, after, "t");
        doc.History.PushAlreadyDone(cmd, doc);
        Assert.True(layer.Surface.GetPixel(5, 5).A > 0);
        doc.History.Undo(doc);
        Assert.Equal(0, layer.Surface.GetPixel(5, 5).A);
        doc.History.Redo(doc);
        Assert.True(layer.Surface.GetPixel(5, 5).A > 0);
    }

    [Fact]
    public void Eidolon_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "eidolon_test_" + Guid.NewGuid().ToString("N") + ".eidolon");
        try
        {
            var doc = new Document(128, 64);
            doc.ActiveRasterLayer!.Surface.SetPixel(3, 4, ColorRgba8.FromRgb(10, 20, 30));
            EidolonFileStore.Save(doc, path);
            var loaded = EidolonFileStore.Load(path);
            Assert.Equal(128, loaded.Width);
            Assert.Equal(64, loaded.Height);
            var p = loaded.ActiveRasterLayer!.Surface.GetPixel(3, 4);
            Assert.Equal(10, p.R);
            Assert.Equal(20, p.G);
            Assert.Equal(30, p.B);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LockAlpha_DoesNotPaintEmpty()
    {
        var doc = new Document(32, 32);
        var layer = doc.ActiveRasterLayer!;
        layer.Locks = LayerLocks.Transparency;
        var preset = BrushPreset.DefaultPencil();
        preset.Params.SizePx = 10;
        preset.Params.LockAlpha = true;
        var session = new StrokeSession(doc, layer, preset, ColorRgba8.Black, 0);
        session.Begin(new PointerSample(0, new Float2(16, 16), 1, PointerPhase.Press));
        session.End();
        Assert.Equal(0, layer.Surface.GetPixel(16, 16).A);
    }

    [Fact]
    public void Selection_Rect_And_Coverage()
    {
        var sel = new Selection(100, 100);
        Assert.True(sel.IsEmpty);
        Assert.Equal(1f, sel.Coverage(10, 10));
        sel.ApplyRect(new IntRect(10, 10, 20, 20), SelectionMode.Replace);
        Assert.False(sel.IsEmpty);
        Assert.Equal(1f, sel.Coverage(15, 15));
        Assert.Equal(0f, sel.Coverage(0, 0));
    }

    [Fact]
    public void Gradient_Linear_WritesPixels()
    {
        var doc = new Document(64, 64);
        var layer = doc.ActiveRasterLayer!;
        var dirty = GradientFill.Apply(
            layer,
            new Float2(0, 32),
            new Float2(63, 32),
            ColorRgba8.Black,
            ColorRgba8.White,
            GradientType.Linear,
            null,
            false);
        Assert.False(dirty.IsEmpty);
        Assert.True(layer.Surface.GetPixel(0, 32).R < 40);
        Assert.True(layer.Surface.GetPixel(63, 32).R > 200);
    }

    [Fact]
    public void Ruler_SnapRelativeDeg45()
    {
        // base 60°: pointer near +10° stays on 60; near +23° → 105; near -23° → 15
        Assert.Equal(60f, RulerState.SnapRelativeDeg45(60f, 70f), 3);
        Assert.Equal(105f, RulerState.SnapRelativeDeg45(60f, 83f), 3);
        Assert.Equal(15f, RulerState.SnapRelativeDeg45(60f, 37f), 3);
        // wrap across 0
        Assert.Equal(350f, RulerState.SnapRelativeDeg45(350f, 5f), 3); // +15° → stay
        Assert.Equal(35f, RulerState.SnapRelativeDeg45(350f, 20f), 3); // +30° → +45
        Assert.Equal(0f, RulerState.SignedDeltaDeg(0f), 3);
        Assert.Equal(-10f, RulerState.SignedDeltaDeg(350f), 3);
    }

    [Fact]
    public void Fisheye_ShiftSnap_ThetaHandle_Relative45Steps()
    {
        var r = new RulerState
        {
            Kind = RulerKind.Fisheye6,
            FishHorizonCenter = new Float2(0, 0),
            FishHorizonRadius = 100f,
            FishGlobalAngleDeg = 0f,
            FishTheta1Deg = 60f,
            FishTheta2Deg = 180f,
            FishTheta3Deg = 300f,
        };

        float baseW = r.GetFishThetaWorldDeg(RulerHandle.FishTheta1); // 60
        Assert.Equal(60f, baseW, 3);

        // Pointer ~70° from center → relative stay at 60 (delta 10)
        r.SetHandle(RulerHandle.FishTheta1,
            new Float2(MathF.Cos(70f * MathF.PI / 180f), MathF.Sin(70f * MathF.PI / 180f)),
            snapAngle45: true, snapBaseWorldDeg: baseW);
        Assert.Equal(60f, r.FishTheta1Deg, 3);

        // Pointer ~100° → +40° rounds to +45 → 105
        r.SetHandle(RulerHandle.FishTheta1,
            new Float2(MathF.Cos(100f * MathF.PI / 180f), MathF.Sin(100f * MathF.PI / 180f)),
            snapAngle45: true, snapBaseWorldDeg: baseW);
        Assert.Equal(105f, r.FishTheta1Deg, 3);

        // Continuous (no snap) keeps free angle
        r.SetHandle(RulerHandle.FishTheta1,
            new Float2(MathF.Cos(33f * MathF.PI / 180f), MathF.Sin(33f * MathF.PI / 180f)),
            snapAngle45: false);
        Assert.InRange(r.FishTheta1Deg, 32f, 34f);

        // Global offset: world base = global+local; result local = steppedWorld - global
        r.FishGlobalAngleDeg = 30f;
        r.FishTheta2Deg = 20f; // world base = 50
        float base2 = r.GetFishThetaWorldDeg(RulerHandle.FishTheta2);
        Assert.Equal(50f, base2, 3);
        // pointer world ~90° → delta +40 → +45 → world 95 → local 65
        r.SetHandle(RulerHandle.FishTheta2,
            new Float2(MathF.Cos(90f * MathF.PI / 180f), MathF.Sin(90f * MathF.PI / 180f)),
            snapAngle45: true, snapBaseWorldDeg: base2);
        Assert.Equal(65f, r.FishTheta2Deg, 3);

        Assert.True(RulerState.IsFisheyeCircleHandle(RulerHandle.FishTheta1));
        Assert.False(RulerState.IsFisheyeCircleHandle(RulerHandle.FishHorizonRim));
        Assert.False(RulerState.IsFisheyeCircleHandle(RulerHandle.FishHorizonCenter));
    }
}
