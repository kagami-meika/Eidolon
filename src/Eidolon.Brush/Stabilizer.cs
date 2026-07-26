using Eidolon.Core;

namespace Eidolon.Brush;

/// <summary>Delayed follow stabilizer (SAI-like lag).</summary>
public sealed class Stabilizer
{
    private Float2 _smoothed;
    private bool _has;
    private float _strength; // 0..1

    public void Reset(float strength)
    {
        _strength = Math.Clamp(strength, 0f, 1f);
        _has = false;
    }

    public Float2 Filter(Float2 raw)
    {
        if (_strength <= 0.001f)
        {
            _smoothed = raw;
            _has = true;
            return raw;
        }

        if (!_has)
        {
            _smoothed = raw;
            _has = true;
            return raw;
        }

        // Higher strength = more lag. tau-like blend factor.
        float t = 1f - MathF.Pow(_strength, 1.2f) * 0.92f;
        t = Math.Clamp(t, 0.02f, 1f);
        _smoothed = new Float2(
            _smoothed.X + (raw.X - _smoothed.X) * t,
            _smoothed.Y + (raw.Y - _smoothed.Y) * t);
        return _smoothed;
    }
}
