using Eidolon.Core;

namespace Eidolon.Brush;

public sealed class StrokeSession
{
    private readonly Document _doc;
    private readonly RasterLayer _layer;
    private readonly BrushPreset _preset;
    private readonly ColorRgba8 _color;
    private readonly Stabilizer _stabilizer = new();
    private readonly HashSet<long> _touchedKeys = new();
    private readonly Dictionary<long, Tile> _before = new();

    private Float2? _lastDabPos;
    private float _carryDist;
    private bool _active;
    private readonly float _globalStabilizer;
    private Float2 _prevCenter;
    private bool _hasPrevCenter;

    public StrokeSession(Document doc, RasterLayer layer, BrushPreset preset, ColorRgba8 color, float globalStabilizer = 0.35f)
    {
        _doc = doc;
        _layer = layer;
        _preset = preset;
        _color = color;
        _globalStabilizer = globalStabilizer;
    }

    public bool IsActive => _active;
    public IntRect DirtyRect { get; private set; }

    public void Begin(in PointerSample sample)
    {
        _active = true;
        _touchedKeys.Clear();
        _before.Clear();
        _lastDabPos = null;
        _carryDist = 0;
        DirtyRect = default;
        _hasPrevCenter = false;
        float s = _preset.Params.StabilizerStrength > 0
            ? _preset.Params.StabilizerStrength
            : _globalStabilizer;
        _stabilizer.Reset(s);
        Move(sample with { Phase = PointerPhase.Press });
    }

    public IntRect Move(in PointerSample sample)
    {
        if (!_active) return default;
        if ((_layer.Locks & LayerLocks.Pixels) != 0) return default;

        var p = _stabilizer.Filter(sample.DocumentPos);
        float pressure = Math.Clamp(sample.Pressure, 0.001f, 1f);

        float size = EffectiveSize(pressure);
        float spacing = Math.Max(0.25f, size * Math.Max(0.02f, _preset.Params.Spacing));

        if (_lastDabPos is null)
        {
            Stamp(p, pressure, size);
            _lastDabPos = p;
            return DirtyRect;
        }

        var last = _lastDabPos.Value;
        float dx = p.X - last.X;
        float dy = p.Y - last.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 1e-4f) return DirtyRect;

        float total = dist + _carryDist;
        float pos = spacing - _carryDist;

        while (pos <= dist)
        {
            float t = pos / dist;
            var dabPos = new Float2(last.X + dx * t, last.Y + dy * t);
            Stamp(dabPos, pressure, EffectiveSize(pressure));
            pos += spacing;
        }

