namespace Eidolon.Core;

public readonly record struct ColorRgba8(byte R, byte G, byte B, byte A)
{
    public static ColorRgba8 Transparent => new(0, 0, 0, 0);
    public static ColorRgba8 Black => new(0, 0, 0, 255);
    public static ColorRgba8 White => new(255, 255, 255, 255);

    public static ColorRgba8 FromRgb(byte r, byte g, byte b) => new(r, g, b, 255);

    public uint ToBgra32() =>
        (uint)(B | (G << 8) | (R << 16) | (A << 24));

    public static ColorRgba8 FromBgra32(uint bgra) =>
        new(
            (byte)((bgra >> 16) & 0xFF),
            (byte)((bgra >> 8) & 0xFF),
            (byte)(bgra & 0xFF),
            (byte)((bgra >> 24) & 0xFF));
}
