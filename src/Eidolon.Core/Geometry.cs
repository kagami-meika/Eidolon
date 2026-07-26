namespace Eidolon.Core;

public readonly record struct Int2(int X, int Y);

public readonly record struct Float2(float X, float Y)
{
    public static Float2 operator +(Float2 a, Float2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Float2 operator -(Float2 a, Float2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Float2 operator *(Float2 a, float s) => new(a.X * s, a.Y * s);
    public static Float2 operator *(float s, Float2 a) => new(a.X * s, a.Y * s);
}

public readonly record struct IntRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static IntRect FromMinMax(int minX, int minY, int maxX, int maxY)
    {
        if (maxX < minX || maxY < minY)
            return default;
        return new IntRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public static IntRect Union(IntRect a, IntRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        int minX = Math.Min(a.X, b.X);
        int minY = Math.Min(a.Y, b.Y);
        int maxX = Math.Max(a.Right, b.Right) - 1;
        int maxY = Math.Max(a.Bottom, b.Bottom) - 1;
        return FromMinMax(minX, minY, maxX, maxY);
    }

    public IntRect Inflate(int px) =>
        new(X - px, Y - px, Width + px * 2, Height + px * 2);

    public IntRect ClampTo(int width, int height)
    {
        int x0 = Math.Clamp(X, 0, width);
        int y0 = Math.Clamp(Y, 0, height);
        int x1 = Math.Clamp(Right, 0, width);
        int y1 = Math.Clamp(Bottom, 0, height);
        if (x1 <= x0 || y1 <= y0) return default;
        return new IntRect(x0, y0, x1 - x0, y1 - y0);
    }

    public bool Contains(int x, int y) =>
        x >= X && y >= Y && x < Right && y < Bottom;
}
