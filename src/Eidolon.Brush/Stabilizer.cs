using Eidolon.Core;

namespace Eidolon.Brush;

/// <summary>
/// Stroke smoother based on the 1€ Filter (Casiez et al.).
/// Low jitter at slow speeds, low lag at high speeds — position and pressure
/// are independent channels that share the UI strength mapping only.
/// </summary>
public sealed class Stabilizer
{
    private readonly OneEuro2D _pos = new();
    private readonly OneEuro1D _pressure = new();
    private float _strength;
    private double _frameTime;
    private float _frameDt = 1f / 120f;
    private bool _hasFrame;

    public void Reset(float strength)
    {
        _strength = Math.Clamp(strength, 0f, 1f);
        _pos.Reset();
        _pressure.Reset();
        _frameTime = 0;
        _frameDt = 1f / 120f;
        _hasFrame = false;
        ApplyStrengthParams();
    }

    /// <summary>Filter document-space position. <paramref name="timeSec"/> is absolute time (e.g. PointerSample.TimeSec).</summary>
    public Float2 Filter(Float2 raw, double timeSec)
    {
        float dt = DtFor(timeSec);
        if (_strength <= 0.001f)
        {
            _pos.Seed(raw.X, raw.Y);
            return raw;
        }

        return _pos.Filter(raw.X, raw.Y, dt);
    }

    /// <summary>Smooth a pressure sample (0..1). Independent of <see cref="Filter"/>; same timestamp reuses dt.</summary>
    public float FilterPressure(float raw, double timeSec)
    {
        raw = Math.Clamp(raw, 0f, 1f);
        float dt = DtFor(timeSec);
        if (_strength <= 0.001f)
        {
            _pressure.Seed(raw);
            return raw;
        }

        return Math.Clamp(_pressure.Filter(raw, dt), 0f, 1f);
    }

    /// <summary>Backward-compatible entry (assumes ~120 Hz) — prefer timed overloads.</summary>
    public Float2 Filter(Float2 raw) => Filter(raw, NextSyntheticTime());

    /// <summary>Backward-compatible entry (assumes ~120 Hz) — prefer timed overloads.</summary>
    public float FilterPressure(float raw) => FilterPressure(raw, NextSyntheticTime());

    private double NextSyntheticTime()
    {
        if (!_hasFrame)
            return 0;
        // New untimed call = new frame (tests that interleave pos/pressure should use timed API).
        return _frameTime + 1.0 / 120.0;
    }

    /// <summary>
    /// Resolve dt for this sample time. Position and pressure for the same
    /// timestamp share one dt so dual-channel filtering does not double-step.
    /// </summary>
    private float DtFor(double timeSec)
    {
        if (_hasFrame && AlmostEqual(timeSec, _frameTime))
            return _frameDt;

        if (!_hasFrame)
        {
            _hasFrame = true;
            _frameTime = timeSec;
            _frameDt = 1f / 120f;
            return _frameDt;
        }

        double rawDt = timeSec - _frameTime;
        if (rawDt <= 0)
        {
            // Non-monotonic clock: keep a safe default without inventing motion.
            _frameTime = timeSec;
            _frameDt = 1f / 120f;
            return _frameDt;
        }

        _frameTime = timeSec;
        _frameDt = (float)Math.Clamp(rawDt, 1.0 / 1000.0, 0.1);
        return _frameDt;
    }

    private static bool AlmostEqual(double a, double b) => Math.Abs(a - b) <= 1e-9;

