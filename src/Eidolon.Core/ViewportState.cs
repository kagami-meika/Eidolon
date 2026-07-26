using System.Numerics;

namespace Eidolon.Core;

public sealed class ViewportState
{
    public Float2 Pan { get; set; }
    public float Scale { get; set; } = 1f;
    public float RotationDeg { get; set; }
    public bool MirrorX { get; set; }

    public Matrix3x2 CreateMatrix(float viewWidth, float viewHeight, int docWidth, int docHeight)
    {
        // Center of view
        float cx = viewWidth * 0.5f;
        float cy = viewHeight * 0.5f;
        float dx = docWidth * 0.5f;
        float dy = docHeight * 0.5f;

        var m = Matrix3x2.CreateTranslation(-dx, -dy);
        if (MirrorX)
            m *= Matrix3x2.CreateScale(-1, 1);
        m *= Matrix3x2.CreateRotation(RotationDeg * MathF.PI / 180f);
        m *= Matrix3x2.CreateScale(Scale, Scale);
        m *= Matrix3x2.CreateTranslation(cx + Pan.X, cy + Pan.Y);
        return m;
    }

    public Float2 ScreenToDocument(Float2 screen, float viewWidth, float viewHeight, int docWidth, int docHeight)
    {
        var m = CreateMatrix(viewWidth, viewHeight, docWidth, docHeight);
        if (!Matrix3x2.Invert(m, out var inv))
            return new Float2(0, 0);
        var v = Vector2.Transform(new Vector2(screen.X, screen.Y), inv);
        return new Float2(v.X, v.Y);
    }

    public Float2 DocumentToScreen(Float2 doc, float viewWidth, float viewHeight, int docWidth, int docHeight)
    {
        var m = CreateMatrix(viewWidth, viewHeight, docWidth, docHeight);
        var v = Vector2.Transform(new Vector2(doc.X, doc.Y), m);
        return new Float2(v.X, v.Y);
    }

    public void ZoomAt(float factor, Float2 screenPivot, float viewWidth, float viewHeight, int docWidth, int docHeight)
    {
        var before = ScreenToDocument(screenPivot, viewWidth, viewHeight, docWidth, docHeight);
        Scale = Math.Clamp(Scale * factor, 0.02f, 64f);
        var afterScreen = DocumentToScreen(before, viewWidth, viewHeight, docWidth, docHeight);
        Pan = new Float2(Pan.X + (screenPivot.X - afterScreen.X), Pan.Y + (screenPivot.Y - afterScreen.Y));
    }
}

public readonly record struct PointerSample(
    double TimeSec,
    Float2 DocumentPos,
    float Pressure,
    PointerPhase Phase,
    float TiltX = 0,
    float TiltY = 0,
    float Rotation = 0,
    bool IsEraser = false);

public enum PointerPhase
{
    Hover,
    Press,
    Move,
    Release,
    Cancel
}
