namespace Eidolon.Core;

public enum GradientType
{
    Linear,
    Radial
}

public static class GradientFill
{
    public static IntRect Apply(
        RasterLayer layer,
        Float2 p0,
        Float2 p1,
        ColorRgba8 c0,
        ColorRgba8 c1,
        GradientType type,
        Selection? selection,
        bool lockAlpha)
    {
        if ((layer.Locks & LayerLocks.Pixels) != 0) return default;
        var surface = layer.Surface;
        int w = surface.Width, h = surface.Height;

        // Affected region: full doc for simplicity, or selection bounds
        IntRect rect = selection is { IsEmpty: false }
            ? selection.Bounds.ClampTo(w, h)
            : new IntRect(0, 0, w, h);
        if (rect.IsEmpty) return default;

        float dx = p1.X - p0.X;
        float dy = p1.Y - p0.Y;
        float len2 = dx * dx + dy * dy;
        if (len2 < 1e-6f) len2 = 1e-6f;
        float radius = MathF.Sqrt(len2);

        for (int y = rect.Y; y < rect.Bottom; y++)
        for (int x = rect.X; x < rect.Right; x++)
        {
            float cov = selection?.Coverage(x, y) ?? 1f;
            if (cov <= 0.001f) continue;

            float t;
            if (type == GradientType.Radial)
            {
                float ddx = x + 0.5f - p0.X;
                float ddy = y + 0.5f - p0.Y;
                t = MathF.Sqrt(ddx * ddx + ddy * ddy) / radius;
            }
            else
            {
                float ddx = x + 0.5f - p0.X;
                float ddy = y + 0.5f - p0.Y;
                t = (ddx * dx + ddy * dy) / len2;
            }
            t = Math.Clamp(t, 0f, 1f);

            var col = Lerp(c0, c1, t);
            var dst = surface.GetPixel(x, y);
            if (lockAlpha && dst.A == 0) continue;

            float sa = (col.A / 255f) * cov;
            if (lockAlpha) sa *= dst.A / 255f;
            float da = dst.A / 255f;
            float sr = col.R / 255f, sg = col.G / 255f, sb = col.B / 255f;
            float dr = dst.R / 255f, dg = dst.G / 255f, db = dst.B / 255f;
            float oa = sa + da * (1 - sa);
            float or_, og, ob;
            if (oa <= 1e-6f) or_ = og = ob = 0;
            else
            {
                or_ = (sr * sa + dr * da * (1 - sa)) / oa;
                og = (sg * sa + dg * da * (1 - sa)) / oa;
                ob = (sb * sa + db * da * (1 - sa)) / oa;
            }
            if (lockAlpha) oa = da;
            surface.SetPixel(x, y, new ColorRgba8(
                (byte)(or_ * 255f + 0.5f),
                (byte)(og * 255f + 0.5f),
                (byte)(ob * 255f + 0.5f),
                (byte)Math.Clamp((int)(oa * 255f + 0.5f), 0, 255)));
        }
        return rect;
    }

    private static ColorRgba8 Lerp(ColorRgba8 a, ColorRgba8 b, float t)
    {
        float u = 1 - t;
        return new ColorRgba8(
            (byte)(a.R * u + b.R * t + 0.5f),
            (byte)(a.G * u + b.G * t + 0.5f),
            (byte)(a.B * u + b.B * t + 0.5f),
            (byte)(a.A * u + b.A * t + 0.5f));
    }
}