    /// <summary>
    /// Map UI strength 0..1 → 1€ parameters.
    /// Higher strength = lower min-cutoff (more smooth when slow).
    /// Beta stays relatively high so fast strokes do not feel dragged.
    /// </summary>
    private void ApplyStrengthParams()
    {
        float s = _strength;
        // mincutoff (Hz): higher floor so slow/medium strokes feel direct.
        // At default strength≈0.35 this gives ~7.9 Hz → τ≈20 ms (3× faster than before).
        float minCutoff = Lerp(10.0f, 2.0f, SmoothStep(s));
        // beta: speed-adaptive lag reduction — fast strokes follow tightly.
        float beta = Lerp(2.5f, 0.25f, s);
        // dCutoff: smoother derivative estimate avoids jitter in adaptive cutoff.
        const float dCutoff = 1.8f;
        _pos.SetParams(minCutoff, beta, dCutoff);

        // Pressure: slightly more stable than position, still independent.
        float pMin = Lerp(12.0f, 3.0f, SmoothStep(s));
        float pBeta = Lerp(2.0f, 0.3f, s);
        _pressure.SetParams(pMin, pBeta, dCutoff);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

/// <summary>1€ low-pass helper (scalar).</summary>
internal sealed class OneEuro1D
{
    private float _minCutoff = 1f;
    private float _beta;
    private float _dCutoff = 1f;
    private float _x;
    private float _dx;
    private bool _has;

    public void Reset()
    {
        _has = false;
        _x = 0;
        _dx = 0;
    }

    public void Seed(float x)
    {
        _x = x;
        _dx = 0;
        _has = true;
    }

    public void SetParams(float minCutoff, float beta, float dCutoff)
    {
        _minCutoff = Math.Max(1e-4f, minCutoff);
        _beta = Math.Max(0f, beta);
        _dCutoff = Math.Max(1e-4f, dCutoff);
    }

    public float Filter(float value, float dt)
    {
        dt = Math.Max(dt, 1e-6f);
        if (!_has)
        {
            Seed(value);
            return value;
        }

        float edx = (value - _x) / dt;
        _dx = ExpSmooth(_dx, edx, Alpha(_dCutoff, dt));
        float cutoff = _minCutoff + _beta * MathF.Abs(_dx);
        _x = ExpSmooth(_x, value, Alpha(cutoff, dt));
        return _x;
    }

    private static float Alpha(float cutoff, float dt)
    {
        float tau = 1f / (2f * MathF.PI * Math.Max(cutoff, 1e-4f));
        return 1f / (1f + tau / dt);
    }

    private static float ExpSmooth(float prev, float value, float alpha)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        return prev + alpha * (value - prev);
    }
}

/// <summary>1€ filter on X/Y with a shared derivative magnitude for cutoff.</summary>
internal sealed class OneEuro2D
{
    private float _minCutoff = 1f;
    private float _beta;
    private float _dCutoff = 1f;
    private float _x, _y;
    private float _dx, _dy;
    private bool _has;

    public void Reset()
    {
        _has = false;
        _x = _y = 0;
        _dx = _dy = 0;
    }

    public void Seed(float x, float y)
    {
        _x = x;
        _y = y;
        _dx = _dy = 0;
        _has = true;
    }

    public void SetParams(float minCutoff, float beta, float dCutoff)
    {
        _minCutoff = Math.Max(1e-4f, minCutoff);
        _beta = Math.Max(0f, beta);
        _dCutoff = Math.Max(1e-4f, dCutoff);
    }

    public Float2 Filter(float x, float y, float dt)
    {
        dt = Math.Max(dt, 1e-6f);
        if (!_has)
        {
            Seed(x, y);
            return new Float2(x, y);
        }

        float rawDx = (x - _x) / dt;
        float rawDy = (y - _y) / dt;
        float aD = Alpha(_dCutoff, dt);
        _dx = ExpSmooth(_dx, rawDx, aD);
        _dy = ExpSmooth(_dy, rawDy, aD);
        float speed = MathF.Sqrt(_dx * _dx + _dy * _dy);
        float cutoff = _minCutoff + _beta * speed;
        float a = Alpha(cutoff, dt);
        _x = ExpSmooth(_x, x, a);
        _y = ExpSmooth(_y, y, a);
        return new Float2(_x, _y);
    }

    private static float Alpha(float cutoff, float dt)
    {
        float tau = 1f / (2f * MathF.PI * Math.Max(cutoff, 1e-4f));
        return 1f / (1f + tau / dt);
    }

    private static float ExpSmooth(float prev, float value, float alpha)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        return prev + alpha * (value - prev);
    }
}
