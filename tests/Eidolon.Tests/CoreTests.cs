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
    public void WillowLeaf_OverlapTrue_FillsSolid()
    {
        var doc = new Document(64, 64);
        var layer = doc.ActiveRasterLayer!;
        var preset = BrushPreset.DefaultWillowLeaf();
        preset.Params.Opacity = 1f;
        var session = new StrokeSession(doc, layer, preset, ColorRgba8.Black, 0, willowOverlap: true);
        // Axis-aligned square path → closed polygon with solid interior.
        session.Begin(new PointerSample(0, new Float2(10, 10), 1, PointerPhase.Press));
        session.Move(new PointerSample(0.02, new Float2(50, 10), 1, PointerPhase.Move));
        session.Move(new PointerSample(0.04, new Float2(50, 50), 1, PointerPhase.Move));
        session.Move(new PointerSample(0.06, new Float2(10, 50), 1, PointerPhase.Move));
        var cmd = session.End();
        Assert.NotNull(cmd);
        Assert.True(layer.Surface.GetPixel(30, 30).A > 200);
        Assert.True(layer.Surface.GetPixel(2, 2).A == 0);
    }

    [Fact]
    public void WillowLeaf_OverlapFalse_SelfOverlapRestoresBase()
    {
        var doc = new Document(64, 64);
        var layer = doc.ActiveRasterLayer!;
        // Pre-paint a marker pixel that must survive outside the hole / path.
        layer.Surface.SetPixel(2, 2, ColorRgba8.FromRgb(1, 2, 3));
        var preset = BrushPreset.DefaultWillowLeaf();
        preset.Params.Opacity = 1f;
        // figure-8 / bow-tie: two triangles sharing center → even-odd hole at center band.
        // Path: (10,10)→(50,10)→(10,50)→(50,50)→ close (self-crossing hourglass).
        var session = new StrokeSession(doc, layer, preset, ColorRgba8.Black, 0, willowOverlap: false);
        session.Begin(new PointerSample(0, new Float2(10, 10), 1, PointerPhase.Press));
        session.Move(new PointerSample(0.02, new Float2(50, 10), 1, PointerPhase.Move));
        session.Move(new PointerSample(0.04, new Float2(10, 50), 1, PointerPhase.Move));
        session.Move(new PointerSample(0.06, new Float2(50, 50), 1, PointerPhase.Move));
        var cmd = session.End();
        Assert.NotNull(cmd);

        // Outer lobes should be painted (odd coverage).
        Assert.True(layer.Surface.GetPixel(20, 15).A > 200, "top-left lobe should paint");
        Assert.True(layer.Surface.GetPixel(44, 45).A > 200, "bottom-right lobe should paint");

        // Near self-crossing center: even-odd leaves a hole → pre-stroke transparent.
        // Sample a few candidates around the diagonal crossing.
        bool holeFound =
            layer.Surface.GetPixel(30, 30).A < 40 ||
            layer.Surface.GetPixel(32, 32).A < 40 ||
            layer.Surface.GetPixel(28, 28).A < 40;
        Assert.True(holeFound, "self-overlap region should restore pre-stroke (hole)");

        // Untouched pre-paint preserved.
        var keep = layer.Surface.GetPixel(2, 2);
        Assert.Equal(1, keep.R);
        Assert.Equal(2, keep.G);
        Assert.Equal(3, keep.B);
    }

    [Fact]
    public void WillowLeaf_LongPath_DoesNotHang()
    {
        var doc = new Document(128, 128);
        var layer = doc.ActiveRasterLayer!;
        var preset = BrushPreset.DefaultWillowLeaf();
        var session = new StrokeSession(doc, layer, preset, ColorRgba8.Black, 0, willowOverlap: true);
        session.Begin(new PointerSample(0, new Float2(64, 20), 1, PointerPhase.Press));
        // Dense spiral-ish path with many Move samples (would freeze without restore/throttle).
        for (int i = 1; i <= 400; i++)
        {
            double a = i * 0.12;
            float r = 8f + i * 0.12f;
            float x = 64f + r * MathF.Cos((float)a);
            float y = 64f + r * MathF.Sin((float)a);
            session.Move(new PointerSample(i * 0.001, new Float2(x, y), 1, PointerPhase.Move));
        }
        var cmd = session.End();
        Assert.NotNull(cmd);
        Assert.True(layer.Surface.Tiles.Count > 0);
    }

    [Fact]
    public void WillowLeaf_AxisRect_NoInteriorHorizontalGaps()
    {
        // Pixel-center half-open Y must not drop interior scanlines (classic 横线 gap bug).
        var doc = new Document(64, 64);
        var layer = doc.ActiveRasterLayer!;
        var preset = BrushPreset.DefaultWillowLeaf();
        preset.Params.Opacity = 1f;
        var session = new StrokeSession(doc, layer, preset, ColorRgba8.Black, 0, willowOverlap: true);
        session.Begin(new PointerSample(0, new Float2(10, 10), 1, PointerPhase.Press));
        session.Move(new PointerSample(0.02, new Float2(50, 10), 1, PointerPhase.Move));
        session.Move(new PointerSample(0.04, new Float2(50, 50), 1, PointerPhase.Move));
        session.Move(new PointerSample(0.06, new Float2(10, 50), 1, PointerPhase.Move));
        Assert.NotNull(session.End());

        // Interior column: every row whose pixel center is inside [10,50) must be solid.
        for (int y = 10; y <= 49; y++)
            Assert.True(layer.Surface.GetPixel(30, y).A > 200, $"gap at y={y}");
        // Outside top/bottom half-open bounds stay empty.
        Assert.Equal(0, layer.Surface.GetPixel(30, 9).A);
        Assert.Equal(0, layer.Surface.GetPixel(30, 50).A);
    }

    [Fact]
    public void WillowLeaf_NearHorizontalEdge_CoversConsecutiveRows()
    {
        // Slightly non-horizontal top/bottom edges (old Ceiling(topY) row indexing skipped rows).
        var doc = new Document(64, 64);
        var layer = doc.ActiveRasterLayer!;
        var preset = BrushPreset.DefaultWillowLeaf();
        preset.Params.Opacity = 1f;
        var session = new StrokeSession(doc, layer, preset, ColorRgba8.Black, 0, willowOverlap: true);
        session.Begin(new PointerSample(0, new Float2(8, 12.2f), 1, PointerPhase.Press));
        session.Move(new PointerSample(0.02, new Float2(56, 12.8f), 1, PointerPhase.Move));
        session.Move(new PointerSample(0.04, new Float2(56, 40.3f), 1, PointerPhase.Move));
        session.Move(new PointerSample(0.06, new Float2(8, 39.7f), 1, PointerPhase.Move));
        Assert.NotNull(session.End());

        // Mid band should be continuous — no single missing horizontal line.
        int gaps = 0;
        for (int y = 14; y <= 38; y++)
        {
            if (layer.Surface.GetPixel(32, y).A < 200)
                gaps++;
        }
        Assert.True(gaps == 0, $"near-horizontal edges left {gaps} interior gaps");
    }

    [Fact]
    public void Stabilizer_PressureIsSeparateChannel()
    {
        var stab = new Stabilizer();
        stab.Reset(0.85f);

        // First sample seeds both channels independently (shared timestamp).
        var p0 = stab.Filter(new Float2(0, 0), 0.0);
        float pr0 = stab.FilterPressure(1f, 0.0);
        Assert.Equal(0f, p0.X);
        Assert.Equal(1f, pr0);

        // Jump position and drop pressure: each 1€ channel smooths on its own.
        var p1 = stab.Filter(new Float2(100, 0), 1.0 / 120.0);
        float pr1 = stab.FilterPressure(0.1f, 1.0 / 120.0);
        Assert.InRange(p1.X, 1f, 99f);
        Assert.InRange(pr1, 0.11f, 0.99f);
        Assert.True(pr1 > 0.1f); // still lagging toward 0.1, not raw
        Assert.True(p1.X < 100f);
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