        _carryDist = total % spacing;
        _lastDabPos = p;
        return DirtyRect;
    }

    public TileEditCommand? End()
    {
        if (!_active) return null;
        _active = false;
        if (_touchedKeys.Count == 0) return null;

        var after = new Dictionary<long, Tile>();
        foreach (var k in _touchedKeys)
        {
            if (_layer.Surface.TryGetTile((int)(k >> 32), (int)(k & 0xFFFFFFFF), out var tile))
                after[k] = tile.Clone();
            else
                after[k] = new Tile(_layer.Surface.TileSize);
        }

        return new TileEditCommand(_layer.Id, _before, after, "Stroke");
    }

    private float EffectiveSize(float pressure)
    {
        float min = Math.Clamp(_preset.Params.MinSizeRatio, 0f, 1f);
        float p = Math.Clamp(pressure, 0f, 1f);
        if (!_preset.Params.SizeByPressure)
            p = 1f;
        else
            p = MathF.Pow(p, 0.9f);
        float s = _preset.Params.SizePx * (min + (1f - min) * p);
        return Math.Max(0.5f, s);
    }

    private void EnsureBefore(long key)
    {
        if (_before.ContainsKey(key)) return;
        int tx = (int)(key >> 32);
        int ty = (int)(key & 0xFFFFFFFF);
        if (_layer.Surface.TryGetTile(tx, ty, out var tile))
            _before[key] = tile.Clone();
        else
            _before[key] = new Tile(_layer.Surface.TileSize);
        _touchedKeys.Add(key);
    }

    private void Stamp(Float2 center, float pressure, float size)
    {
        bool erase = _preset.Params.EraseMode;
        float pressureOp = Math.Clamp(pressure, 0.001f, 1f);
        float flow = _preset.Params.Flow;
        if (_preset.Params.FlowByPressure)
            flow *= 0.2f + 0.8f * pressureOp;
        float opMul = 1f;
        if (_preset.Params.OpacityByPressure)
            opMul = 0.12f + 0.88f * MathF.Pow(pressureOp, 0.95f);
        float opacity = Math.Clamp(_preset.Params.Opacity * flow * opMul, 0f, 1f);
        if (_preset.Kind == BrushToolKind.Airbrush)
            opacity = Math.Clamp(_preset.Params.Opacity * flow * (0.12f + 0.88f * pressureOp), 0f, 1f);

        float hardness = Math.Clamp(_preset.Params.Hardness, 0f, 1f);
        float soft = Math.Clamp(_preset.Params.SoftEdge, 0f, 1f);
        bool aa = _preset.Params.AntiAlias;
        bool lockAlpha = _preset.Params.LockAlpha ||
                         (_layer.Locks & LayerLocks.Transparency) != 0;
        bool binary = _preset.Kind == BrushToolKind.Binary;

        float r = size * 0.5f;
        float aaPad = aa ? 1.25f : 0.01f;
        int minX = (int)MathF.Floor(center.X - r - aaPad);
        int maxX = (int)MathF.Ceiling(center.X + r + aaPad);
        int minY = (int)MathF.Floor(center.Y - r - aaPad);
        int maxY = (int)MathF.Ceiling(center.Y + r + aaPad);
        minX = Math.Clamp(minX, 0, _layer.Surface.Width - 1);
        maxX = Math.Clamp(maxX, 0, _layer.Surface.Width - 1);
        minY = Math.Clamp(minY, 0, _layer.Surface.Height - 1);
        maxY = Math.Clamp(maxY, 0, _layer.Surface.Height - 1);
        if (maxX < minX || maxY < minY) return;

        int ts = _layer.Surface.TileSize;
        var rect = IntRect.FromMinMax(minX, minY, maxX, maxY);
        DirtyRect = DirtyRect.IsEmpty ? rect : IntRect.Union(DirtyRect, rect);

        // Soft profile: high hardness => thin feather, no dark ring
        float featherStart = Math.Clamp(hardness * (1f - soft * 0.35f), 0f, 0.98f);
        float sr0 = _color.R / 255f, sg0 = _color.G / 255f, sb0 = _color.B / 255f;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x + 0.5f - center.X;
                float dy = y + 0.5f - center.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                float cover = DiskCoverage(dist, r, aa ? 1f : 0.02f);
                if (cover <= 0.001f) continue;

                float nd = dist / Math.Max(r, 1e-4f);
                float profile;
                if (nd <= featherStart) profile = 1f;
                else if (nd >= 1f) profile = 0f;
                else
                {
                    float t = (nd - featherStart) / (1f - featherStart);
                    profile = 1f - (t * t * t * (t * (t * 6f - 15f) + 10f));
                }

                cover *= profile;
                if (binary) cover = cover >= 0.5f ? 1f : 0f;

                // Texture / fly-white (procedural value noise)
                float texAmt = Math.Clamp(_preset.Params.TextureStrength, 0f, 1f);
                if (texAmt > 0.001f)
                {
                    float sc = Math.Max(0.05f, _preset.Params.TextureScale);
                    float g = HashGrain(x, y, _preset.Params.TextureSeed, sc);
                    // high grain => more skip (飞白)
                    float keep = 1f - texAmt * (1f - g);
                    cover *= Math.Clamp(keep, 0f, 1f);
                }

                float a = cover * opacity;
                float sel = _doc.Selection.Coverage(x, y);
                if (sel <= 0.001f) continue;
                a *= sel;
                if (a <= 0.001f) continue;

                int tx = x / ts;
                int ty = y / ts;
                long key = TileSurface.Key(tx, ty);
                EnsureBefore(key);
                var tile = _layer.Surface.GetOrCreateTile(tx, ty);
                int lx = x - tx * ts;
                int ly = y - ty * ts;
                int idx = ly * ts + lx;
                var dst = tile.Pixels[idx];

                if (erase)
                {
                    float eraseA = dst.A / 255f * (1f - a);
                    tile.Pixels[idx] = new ColorRgba8(dst.R, dst.G, dst.B,
                        (byte)Math.Clamp((int)(eraseA * 255f + 0.5f), 0, 255));
                    tile.Version++;
                    continue;
                }

                if (_preset.Kind == BrushToolKind.Smudge)
                {
                    // Pull color from opposite of stroke direction
                    float str = Math.Clamp(_preset.Params.SmudgeStrength, 0f, 1f) * a;
                    float sx = center.X, sy = center.Y;
                    if (_hasPrevCenter)
                    {
                        float pdx = center.X - _prevCenter.X;
                        float pdy = center.Y - _prevCenter.Y;
                        float plen = MathF.Sqrt(pdx * pdx + pdy * pdy);
                        if (plen > 1e-3f)
                        {
                            // sample upstream
                            float sampleDist = Math.Min(r * 0.85f, 12f);
                            sx = x + 0.5f - pdx / plen * sampleDist;
                            sy = y + 0.5f - pdy / plen * sampleDist;
                        }
                    }
                    int ix = (int)MathF.Floor(sx);
                    int iy = (int)MathF.Floor(sy);
                    var srcPix = _layer.Surface.GetPixel(ix, iy);
                    float dr0 = dst.R / 255f, dg0 = dst.G / 255f, db0 = dst.B / 255f, da0 = dst.A / 255f;
                    float sr0s = srcPix.R / 255f, sg0s = srcPix.G / 255f, sb0s = srcPix.B / 255f, sa0s = srcPix.A / 255f;
                    float tmix = Math.Clamp(str, 0f, 1f);
                    float or_ = dr0 * (1 - tmix) + sr0s * tmix;
                    float og = dg0 * (1 - tmix) + sg0s * tmix;
                    float ob = db0 * (1 - tmix) + sb0s * tmix;
                    float oa0 = da0 * (1 - tmix * 0.35f) + sa0s * (tmix * 0.35f);
                    if (lockAlpha) oa0 = da0;
                    tile.Pixels[idx] = new ColorRgba8(
                        (byte)(or_ * 255f + 0.5f),
                        (byte)(og * 255f + 0.5f),
                        (byte)(ob * 255f + 0.5f),
                        (byte)Math.Clamp((int)(oa0 * 255f + 0.5f), 0, 255));
                    tile.Version++;
                    continue;
                }

                if (lockAlpha && dst.A == 0) continue;

                float sa = a;
                if (lockAlpha) sa *= dst.A / 255f;

                float da = dst.A / 255f;
                float sr = sr0, sg = sg0, sb = sb0;
                float dr = dst.R / 255f, dg = dst.G / 255f, db = dst.B / 255f;

                if (_preset.Params.Blend > 0.001f && da > 0.001f)
                {
                    float bamt = Math.Clamp(_preset.Params.Blend, 0f, 1f) * pressureOp;
                    sr = sr * (1 - bamt) + dr * bamt;
                    sg = sg * (1 - bamt) + dg * bamt;
                    sb = sb * (1 - bamt) + db * bamt;
                }

                // Straight-alpha over with constant source RGB (no black fringe in layer storage)
                float oa = sa + da * (1f - sa);
                float outR, outG, outB;
                if (oa <= 1e-6f)
                {
                    outR = outG = outB = 0;
                }
                else
                {
                    outR = (sr * sa + dr * da * (1f - sa)) / oa;
                    outG = (sg * sa + dg * da * (1f - sa)) / oa;
                    outB = (sb * sa + db * da * (1f - sa)) / oa;
                }

                if (lockAlpha) oa = da;

                outR = Math.Clamp(outR, 0f, 1f);
                outG = Math.Clamp(outG, 0f, 1f);
                outB = Math.Clamp(outB, 0f, 1f);

                tile.Pixels[idx] = new ColorRgba8(
                    (byte)(outR * 255f + 0.5f),
                    (byte)(outG * 255f + 0.5f),
                    (byte)(outB * 255f + 0.5f),
                    (byte)Math.Clamp((int)(oa * 255f + 0.5f), 0, 255));
                tile.Version++;
            }
        }
        _prevCenter = center;
        _hasPrevCenter = true;
    }

    private static float HashGrain(int x, int y, int seed, float scale)
    {
        // value noise 0..1
        float fx = x * scale * 0.15f;
        float fy = y * scale * 0.15f;
        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);
        float tx = fx - x0;
        float ty = fy - y0;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        float v00 = Hash2(x0, y0, seed);
        float v10 = Hash2(x0 + 1, y0, seed);
        float v01 = Hash2(x0, y0 + 1, seed);
        float v11 = Hash2(x0 + 1, y0 + 1, seed);
        float v0 = v00 * (1 - tx) + v10 * tx;
        float v1 = v01 * (1 - tx) + v11 * tx;
        return v0 * (1 - ty) + v1 * ty;
    }

    private static float Hash2(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 362437);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    private static float DiskCoverage(float d, float r, float aaWidth)
    {
        if (d <= r - aaWidth) return 1f;
        if (d >= r + aaWidth) return 0f;
        float t = (d - (r - aaWidth)) / Math.Max(2f * aaWidth, 1e-4f);
        t = Math.Clamp(t, 0f, 1f);
        return 1f - (t * t * (3f - 2f * t));
    }
}