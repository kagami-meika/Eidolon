namespace Eidolon.Core;

public static class ColorModels
{
    public static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        v = max;
        float d = max - min;
        s = max <= 1e-6f ? 0 : d / max;
        if (d <= 1e-6f) { h = 0; return; }
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6f;
    }

    public static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        h = ((h % 1f) + 1f) % 1f;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        if (s <= 1e-6f) { r = g = b = v; return; }
        float i = MathF.Floor(h * 6f);
        float f = h * 6f - i;
        float p = v * (1 - s);
        float q = v * (1 - f * s);
        float t = v * (1 - (1 - f) * s);
        switch ((int)i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
    }

    public static void RgbToHsl(float r, float g, float b, out float h, out float s, out float l)
    {
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        l = (max + min) * 0.5f;
        if (MathF.Abs(max - min) < 1e-6f) { h = s = 0; return; }
        float d = max - min;
        s = l > 0.5f ? d / (2 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6f;
    }

    public static void HslToRgb(float h, float s, float l, out float r, out float g, out float b)
    {
        h = ((h % 1f) + 1f) % 1f;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s <= 1e-6f) { r = g = b = l; return; }
        float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
        float p = 2 * l - q;
        r = Hue2Rgb(p, q, h + 1f / 3f);
        g = Hue2Rgb(p, q, h);
        b = Hue2Rgb(p, q, h - 1f / 3f);
    }

    private static float Hue2Rgb(float p, float q, float t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1f / 6f) return p + (q - p) * 6 * t;
        if (t < 0.5f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6;
        return p;
    }

    public static void RgbToOklch(float r, float g, float b, out float L, out float C, out float H)
    {
        float lr = SrgbToLinear(r), lg = SrgbToLinear(g), lb = SrgbToLinear(b);
        float l_ = MathF.Cbrt(0.4122214708f * lr + 0.5363325363f * lg + 0.0514459929f * lb);
        float m_ = MathF.Cbrt(0.2119034982f * lr + 0.6806995451f * lg + 0.1073969566f * lb);
        float s_ = MathF.Cbrt(0.0883024619f * lr + 0.2817188376f * lg + 0.6299787005f * lb);
        float labL = 0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_;
        float labA = 1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_;
        float labB = 0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_;
        L = labL;
        C = MathF.Sqrt(labA * labA + labB * labB);
        H = MathF.Atan2(labB, labA);
        if (H < 0) H += MathF.PI * 2f;
    }

    public static void OklchToRgb(float L, float C, float H, out float r, out float g, out float b)
    {
        OklchToRgbChecked(L, C, H, out r, out g, out b);
    }

    /// <summary>Returns false if linear RGB was outside 0..1 before clamp (out of sRGB gamut).</summary>
    public static bool OklchToRgbChecked(float L, float C, float H, out float r, out float g, out float b)
    {
        float labA = C * MathF.Cos(H);
        float labB = C * MathF.Sin(H);
        float l_ = L + 0.3963377774f * labA + 0.2158037573f * labB;
        float m_ = L - 0.1055613458f * labA - 0.0638541728f * labB;
        float s_ = L - 0.0894841775f * labA - 1.2914855480f * labB;
        l_ = l_ * l_ * l_;
        m_ = m_ * m_ * m_;
        s_ = s_ * s_ * s_;
        float lr = +4.0767416621f * l_ - 3.3077115913f * m_ + 0.2309699292f * s_;
        float lg = -1.2684380046f * l_ + 2.6097574011f * m_ - 0.3413193965f * s_;
        float lb = -0.0041960863f * l_ - 0.7034186147f * m_ + 1.7076147010f * s_;
        bool gamut = lr >= -1e-5f && lr <= 1f + 1e-5f
                  && lg >= -1e-5f && lg <= 1f + 1e-5f
                  && lb >= -1e-5f && lb <= 1f + 1e-5f;
        r = Math.Clamp(LinearToSrgb(lr), 0, 1);
        g = Math.Clamp(LinearToSrgb(lg), 0, 1);
        b = Math.Clamp(LinearToSrgb(lb), 0, 1);
        return gamut;
    }

    public static float SrgbToLinear(float c) =>
        c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    public static float LinearToSrgb(float c)
    {
        c = Math.Clamp(c, 0, 1);
        return c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

    public static ColorRgba8 FromFloatRgb(float r, float g, float b, byte a = 255) =>
        new(
            (byte)Math.Clamp((int)(r * 255f + 0.5f), 0, 255),
            (byte)Math.Clamp((int)(g * 255f + 0.5f), 0, 255),
            (byte)Math.Clamp((int)(b * 255f + 0.5f), 0, 255),
            a);
}