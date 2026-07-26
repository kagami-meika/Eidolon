namespace Eidolon.Core;

public enum RulerKind
{
    None,
    Straight,
    Ellipse,
    Symmetry,
    VanishingPoint,
    Perspective1,   // 一点透视：灭点 + 水平/垂直引导
    Perspective2,   // 两点：视平线 + 线上两灭点 + 垂直于视平线引导
    Perspective3,   // 三点：视平线 + 线上两灭点 + 第三灭点
    Fisheye6
}

public enum RulerHandle
{
    None,
    Origin,
    EllA, EllB, EllC, EllD,
    Vp,
    Vp0, Vp1, Vp2,      // Vp0/Vp1 在视平线上；Vp2 为第三点
    FishR0, FishR1, FishG0, FishG1, FishB0, FishB1,
    FishHorizonCenter, FishHorizonRim, FishTheta1, FishTheta2, FishTheta3,
    FishP               // Fisheye6 reference point P (and its inverse P')
}

/// <summary>Fisheye6 reference‑point mode.</summary>
public enum FisheyePMode
{
    Off,
    VisualOnly,
    Snappable
}

/// <summary>Locked trajectory for one stroke (or force-snap continuous).</summary>
public enum TrackKind
{
    None,
    Line,    // Anchor + Dir (unit)
    Circle,  // Center + Radius
    Ellipse  // use homography circle snap
}

/// <summary>
/// Circle or its collinear degeneration (infinite line).
/// Large-radius circles are represented as lines to avoid float blow-up.
/// </summary>
public readonly struct DocCircle
{
    public readonly bool IsLine;
    public readonly Float2 Center;
    public readonly double Radius; // double: may be huge before line degeneration
    public readonly Float2 LineA;
    public readonly Float2 LineB;

    public DocCircle(Float2 center, double radius)
    {
        IsLine = false;
        Center = center;
        Radius = radius;
        LineA = default;
        LineB = default;
    }

    public DocCircle(Float2 lineA, Float2 lineB, bool isLine)
    {
        IsLine = isLine;
        Center = default;
        Radius = 0;
        LineA = lineA;
        LineB = lineB;
    }

    public bool IsValid =>
        IsLine
            ? (LineA.X != LineB.X || LineA.Y != LineB.Y)
            : (Radius > 1e-9 && !double.IsNaN(Radius) && !double.IsInfinity(Radius));

    /// <summary>Project point onto circle (or line if degenerate).</summary>
    public Float2 Project(Float2 p)
    {
        if (!IsValid) return p;
        if (IsLine)
            return ProjectToLineStatic(p, LineA, LineB);

        // Stable: C + R * normalize(P - C) in double
        double dx = p.X - Center.X;
        double dy = p.Y - Center.Y;
        double d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1e-12)
            return new Float2((float)(Center.X + Radius), (float)Center.Y);
        double s = Radius / d;
        return new Float2((float)(Center.X + dx * s), (float)(Center.Y + dy * s));
    }

    /// <summary>
    /// Circumcircle of three points. Uses decimal for the linear solve (higher precision),
    /// then double for radius. Nearly collinear → infinite line (no huge-R float circle).
    /// </summary>
    public static DocCircle? From3Points(Float2 a, Float2 b, Float2 c)
    {
        // Translate so A is origin; use decimal for bisector solve to reduce cancellation
        decimal ax = (decimal)a.X, ay = (decimal)a.Y;
        decimal bx = (decimal)b.X - ax, by = (decimal)b.Y - ay;
        decimal cx = (decimal)c.X - ax, cy = (decimal)c.Y - ay;

        decimal bb = bx * bx + by * by;
        decimal cc = cx * cx + cy * cy;
        decimal cross = bx * cy - by * cx;
        decimal crossAbs = cross < 0 ? -cross : cross;

        double ab = Math.Sqrt((double)bb);
        double ac = Math.Sqrt((double)cc);
        double bcx = (double)(cx - bx), bcy = (double)(cy - by);
        double bc = Math.Sqrt(bcx * bcx + bcy * bcy);
        double maxEdge = Math.Max(ab, Math.Max(ac, bc));
        if (maxEdge < 1e-12)
            return null;

        double area2 = (double)crossAbs;
        double abc = ab * ac * bc;
        double rEst = area2 < 1e-30 ? double.PositiveInfinity : abc / area2;

        // Flat triangle → line through longest edge (no bow-arc from bad huge circle)
        if ((double)crossAbs < 1e-18 * maxEdge * maxEdge || rEst > 1e6 * maxEdge)
        {
            Float2 la, lb;
            if (bc >= ab && bc >= ac) { la = b; lb = c; }
            else if (ac >= ab) { la = a; lb = c; }
            else { la = a; lb = b; }
            return new DocCircle(la, lb, isLine: true);
        }

        // Circumcenter in local frame (decimal)
        decimal inv = 0.5m / cross;
        decimal ux = inv * (cy * bb - by * cc);
        decimal uy = inv * (bx * cc - cx * bb);
        double uxd = (double)ux, uyd = (double)uy;
        double r = Math.Sqrt(uxd * uxd + uyd * uyd);
        if (r < 1e-12 || double.IsNaN(r) || double.IsInfinity(r))
            return null;

        return new DocCircle(
            new Float2((float)((double)ax + uxd), (float)((double)ay + uyd)),
            r);
    }

    /// <summary>
    /// Sample visible circle/line against bounds.
    /// Returns <b>disjoint</b> polylines so drawing never connects across gaps (no bowstring/chord artifact).
    /// </summary>
    public List<List<Float2>> SampleVisibleSegments(float minX, float minY, float maxX, float maxY, float maxSegLen = 4f)
    {
        var segments = new List<List<Float2>>();
        if (!IsValid) return segments;

        if (IsLine)
        {
            if (ClipLineToRect(LineA, LineB, minX, minY, maxX, maxY, out var p0, out var p1))
                segments.Add(new List<Float2> { p0, p1 });
            return segments;
        }

        double cx = Center.X, cy = Center.Y, r = Radius;
        double pad = Math.Max(maxSegLen, 2);
        double x0 = minX - pad, y0 = minY - pad, x1 = maxX + pad, y1 = maxY + pad;

        double nearestX = Math.Clamp(cx, x0, x1);
        double nearestY = Math.Clamp(cy, y0, y1);
        double ndx = nearestX - cx, ndy = nearestY - cy;
        if (ndx * ndx + ndy * ndy > r * r)
            return segments;

        // Chord length constraint: Δθ ≈ maxSegLen / R
        double dTheta = Math.Clamp(maxSegLen / Math.Max(r, 1e-6), 0.002, Math.PI / 12);

        bool centerIn = cx >= x0 && cx <= x1 && cy >= y0 && cy <= y1;
        double span = Math.Max(maxX - minX, maxY - minY);

        // Full circle only when center is in view and R is not huge
        if (centerIn && r < span * 4)
        {
            int n = Math.Max(16, (int)Math.Ceiling(2 * Math.PI / dTheta));
            n = Math.Min(n, 720);
            var loop = new List<Float2>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double th = i * (2 * Math.PI / n);
                loop.Add(new Float2((float)(cx + r * Math.Cos(th)), (float)(cy + r * Math.Sin(th))));
            }
            segments.Add(loop);
            return segments;
        }

        // Large R or center outside: collect contiguous angular runs only.
        // Critical: never join points across a gap — that creates a straight "bowstring".
        int n2 = Math.Max(48, (int)Math.Ceiling(2 * Math.PI / dTheta));
        n2 = Math.Min(n2, 1440);
        List<Float2>? run = null;
        double gapLimit = dTheta * 2.5; // max angular step still considered contiguous

        double prevTh = double.NaN;
        for (int i = 0; i <= n2; i++)
        {
            double th = i * (2 * Math.PI / n2);
            if (i == n2) th = 2 * Math.PI; // exact close
            double px = cx + r * Math.Cos(th);
            double py = cy + r * Math.Sin(th);
            bool inside = px >= x0 && px <= x1 && py >= y0 && py <= y1;
            if (inside)
            {
                var pt = new Float2((float)px, (float)py);
                if (run is null)
                    run = new List<Float2> { pt };
                else if (!double.IsNaN(prevTh) && AngleDelta(prevTh, th) > gapLimit)
                {
                    // angular discontinuity → new segment
                    if (run.Count >= 2) segments.Add(run);
                    run = new List<Float2> { pt };
                }
                else
                    run.Add(pt);
                prevTh = th;
            }
            else if (run is not null)
            {
                if (run.Count >= 2) segments.Add(run);
                run = null;
                prevTh = double.NaN;
            }
        }
        if (run is { Count: >= 2 })
            segments.Add(run);

        // Merge first/last if they meet at θ=0 seam and both on circle (full wrap)
        if (segments.Count >= 2)
        {
            var first = segments[0];
            var last = segments[^1];
            // if sampling closed the loop across 0 and both ends are near each other on circle
            var a = last[^1];
            var b = first[0];
            double ddx = a.X - b.X, ddy = a.Y - b.Y;
            if (ddx * ddx + ddy * ddy < maxSegLen * maxSegLen * 4)
            {
                last.AddRange(first);
                segments[0] = last;
                segments.RemoveAt(segments.Count - 1);
            }
        }

        // Fallback: pure local tangent line (exactly 2 points) — never multi-point bow
        if (segments.Count == 0)
        {
            var mid = new Float2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            var on = Project(mid);
            double rdx = on.X - cx, rdy = on.Y - cy;
            double len = Math.Sqrt(rdx * rdx + rdy * rdy);
            if (len > 1e-12)
            {
                double tx = -rdy / len, ty = rdx / len;
                double half = Math.Max(maxX - minX, maxY - minY) * 1.5;
                var q0 = new Float2((float)(on.X - tx * half), (float)(on.Y - ty * half));
                var q1 = new Float2((float)(on.X + tx * half), (float)(on.Y + ty * half));
                if (ClipLineToRect(q0, q1, minX, minY, maxX, maxY, out var c0, out var c1))
                    segments.Add(new List<Float2> { c0, c1 });
            }
        }

        return segments;
    }

    /// <summary>Backward-compatible: flattens segments (prefer SampleVisibleSegments for drawing).</summary>
    public List<Float2> SampleVisible(float minX, float minY, float maxX, float maxY, float maxSegLen = 4f)
    {
        var segs = SampleVisibleSegments(minX, minY, maxX, maxY, maxSegLen);
        if (segs.Count == 0) return new List<Float2>();
        if (segs.Count == 1) return segs[0];
        // Do not merge — return longest segment only to avoid accidental bows if caller ignores splits
        List<Float2> best = segs[0];
        for (int i = 1; i < segs.Count; i++)
            if (segs[i].Count > best.Count) best = segs[i];
        return best;
    }

    private static double AngleDelta(double a, double b)
    {
        double d = Math.Abs(b - a);
        if (d > Math.PI) d = 2 * Math.PI - d;
        return d;
    }

    private static Float2 ProjectToLineStatic(Float2 p, Float2 a, Float2 b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double len2 = abx * abx + aby * aby;
        if (len2 < 1e-18) return a;
        double t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2;
        return new Float2((float)(a.X + t * abx), (float)(a.Y + t * aby));
    }

    /// <summary>Liang-Barsky style clip of infinite line through a,b to rect.</summary>
    private static bool ClipLineToRect(Float2 a, Float2 b, float minX, float minY, float maxX, float maxY,
        out Float2 p0, out Float2 p1)
    {
        p0 = p1 = default;
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-18) return false;
        dx /= len; dy /= len;
        // Parametric: P(t) = A + t * dir, t in (-inf, inf)
        // Intersect with four edges, collect t in range that lies inside
        double tMin = double.NegativeInfinity, tMax = double.PositiveInfinity;
        // x bounds
        if (Math.Abs(dx) < 1e-18)
        {
            if (a.X < minX || a.X > maxX) return false;
        }
        else
        {
            double t1 = (minX - a.X) / dx;
            double t2 = (maxX - a.X) / dx;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
        }
        if (Math.Abs(dy) < 1e-18)
        {
            if (a.Y < minY || a.Y > maxY) return false;
        }
        else
        {
            double t1 = (minY - a.Y) / dy;
            double t2 = (maxY - a.Y) / dy;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
        }
        if (tMin > tMax || double.IsInfinity(tMin) || double.IsInfinity(tMax)) return false;
        p0 = new Float2((float)(a.X + tMin * dx), (float)(a.Y + tMin * dy));
        p1 = new Float2((float)(a.X + tMax * dx), (float)(a.Y + tMax * dy));
        return true;
    }
}

public readonly struct Homography3
{
    public readonly float M00, M01, M02, M10, M11, M12, M20, M21, M22;

    public Homography3(float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22)
    {
        M00 = m00; M01 = m01; M02 = m02;
        M10 = m10; M11 = m11; M12 = m12;
        M20 = m20; M21 = m21; M22 = m22;
    }

    public Float2 Map(float x, float y)
    {
        float X = M00 * x + M01 * y + M02;
        float Y = M10 * x + M11 * y + M12;
        float W = M20 * x + M21 * y + M22;
        if (MathF.Abs(W) < 1e-12f) return new Float2(X, Y);
        return new Float2(X / W, Y / W);
    }

    public bool TryUnmap(Float2 p, out float u, out float v)
    {
        if (!TryInvert(out var inv)) { u = v = 0; return false; }
        float X = inv.M00 * p.X + inv.M01 * p.Y + inv.M02;
        float Y = inv.M10 * p.X + inv.M11 * p.Y + inv.M12;
        float W = inv.M20 * p.X + inv.M21 * p.Y + inv.M22;
        if (MathF.Abs(W) < 1e-12f) { u = v = 0; return false; }
        u = X / W; v = Y / W;
        return true;
    }

    public bool TryInvert(out Homography3 inv)
    {
        float a = M00, b = M01, c = M02, d = M10, e = M11, f = M12, g = M20, h = M21, i = M22;
        float A = e * i - f * h, B = f * g - d * i, C = d * h - e * g;
        float D = c * h - b * i, E = a * i - c * g, F = b * g - a * h;
        float G = b * f - c * e, H = c * d - a * f, I = a * e - b * d;
        float det = a * A + b * B + c * C;
        if (MathF.Abs(det) < 1e-12f) { inv = default; return false; }
        float s = 1f / det;
        inv = new Homography3(A * s, D * s, G * s, B * s, E * s, H * s, C * s, F * s, I * s);
        return true;
    }

    public static bool FromUnitSquareToQuad(Float2 a, Float2 b, Float2 c, Float2 d, out Homography3 H)
    {
        float[] srcX = { 0, 1, 1, 0 };
        float[] srcY = { 0, 0, 1, 1 };
        Float2[] dst = { a, b, c, d };
        double[,] M = new double[8, 8];
        double[] rhs = new double[8];
        for (int i = 0; i < 4; i++)
        {
            double x = srcX[i], y = srcY[i];
            double u = dst[i].X, v = dst[i].Y;
            int r0 = i * 2, r1 = i * 2 + 1;
            M[r0, 0] = x; M[r0, 1] = y; M[r0, 2] = 1;
            M[r0, 3] = 0; M[r0, 4] = 0; M[r0, 5] = 0;
            M[r0, 6] = -u * x; M[r0, 7] = -u * y;
            rhs[r0] = u;
            M[r1, 0] = 0; M[r1, 1] = 0; M[r1, 2] = 0;
            M[r1, 3] = x; M[r1, 4] = y; M[r1, 5] = 1;
            M[r1, 6] = -v * x; M[r1, 7] = -v * y;
            rhs[r1] = v;
        }
        if (!Solve8(M, rhs, out double[] h)) { H = default; return false; }
        H = new Homography3((float)h[0], (float)h[1], (float)h[2], (float)h[3], (float)h[4], (float)h[5], (float)h[6], (float)h[7], 1f);
        return true;
    }

    private static bool Solve8(double[,] A, double[] b, out double[] x)
    {
        int n = 8;
        x = new double[n];
        double[,] m = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = A[i, j];
            m[i, n] = b[i];
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            double best = Math.Abs(m[col, col]);
            for (int r = col + 1; r < n; r++)
            {
                double v = Math.Abs(m[r, col]);
                if (v > best) { best = v; piv = r; }
            }
            if (best < 1e-14) return false;
            if (piv != col)
                for (int j = col; j <= n; j++)
                    (m[col, j], m[piv, j]) = (m[piv, j], m[col, j]);
            double div = m[col, col];
            for (int j = col; j <= n; j++) m[col, j] /= div;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = m[r, col];
                if (Math.Abs(f) < 1e-18) continue;
                for (int j = col; j <= n; j++) m[r, j] -= f * m[col, j];
            }
        }
        for (int i = 0; i < n; i++) x[i] = m[i, n];
        return true;
    }
}


public struct StereographicGeo
{
    public float Xo, Yo, R;
    public float Theta1, Theta2, Theta3;
    public float P, Q, S;
    public float R1, R2, R3;
    public float Ct1x, Ct1y, Ct2x, Ct2y, Ct3x, Ct3y;
    public float D1p, D1n, D2p, D2n, D3p, D3n;
    public float Ts1, Ts2, Ts3;

    public Float2 VanishingPoint(int axis, bool positive)
    {
        float d, ct, st;
        switch (axis)
        {
            case 0: d = positive ? D1p : D1n; ct = Ct1x; st = Ct1y; break;
            case 1: d = positive ? D2p : D2n; ct = Ct2x; st = Ct2y; break;
            default: d = positive ? D3p : D3n; ct = Ct3x; st = Ct3y; break;
        }
        return new Float2(Xo + d * ct, Yo + d * st);
    }

    public Float2 VanishingLineCircleCenter(int axis)
    {
        float d, ct, st;
        switch (axis)
        {
            case 0: d = P;  ct = Ct1x; st = Ct1y; break;
            case 1: d = Q;  ct = Ct2x; st = Ct2y; break;
            default: d = S; ct = Ct3x; st = Ct3y; break;
        }
        return new Float2(Xo + d * ct, Yo + d * st);
    }

    public double VanishingLineCircleRadius(int axis) => axis switch
    {
        0 => R1,
        1 => R2,
        _ => R3
    };

    public DocCircle GetPerspectiveCircle(int axis, Float2 k)
    {
        float dx = k.X - Xo, dy = k.Y - Yo;
        float Dk2 = dx * dx + dy * dy;
        float ct, st, r_axis;
        float r2r_div;
        switch (axis)
        {
            case 0: ct = Ct1x; st = Ct1y; r_axis = R1; r2r_div = R * R / P; break;
            case 1: ct = Ct2x; st = Ct2y; r_axis = R2; r2r_div = R * R / Q; break;
            default: ct = Ct3x; st = Ct3y; r_axis = R3; r2r_div = R * R / S; break;
        }
        float u = dx * ct + dy * st;
        float v = -dx * st + dy * ct;
        float denom = 2f * v;
        if (MathF.Abs(denom) < 1e-9f)
            return default;
        float lambda = (Dk2 + 2f * r2r_div * u - R * R) / denom;
        float Cx = Xo - r2r_div * ct - lambda * st;
        float Cy = Yo - r2r_div * st + lambda * ct;
        double Rk = Math.Sqrt((double)(R * r_axis / (axis == 0 ? P : axis == 1 ? Q : S)) * (R * r_axis / (axis == 0 ? P : axis == 1 ? Q : S)) + (double)lambda * lambda);
        return new DocCircle(new Float2(Cx, Cy), Rk);
    }
}

public sealed class RulerState
{
    public RulerKind Kind { get; set; } = RulerKind.None;
    public bool Visible { get; set; } = true;
    public bool SnapEnabled { get; set; } = true;
    /// <summary>When true, always project onto track (ignore distance threshold).</summary>
    public bool ForceSnap { get; set; }
    public float SnapStrength { get; set; } = 12f;
    public RulerHandle ActiveHandle { get; set; } = RulerHandle.None;

    public Float2 Origin { get; set; } = new(400, 300);
    public float AngleDeg { get; set; }

    public Float2 EllA { get; set; } = new(300, 200);
    public Float2 EllB { get; set; } = new(500, 200);
    public Float2 EllC { get; set; } = new(500, 400);
    public Float2 EllD { get; set; } = new(300, 400);

    // Hidden axis anchors: only the origin and angle define the infinite guide.
    public Float2 SymmetryOrigin { get; set; } = new(400, 400);
    public float SymmetryAngleDeg { get; set; } = 90f;

    public Float2 Vp { get; set; } = new(480, 300);

    // Infinite horizon: hidden origin + angle. VPs are constrained to this line.
    public Float2 HorizonOrigin { get; set; } = new(480, 400);
    public float HorizonAngleDeg { get; set; }
    public Float2 Vp0 { get; set; } = new(150, 400);
    public Float2 Vp1 { get; set; } = new(800, 400);
    public Float2 Vp2 { get; set; } = new(480, 80);

    public Float2 FishR0 { get; set; } = new(100, 200);
    public Float2 FishR1 { get; set; } = new(800, 200);
    public Float2 FishG0 { get; set; } = new(100, 500);
    public Float2 FishG1 { get; set; } = new(800, 500);
    public Float2 FishB0 { get; set; } = new(200, 100);
    public Float2 FishB1 { get; set; } = new(200, 700);

    // Stereographic 6-point fisheye (simplified): horizon circle + 3 polar angles
    public Float2 FishHorizonCenter { get; set; } = new(400, 300);
    public float FishHorizonRadius { get; set; } = 200f;
    public float FishTheta1Deg { get; set; } = 60f;
    public float FishTheta2Deg { get; set; } = 180f;
    public float FishTheta3Deg { get; set; } = 300f;
    public float FishGlobalAngleDeg { get; set; }

    /// <summary>Fisheye6 reference point P (user‑placed).</summary>
    public Float2 FisheyeP { get; set; } = new(400, 200);
    /// <summary>Fisheye6 P toggle mode: Off / VisualOnly / Snappable.</summary>
    public FisheyePMode FisheyePMode { get; set; } = FisheyePMode.Off;

    /// <summary>Circle inversion of P through the horizon circle.
    /// Returns null when mode is Off or P coincides with the center.
    /// P' = O − (P−O)·r²/|P−O|²  (collinear, opposite side, OP·OP' = r²).</summary>
    public Float2? FisheyePInverse()
    {
        if (FisheyePMode == FisheyePMode.Off) return null;
        Float2 o = FishHorizonCenter;
        float dx = FisheyeP.X - o.X, dy = FisheyeP.Y - o.Y;
        float d2 = dx * dx + dy * dy;
        if (d2 < 1e-6f) return null;
        float r2 = FishHorizonRadius * FishHorizonRadius;
        float s = r2 / d2;
        return new Float2(o.X - dx * s, o.Y - dy * s);
    }

    /// <summary>Direction vector from horizon center through P (unit).</summary>
    public Float2 FisheyePDir()
    {
        Float2 o = FishHorizonCenter;
        float dx = FisheyeP.X - o.X, dy = FisheyeP.Y - o.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6f) return new Float2(1, 0);
        return new Float2(dx / len, dy / len);
    }

    public Float2? StrokeAnchor { get; set; }
    public Float2? HoverDoc { get; set; }
    public bool PreviewEnabled { get; set; }

    /// <summary>Per-line snap enable for perspective-type rulers (channel 0/1/2).
    /// When false, that line is drawn but does not participate in snapping.
    /// Perspective1: 0=VP ray, 1=H, 2=V.
    /// Perspective2: 0=Vp0, 1=Vp1, 2=⟂horizon.
    /// Perspective3: 0=Vp0, 1=Vp1, 2=Vp2.
    /// Fisheye6: 0/1/2 = axis 0/1/2 circles.</summary>
    public bool PerspectiveLine0Enabled { get; set; } = true;
    public bool PerspectiveLine1Enabled { get; set; } = true;
    public bool PerspectiveLine2Enabled { get; set; } = true;

    /// <summary>Returns true if the given channel (0/1/2) is enabled for snapping.</summary>
    public bool IsLineSnapEnabled(int channel) => channel switch
    {
        0 => PerspectiveLine0Enabled,
        1 => PerspectiveLine1Enabled,
        2 => PerspectiveLine2Enabled,
        _ => false
    };

    // Locked track for current stroke
    public TrackKind LockedTrack { get; private set; }
    public Float2 LockA { get; private set; }
    public Float2 LockB { get; private set; } // second point or dir endpoint
    public Float2 LockCenter { get; private set; }
    public float LockRadius { get; private set; }
    private readonly List<Float2> _velocitySamples = new();
    private Float2 _lastStrokePoint;
    private double _lastStrokeTime;

    public void ResetForDocument(int width, int height)
    {
        float cx = width * 0.5f, cy = height * 0.5f;
        float s = Math.Min(width, height) * 0.2f;
        Origin = new Float2(cx, cy);
        AngleDeg = 0;
        EllA = new Float2(cx - s, cy - s);
        EllB = new Float2(cx + s, cy - s);
        EllC = new Float2(cx + s, cy + s);
        EllD = new Float2(cx - s, cy + s);
        SymmetryOrigin = new Float2(cx, cy);
        SymmetryAngleDeg = 90f;
        Vp = new Float2(cx, cy);
        HorizonOrigin = new Float2(cx, cy);
        HorizonAngleDeg = 0f;
        Vp0 = ProjectToHorizon(new Float2(width * 0.2f, cy));
        Vp1 = ProjectToHorizon(new Float2(width * 0.8f, cy));
        Vp2 = new Float2(cx, height * 0.08f);
        FishR0 = new Float2(width * 0.15f, height * 0.25f);
        FishR1 = new Float2(width * 0.85f, height * 0.25f);
        FishG0 = new Float2(width * 0.15f, height * 0.75f);
        FishG1 = new Float2(width * 0.85f, height * 0.75f);
        FishB0 = new Float2(width * 0.25f, height * 0.12f);
        FishB1 = new Float2(width * 0.25f, height * 0.88f);
        FishHorizonCenter = new Float2(cx, cy);
        FishHorizonRadius = MathF.Sqrt((float)width * height) * 0.5f;
        FishTheta1Deg = 60f;
        FishTheta2Deg = 180f;
        FishTheta3Deg = 300f;
        FishGlobalAngleDeg = 0f;
        FisheyeP = new Float2(cx, cy - MathF.Sqrt((float)width * height) * 0.3f);
        FisheyePMode = FisheyePMode.Off;
        PerspectiveLine0Enabled = true;
        PerspectiveLine1Enabled = true;
        PerspectiveLine2Enabled = true;
        ActiveHandle = RulerHandle.None;
        StrokeAnchor = null;
        HoverDoc = null;
        PreviewEnabled = false;
        ClearLock();
        _velocitySamples.Clear();
        _lastStrokePoint = default;
        _lastStrokeTime = 0;
    }

    public void ClearLock()
    {
        LockedTrack = TrackKind.None;
    }

    public Float2 HorizonDir() => DirFromAngle(HorizonAngleDeg);

    public Float2 HorizonNormal()
    {
        var d = HorizonDir();
        return new Float2(-d.Y, d.X);
    }

    public Float2 ProjectToHorizon(Float2 p) => ProjectLine(p, HorizonOrigin, HorizonOrigin + HorizonDir());

    public bool TryGetEllipseHomography(out Homography3 H) =>
        Homography3.FromUnitSquareToQuad(EllA, EllB, EllC, EllD, out H);

    public IEnumerable<(RulerHandle handle, Float2 pos)> EnumerateHandles()
    {
        if (Kind == RulerKind.None) yield break;
        switch (Kind)
        {
            case RulerKind.Straight:
                yield return (RulerHandle.Origin, Origin);
                break;
            case RulerKind.Ellipse:
                yield return (RulerHandle.EllA, EllA);
                yield return (RulerHandle.EllB, EllB);
                yield return (RulerHandle.EllC, EllC);
                yield return (RulerHandle.EllD, EllD);
                break;
            case RulerKind.Symmetry:
                // The axis origin is intentionally hidden; Select edits it through the axis body.
                break;
            case RulerKind.VanishingPoint:
            case RulerKind.Perspective1:
                yield return (RulerHandle.Vp, Vp);
                break;
            case RulerKind.Perspective2:
                yield return (RulerHandle.Vp0, Vp0);
                yield return (RulerHandle.Vp1, Vp1);
                break;
            case RulerKind.Perspective3:
                yield return (RulerHandle.Vp0, Vp0);
                yield return (RulerHandle.Vp1, Vp1);
                yield return (RulerHandle.Vp2, Vp2);
                break;
            case RulerKind.Fisheye6:
                yield return (RulerHandle.FishHorizonCenter, FishHorizonCenter);
                yield return (RulerHandle.FishHorizonRim, FishHorizonPointOnRim(0));
                yield return (RulerHandle.FishTheta1, FishThetaHandlePos(0));
                yield return (RulerHandle.FishTheta2, FishThetaHandlePos(1));
                yield return (RulerHandle.FishTheta3, FishThetaHandlePos(2));
                if (FisheyePMode != FisheyePMode.Off)
                    yield return (RulerHandle.FishP, FisheyeP);
                break;
        }
    }

    public RulerHandle HitTest(Float2 docPt, float hitRadius)
    {
        RulerHandle best = RulerHandle.None;
        float bestD = hitRadius;
        foreach (var (h, p) in EnumerateHandles())
        {
            float d = Dist(docPt, p);
            if (d <= bestD) { bestD = d; best = h; }
        }
        if (Kind == RulerKind.Symmetry && DistanceToLine(docPt, SymmetryOrigin, SymmetryOrigin + DirFromAngle(SymmetryAngleDeg)) <= hitRadius)
            return RulerHandle.Origin;
        if (Kind is RulerKind.Perspective2 or RulerKind.Perspective3
            && DistanceToLine(docPt, HorizonOrigin, HorizonOrigin + HorizonDir()) <= hitRadius)
            return RulerHandle.Vp0;
        return best;
    }

    public void SetHandle(RulerHandle h, Float2 docPt, bool snapAngle45 = false, float? snapBaseWorldDeg = null)
    {
        switch (h)
        {
            case RulerHandle.Origin:
                if (Kind == RulerKind.Symmetry) SymmetryOrigin = docPt;
                else Origin = docPt;
                break;
            case RulerHandle.EllA: EllA = docPt; break;
            case RulerHandle.EllB: EllB = docPt; break;
            case RulerHandle.EllC: EllC = docPt; break;
            case RulerHandle.EllD: EllD = docPt; break;
            case RulerHandle.Vp: Vp = docPt; break;
            case RulerHandle.Vp0:
                Vp0 = ProjectToHorizon(docPt);
                break;
            case RulerHandle.Vp1:
                Vp1 = ProjectToHorizon(docPt);
                break;
            case RulerHandle.Vp2:
                Vp2 = docPt;
                break;
            case RulerHandle.FishR0: FishR0 = docPt; break;
            case RulerHandle.FishR1: FishR1 = docPt; break;
            case RulerHandle.FishG0: FishG0 = docPt; break;
            case RulerHandle.FishG1: FishG1 = docPt; break;
            case RulerHandle.FishB0: FishB0 = docPt; break;
            case RulerHandle.FishB1: FishB1 = docPt; break;
            case RulerHandle.FishHorizonCenter:
                FishHorizonCenter = docPt;
                break;
            case RulerHandle.FishHorizonRim:
            {
                float dx = docPt.X - FishHorizonCenter.X, dy = docPt.Y - FishHorizonCenter.Y;
                FishHorizonRadius = Math.Max(8f, MathF.Sqrt(dx * dx + dy * dy));
                break;
            }
            case RulerHandle.FishTheta1:
                FishTheta1Deg = FishPolarLocalDeg(docPt, snapAngle45, snapBaseWorldDeg);
                break;
            case RulerHandle.FishTheta2:
                FishTheta2Deg = FishPolarLocalDeg(docPt, snapAngle45, snapBaseWorldDeg);
                break;
            case RulerHandle.FishTheta3:
                FishTheta3Deg = FishPolarLocalDeg(docPt, snapAngle45, snapBaseWorldDeg);
                break;
            case RulerHandle.FishP:
                FisheyeP = docPt;
                break;
        }
    }

    /// <summary>True for fisheye polar handles that move on the horizon unit circle.</summary>
    public static bool IsFisheyeCircleHandle(RulerHandle h) =>
        h is RulerHandle.FishTheta1 or RulerHandle.FishTheta2 or RulerHandle.FishTheta3;

    private float FishPolarLocalDeg(Float2 docPt, bool snapAngle45, float? snapBaseWorldDeg)
    {
        float polar = PolarAngleDeg(docPt - FishHorizonCenter);
        // Relative lattice: base world angle at drag start + n×45°, not absolute compass snap.
        if (snapAngle45 && snapBaseWorldDeg is float baseW)
            polar = SnapRelativeDeg45(baseW, polar);
        return NormalizeDeg(polar - FishGlobalAngleDeg);
    }

    /// <summary>World-space polar angle of a fisheye theta handle (global + local).</summary>
    public float GetFishThetaWorldDeg(RulerHandle h) => h switch
    {
        RulerHandle.FishTheta1 => NormalizeDeg(FishGlobalAngleDeg + FishTheta1Deg),
        RulerHandle.FishTheta2 => NormalizeDeg(FishGlobalAngleDeg + FishTheta2Deg),
        RulerHandle.FishTheta3 => NormalizeDeg(FishGlobalAngleDeg + FishTheta3Deg),
        _ => 0f
    };

    /// <summary>
    /// Quantize pointer world angle to baseWorld + n×45° (shortest signed delta).
    /// Preserves the original angle lattice from drag start instead of snapping to absolute 0/45/90.
    /// </summary>
    public static float SnapRelativeDeg45(float baseWorldDeg, float pointerWorldDeg)
    {
        float delta = SignedDeltaDeg(pointerWorldDeg - baseWorldDeg);
        float stepped = MathF.Round(delta / 45f) * 45f;
        return NormalizeDeg(baseWorldDeg + stepped);
    }

    /// <summary>Signed shortest angular delta in (-180, 180].</summary>
    public static float SignedDeltaDeg(float deg)
    {
        deg %= 360f;
        if (deg > 180f) deg -= 360f;
        if (deg <= -180f) deg += 360f;
        return deg;
    }

    public void Translate(Float2 delta)
    {
        switch (Kind)
        {
            case RulerKind.Straight: Origin += delta; break;
            case RulerKind.Ellipse:
                EllA += delta; EllB += delta; EllC += delta; EllD += delta; break;
            case RulerKind.Symmetry: SymmetryOrigin += delta; break;
            case RulerKind.VanishingPoint:
            case RulerKind.Perspective1:
                Vp += delta; break;
            case RulerKind.Perspective2:
                HorizonOrigin += delta; Vp0 += delta; Vp1 += delta; break;
            case RulerKind.Perspective3:
                HorizonOrigin += delta; Vp0 += delta; Vp1 += delta; Vp2 += delta; break;
            case RulerKind.Fisheye6:
                FishHorizonCenter += delta;
                FisheyeP += delta;
                FishR0 += delta; FishR1 += delta;
                FishG0 += delta; FishG1 += delta;
                FishB0 += delta; FishB1 += delta; break;
        }
    }

    public void Rotate(Float2 pivot, float deltaDeg)
    {
        float rad = deltaDeg * MathF.PI / 180f;
        float c = MathF.Cos(rad), s = MathF.Sin(rad);
        Float2 Rot(Float2 p)
        {
            float dx = p.X - pivot.X, dy = p.Y - pivot.Y;
            return new Float2(pivot.X + c * dx - s * dy, pivot.Y + s * dx + c * dy);
        }
        switch (Kind)
        {
            case RulerKind.Straight:
                AngleDeg += deltaDeg;
                Origin = Rot(Origin);
                break;
            case RulerKind.Ellipse:
                EllA = Rot(EllA); EllB = Rot(EllB); EllC = Rot(EllC); EllD = Rot(EllD);
                break;
            case RulerKind.Symmetry:
                SymmetryOrigin = Rot(SymmetryOrigin);
                SymmetryAngleDeg += deltaDeg;
                break;
            case RulerKind.VanishingPoint:
            case RulerKind.Perspective1:
                Vp = Rot(Vp); break;
            case RulerKind.Perspective2:
                HorizonOrigin = Rot(HorizonOrigin);
                HorizonAngleDeg += deltaDeg;
                Vp0 = ProjectToHorizon(Rot(Vp0));
                Vp1 = ProjectToHorizon(Rot(Vp1));
                break;
            case RulerKind.Perspective3:
                HorizonOrigin = Rot(HorizonOrigin);
                HorizonAngleDeg += deltaDeg;
                Vp0 = ProjectToHorizon(Rot(Vp0));
                Vp1 = ProjectToHorizon(Rot(Vp1));
                Vp2 = Rot(Vp2);
                break;
            case RulerKind.Fisheye6:
                FishHorizonCenter = Rot(FishHorizonCenter);
                FishGlobalAngleDeg += deltaDeg;
                FisheyeP = Rot(FisheyeP);
                FishR0 = Rot(FishR0); FishR1 = Rot(FishR1);
                FishG0 = Rot(FishG0); FishG1 = Rot(FishG1);
                FishB0 = Rot(FishB0); FishB1 = Rot(FishB1); break;
        }
    }

    public void ScaleUniform(Float2 pivot, float factor)
    {
        factor = Math.Clamp(factor, 0.05f, 20f);
        Float2 Sc(Float2 p) => new(pivot.X + (p.X - pivot.X) * factor, pivot.Y + (p.Y - pivot.Y) * factor);
        switch (Kind)
        {
            case RulerKind.Straight: Origin = Sc(Origin); break;
            case RulerKind.Ellipse:
                EllA = Sc(EllA); EllB = Sc(EllB); EllC = Sc(EllC); EllD = Sc(EllD); break;
            case RulerKind.Symmetry: SymmetryOrigin = Sc(SymmetryOrigin); break;
            case RulerKind.VanishingPoint:
            case RulerKind.Perspective1:
                Vp = Sc(Vp); break;
            case RulerKind.Perspective2:
                HorizonOrigin = Sc(HorizonOrigin);
                Vp0 = ProjectToHorizon(Sc(Vp0));
                Vp1 = ProjectToHorizon(Sc(Vp1));
                break;
            case RulerKind.Perspective3:
                HorizonOrigin = Sc(HorizonOrigin);
                Vp0 = ProjectToHorizon(Sc(Vp0));
                Vp1 = ProjectToHorizon(Sc(Vp1));
                Vp2 = Sc(Vp2);
                break;
            case RulerKind.Fisheye6:
                FishHorizonCenter = Sc(FishHorizonCenter);
                FishHorizonRadius = Math.Max(8f, FishHorizonRadius * factor);
                FisheyeP = Sc(FisheyeP);
                FishR0 = Sc(FishR0); FishR1 = Sc(FishR1);
                FishG0 = Sc(FishG0); FishG1 = Sc(FishG1);
                FishB0 = Sc(FishB0); FishB1 = Sc(FishB1); break;
        }
    }

    public Float2 Centroid()
    {
        return Kind switch
        {
            RulerKind.Straight => Origin,
            RulerKind.Ellipse => new Float2((EllA.X + EllB.X + EllC.X + EllD.X) * 0.25f, (EllA.Y + EllB.Y + EllC.Y + EllD.Y) * 0.25f),
            RulerKind.Symmetry => SymmetryOrigin,
            RulerKind.VanishingPoint or RulerKind.Perspective1 => Vp,
            RulerKind.Perspective2 => HorizonOrigin,
            RulerKind.Perspective3 => new Float2((Vp0.X + Vp1.X + Vp2.X) / 3f, (Vp0.Y + Vp1.Y + Vp2.Y) / 3f),
            RulerKind.Fisheye6 => FishHorizonCenter,
            _ => Origin
        };
    }

    // ========== Stroke lock + snap ==========

    public void BeginStrokeConstraint(Float2 penDown)
    {
        StrokeAnchor = penDown;
        ClearLock();
        _velocitySamples.Clear();
        _lastStrokePoint = penDown;
        _lastStrokeTime = 0;
    }

    /// <summary>Feed raw input before snapping. Track choice is based on velocity direction, not displacement.</summary>
    public void ObserveStrokePoint(Float2 point, double timeSec)
    {
        if (StrokeAnchor is null || LockedTrack != TrackKind.None) return;
        if (_lastStrokeTime > 0)
        {
            double dt = Math.Max(0.0001, timeSec - _lastStrokeTime);
            var v = (point - _lastStrokePoint) * (float)(1.0 / dt);
            float speed = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
            if (speed > 1f)
            {
                var dir = v * (1f / speed);
                _velocitySamples.Add(dir);
                if (_velocitySamples.Count > 6) _velocitySamples.RemoveAt(0);
            }
        }
        _lastStrokePoint = point;
        _lastStrokeTime = timeSec;
        if (_velocitySamples.Count < 3) return;

        Float2 sum = default;
        foreach (var d in _velocitySamples) sum += d;
        float len = MathF.Sqrt(sum.X * sum.X + sum.Y * sum.Y);
        if (len < 0.2f) return;
        var direction = sum * (1f / len);
        LockTrackAt(StrokeAnchor.Value + direction * 100f);
    }

    public void EndStrokeConstraint()
    {
        StrokeAnchor = null;
        ClearLock();
        _velocitySamples.Clear();
    }

    public Float2 Snap(Float2 p) => Constrain(p);

    public Float2 Constrain(Float2 p)
    {
        if (!SnapEnabled || Kind == RulerKind.None) return p;

        // During a stroke: always remain on the track chosen at pen-down
        if (LockedTrack != TrackKind.None)
            return ProjectLocked(p);

        // During velocity probe warmup: project onto provisional direction from anchor
        // so the stroke stays on the preview trajectory while collecting samples.
        if (StrokeAnchor is not null)
            return ProvisionalSnap(p);

        // Outside stroke
        if (ForceSnap)
        {
            // Continuous force-snap: project onto current best guide (no sticky lock)
            LockTrackAt(p);
            var r = ProjectLocked(p);
            ClearLock();
            return r;
        }

        return SoftSnap(p);
    }

    public bool ShouldMirrorStroke => Kind == RulerKind.Symmetry && SnapEnabled;

    public Float2 MirrorAcrossSymmetry(Float2 p)
    {
        var a = SymmetryOrigin;
        var b = SymmetryOrigin + DirFromAngle(SymmetryAngleDeg);
        var ab = b - a;
        float len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 1e-6f) return p;
        float t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2;
        var proj = new Float2(a.X + t * ab.X, a.Y + t * ab.Y);
        return new Float2(2 * proj.X - p.X, 2 * proj.Y - p.Y);
    }

    private void LockTrackAt(Float2 p)
    {
        ClearLock();
        switch (Kind)
        {
            case RulerKind.Straight:
                LockLine(Origin, Origin + DirFromAngle(AngleDeg));
                break;
            case RulerKind.Ellipse:
                LockedTrack = TrackKind.Ellipse;
                break;
            case RulerKind.Symmetry:
                break;
            case RulerKind.VanishingPoint:
                LockLine(p, Vp);
                break;
            case RulerKind.Perspective1:
            {
                Float2 anchor = StrokeAnchor ?? p;
                // Collect distances only for enabled lines
                bool e0 = IsLineSnapEnabled(0), e1 = IsLineSnapEnabled(1), e2 = IsLineSnapEnabled(2);
                int enabled = (e0 ? 1 : 0) + (e1 ? 1 : 0) + (e2 ? 1 : 0);
                if (enabled == 0) break;
                var pVp = ProjectLine(p, anchor, Vp);
                float dVp = e0 ? Dist(p, pVp) : float.MaxValue;
                var pH = ProjectLine(p, anchor, anchor + new Float2(1, 0));
                float dH = e1 ? Dist(p, pH) : float.MaxValue;
                var pV = ProjectLine(p, anchor, anchor + new Float2(0, 1));
                float dV = e2 ? Dist(p, pV) : float.MaxValue;
                if (dVp <= dH && dVp <= dV) LockLine(anchor, Vp);
                else if (dH <= dV) LockLine(anchor, anchor + new Float2(1, 0));
                else LockLine(anchor, anchor + new Float2(0, 1));
                break;
            }
            case RulerKind.Perspective2:
            {
                // Track choices: ray→Vp0, ray→Vp1, or line ⟂ horizon through anchor
                Float2 anchor = StrokeAnchor ?? p;
                var n = HorizonNormal();
                bool e0 = IsLineSnapEnabled(0), e1 = IsLineSnapEnabled(1), e2 = IsLineSnapEnabled(2);
                int enabled = (e0 ? 1 : 0) + (e1 ? 1 : 0) + (e2 ? 1 : 0);
                if (enabled == 0) break;
                float d0 = e0 ? Dist(p, ProjectLine(p, anchor, Vp0)) : float.MaxValue;
                float d1 = e1 ? Dist(p, ProjectLine(p, anchor, Vp1)) : float.MaxValue;
                float dN = e2 ? Dist(p, ProjectLine(p, anchor, anchor + n)) : float.MaxValue;
                if (d0 <= d1 && d0 <= dN) LockLine(anchor, Vp0);
                else if (d1 <= dN) LockLine(anchor, Vp1);
                else LockLine(anchor, anchor + n * 100f);
                break;
            }
            case RulerKind.Perspective3:
            {
                Float2 anchor = StrokeAnchor ?? p;
                bool e0 = IsLineSnapEnabled(0), e1 = IsLineSnapEnabled(1), e2 = IsLineSnapEnabled(2);
                int enabled = (e0 ? 1 : 0) + (e1 ? 1 : 0) + (e2 ? 1 : 0);
                if (enabled == 0) break;
                float best = float.MaxValue;
                Float2 bestVp = Vp0;
                if (e0) { float d = Dist(p, ProjectLine(p, anchor, Vp0)); if (d < best) { best = d; bestVp = Vp0; } }
                if (e1) { float d = Dist(p, ProjectLine(p, anchor, Vp1)); if (d < best) { best = d; bestVp = Vp1; } }
                if (e2) { float d = Dist(p, ProjectLine(p, anchor, Vp2)); if (d < best) { best = d; bestVp = Vp2; } }
                LockLine(anchor, bestVp);
                break;
            }
            case RulerKind.Fisheye6:
            {
                Float2 third = StrokeAnchor ?? p;
                Float2 anchor = StrokeAnchor ?? p;
                // Track best distance across circles and the optional P‑P' line
                float bestD = float.MaxValue;
                DocCircle? bestC = null;
                Float2 bestLineA = default, bestLineB = default;
                bool bestIsLine = false;

                // Optional P‑P'‑anchor circle (when Snappable)
                if (FisheyePMode == FisheyePMode.Snappable && FisheyePInverse() is Float2 pi)
                {
                    if (DocCircle.From3Points(FisheyeP, pi, anchor) is DocCircle cPP)
                    {
                        float d = Dist(p, cPP.Project(p));
                        if (d < bestD) { bestD = d; bestC = cPP; bestIsLine = false; }
                    }
                    // If degenerate (collinear): fall back to line through anchor in direction O→P
                    else
                    {
                        var pDir = FisheyePDir();
                        var lineProj = ProjectLine(p, anchor, anchor + pDir);
                        float d = Dist(p, lineProj);
                        if (d < bestD) { bestD = d; bestLineA = anchor; bestLineB = anchor + pDir; bestIsLine = true; }
                    }
                }

                // Try stereographic 3-circle approach first
                if (TryComputeFisheyeGeo(out var geo))
                {
                    for (int axis = 0; axis < 3; axis++)
                    {
                        if (!IsLineSnapEnabled(axis)) continue;
                        var c = geo.GetPerspectiveCircle(axis, third);
                        if (c.IsValid)
                        {
                            float d = Dist(p, c.Project(p));
                            if (d < bestD) { bestD = d; bestC = c; bestIsLine = false; }
                        }
                    }
                    if (bestC is DocCircle cc)
                        LockCircle(cc);
                    else if (bestIsLine)
                        LockLine(bestLineA, bestLineB);
                    else if (IsLineSnapEnabled(0))
                        LockLine(third, geo.VanishingPoint(0, true));
                    // If all lines disabled, no lock
                }
                else
                {
                    // Fallback: old 3-point circles through handle pairs
                    Float2 la = FishR0, lb = FishR1;
                    (Float2, Float2, int)[] pairs = { (FishR0, FishR1, 0), (FishG0, FishG1, 1), (FishB0, FishB1, 2) };
                    foreach (var (c0, c1, axis) in pairs)
                    {
                        if (!IsLineSnapEnabled(axis)) continue;
                        if (DocCircle.From3Points(c0, c1, third) is DocCircle c)
                        {
                            float d = Dist(p, c.Project(p));
                            if (d < bestD) { bestD = d; bestC = c; bestIsLine = false; }
                        }
                        else
                        {
                            float d = Dist(p, ProjectLine(p, c0, c1));
                            if (d < bestD)
                            {
                                bestD = d;
                                bestC = null;
                                la = c0; lb = c1;
                                bestLineA = c0; bestLineB = c1;
                                bestIsLine = true;
                            }
                        }
                    }
                    if (bestC is DocCircle cc)
                        LockCircle(cc);
                    else if (bestIsLine && bestD < float.MaxValue)
                        LockLine(bestLineA, bestLineB);
                }
                break;
            }
        }
    }


    /// <summary>
    /// During stroke warmup (velocity samples < 3), project onto the nearest preview guide
    /// using the direction from the anchor to the current point.
    /// </summary>
    private Float2 ProvisionalSnap(Float2 p)
    {
        var anchor = StrokeAnchor.GetValueOrDefault();
        // Use a temporary lock based on direction from anchor through current point
        // (or through the anchor-to-p direction for the first sample).
        Float2 probe = p;
        // If there are velocity samples, use averaged direction
        if (_velocitySamples.Count > 0)
        {
            Float2 sum = default;
            foreach (var d in _velocitySamples) sum += d;
            float len = MathF.Sqrt(sum.X * sum.X + sum.Y * sum.Y);
            if (len > 0.1f)
            {
                var dir = sum * (1f / len);
                probe = anchor + dir * 100f;
            }
        }
        LockTrackAt(probe);
        var snapped = ProjectLocked(p);
        if (_velocitySamples.Count < 3)
            ClearLock(); // temporary — release until we have 3 samples
        return snapped;
    }

    private void LockLine(Float2 a, Float2 b)
    {
        LockedTrack = TrackKind.Line;
        LockA = a;
        LockB = b;
    }

    private void LockCircle(DocCircle c)
    {
        if (c.IsLine)
        {
            LockLine(c.LineA, c.LineB);
            return;
        }
        // Huge R: lock the local tangent line at the stroke anchor (circle ≈ line in the document)
        if (c.Radius > 1e6)
        {
            var mid = StrokeAnchor ?? new Float2(c.Center.X, c.Center.Y);
            var foot = c.Project(mid);
            double rdx = foot.X - (double)c.Center.X;
            double rdy = foot.Y - (double)c.Center.Y;
            double len = Math.Sqrt(rdx * rdx + rdy * rdy);
            Float2 tangent = len > 1e-12
                ? new Float2((float)(-rdy / len), (float)(rdx / len))
                : new Float2(1, 0);
            LockLine(foot, foot + tangent);
            return;
        }
        LockedTrack = TrackKind.Circle;
        LockCenter = c.Center;
        LockRadius = (float)c.Radius;
    }

    private Float2 ProjectLocked(Float2 p)
    {
        return LockedTrack switch
        {
            TrackKind.Line => ProjectLine(p, LockA, LockB),
            TrackKind.Circle => ProjectCircleStable(p, LockCenter, LockRadius),
            TrackKind.Ellipse => ProjectEllipse(p),
            _ => p
        };
    }

    /// <summary>Project onto circle using double radial normalization (stable for large R).</summary>
    private static Float2 ProjectCircleStable(Float2 p, Float2 center, float radius)
    {
        if (radius < 1e-3f || float.IsNaN(radius) || float.IsInfinity(radius))
            return p;
        double dx = p.X - (double)center.X;
        double dy = p.Y - (double)center.Y;
        double d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1e-12)
            return new Float2(center.X + radius, center.Y);
        double s = radius / d;
        return new Float2((float)(center.X + dx * s), (float)(center.Y + dy * s));
    }

    private Float2 SoftSnap(Float2 p)
    {
        // only snap if close to a guide; used outside stroke when !ForceSnap
        var candidates = CollectSoftCandidates(p);
        Float2 best = p;
        float bestD = SnapStrength;
        foreach (var c in candidates)
        {
            float d = Dist(p, c);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    private List<Float2> CollectSoftCandidates(Float2 p)
    {
        var list = new List<Float2>();
        switch (Kind)
        {
            case RulerKind.Straight:
                list.Add(ProjectLine(p, Origin, Origin + DirFromAngle(AngleDeg)));
                break;
            case RulerKind.Ellipse:
                list.Add(ProjectEllipse(p));
                break;
            case RulerKind.VanishingPoint:
            case RulerKind.Perspective1:
                // no free soft ray without anchor
                break;
            case RulerKind.Perspective2:
                list.Add(ProjectToHorizon(p));
                break;
            case RulerKind.Perspective3:
                list.Add(ProjectToHorizon(p));
                break;
        }
        return list;
    }

    private Float2 ProjectEllipse(Float2 p)
    {
        if (!TryGetEllipseHomography(out var H)) return p;
        if (!H.TryUnmap(p, out float u, out float v)) return p;
        float du = u - 0.5f, dv = v - 0.5f;
        float r = MathF.Sqrt(du * du + dv * dv);
        if (r < 1e-8f) return p;
        float s = 0.5f / r;
        return H.Map(0.5f + du * s, 0.5f + dv * s);
    }

    // ========== Preview ==========

    public bool WantsHoverPreview =>
        Visible && PreviewEnabled && (Kind is RulerKind.VanishingPoint or RulerKind.Perspective1
            or RulerKind.Perspective2 or RulerKind.Perspective3 or RulerKind.Fisheye6);

    public IEnumerable<(Float2 a, Float2 b, byte channel)> PreviewRays()
    {
        if (!WantsHoverPreview) yield break;
        Float2? tip = StrokeAnchor ?? HoverDoc;
        if (tip is not Float2 t) yield break;

        switch (Kind)
        {
            case RulerKind.VanishingPoint:
                // LineInf extends from first point along a→b; first point MUST be the tip
                yield return (t, Vp, 0);
                break;
            case RulerKind.Perspective1:
                // ray to VP + H + V through tip (canvas axes)
                // LineInf draws infinite line through point A along A→B, so A = tip
                yield return (t, Vp, 0);
                yield return (t, t + new Float2(1, 0), 1);
                yield return (t, t + new Float2(0, 1), 2);
                break;
            case RulerKind.Perspective2:
            {
                yield return (t, Vp0, 0);
                yield return (t, Vp1, 1);
                var n = HorizonNormal();
                yield return (t, t + n, 2);
                break;
            }
            case RulerKind.Perspective3:
                yield return (t, Vp0, 0);
                yield return (t, Vp1, 1);
                yield return (t, Vp2, 2);
                break;
        }
    }

    public IEnumerable<(DocCircle circle, byte channel)> PreviewFisheyeCircles()
    {
        if (!WantsHoverPreview || Kind != RulerKind.Fisheye6) yield break;
        Float2? third = StrokeAnchor ?? HoverDoc;
        if (third is not Float2 t) yield break;

        // Use stereographic geometry when valid angles are set
        if (TryComputeFisheyeGeo(out var geo))
        {
            yield return (geo.GetPerspectiveCircle(0, t), 0);
            yield return (geo.GetPerspectiveCircle(1, t), 1);
            yield return (geo.GetPerspectiveCircle(2, t), 2);
        }
        else
        {
            // Fallback: 3-point circles through old handle pairs
            if (DocCircle.From3Points(FishR0, FishR1, t) is DocCircle cr) yield return (cr, 0);
            if (DocCircle.From3Points(FishG0, FishG1, t) is DocCircle cg) yield return (cg, 1);
            if (DocCircle.From3Points(FishB0, FishB1, t) is DocCircle cb) yield return (cb, 2);
        }
    }

    public IEnumerable<Float2> SampleEllipse(int segments = 72)
    {
        if (!TryGetEllipseHomography(out var H)) yield break;
        for (int i = 0; i <= segments; i++)
        {
            float ang = i * (MathF.PI * 2f / segments);
            yield return H.Map(0.5f + 0.5f * MathF.Cos(ang), 0.5f + 0.5f * MathF.Sin(ang));
        }
    }

    private static Float2 DirFromAngle(float deg)
    {
        float rad = deg * MathF.PI / 180f;
        return new Float2(MathF.Cos(rad), MathF.Sin(rad));
    }

    private static Float2 ProjectLine(Float2 p, Float2 a, Float2 b)
    {
        var ab = b - a;
        float len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 1e-6f) return a;
        float t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2;
        return new Float2(a.X + t * ab.X, a.Y + t * ab.Y);
    }

    private static float Dist(Float2 a, Float2 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }


    // ========== Stereographic 6-point fisheye (simplified) ==========

    public Float2 FishHorizonPointOnRim(float angleDegOffset)
    {
        float rad = (FishGlobalAngleDeg + angleDegOffset) * MathF.PI / 180f;
        return new Float2(
            FishHorizonCenter.X + FishHorizonRadius * MathF.Cos(rad),
            FishHorizonCenter.Y + FishHorizonRadius * MathF.Sin(rad));
    }

    public Float2 FishThetaHandlePos(int slot)
    {
        float theta = slot switch
        {
            0 => FishTheta1Deg,
            1 => FishTheta2Deg,
            2 => FishTheta3Deg,
            _ => 0f
        };
        float rad = (FishGlobalAngleDeg + theta) * MathF.PI / 180f;
        float dist = FishHorizonRadius;
        return new Float2(
            FishHorizonCenter.X + dist * MathF.Cos(rad),
            FishHorizonCenter.Y + dist * MathF.Sin(rad));
    }

    private static float PolarAngleDeg(Float2 v)
    {
        float rad = MathF.Atan2(v.Y, v.X);
        float deg = rad * 180f / MathF.PI;
        return ((deg % 360f) + 360f) % 360f;
    }

    private static float NormalizeDeg(float deg)
    {
        deg %= 360f;
        if (deg < 0) deg += 360f;
        return deg;
    }

    private static float CosDeg(float deg) => MathF.Cos(deg * MathF.PI / 180f);
    private static float SinDeg(float deg) => MathF.Sin(deg * MathF.PI / 180f);

    public bool TryComputeFisheyeGeo(out StereographicGeo geo)
    {
        geo = default;
        float r2 = FishHorizonRadius;
        if (r2 < 1f) return false;

        float t1 = NormalizeDeg(FishGlobalAngleDeg + FishTheta1Deg);
        float t2 = NormalizeDeg(FishGlobalAngleDeg + FishTheta2Deg);
        float t3 = NormalizeDeg(FishGlobalAngleDeg + FishTheta3Deg);

        // Interior angle check: each angle between consecutive thetas must be in (90°, 180°)
        float d12 = NormalizeDeg(t2 - t1);
        float d23 = NormalizeDeg(t3 - t2);
        float d31 = NormalizeDeg(t1 - t3 + 360f);
        float c12 = CosDeg(d12), c23 = CosDeg(d23), c31 = CosDeg(d31);
        if (c12 >= 0f || c23 >= 0f || c31 >= 0f) return false;
        if (d12 < 90f || d12 > 180f || d23 < 90f || d23 > 180f || d31 < 90f || d31 > 180f) return false;

        float prod12 = -c12, prod23 = -c23, prod31 = -c31;
        float p = r2 * MathF.Sqrt(prod23 / (c12 * c31));
        float q = r2 * MathF.Sqrt(prod31 / (c23 * c12));
        float s = r2 * MathF.Sqrt(prod12 / (c31 * c23));
        float r1 = MathF.Sqrt(p * p + r2 * r2);
        float r2_ = MathF.Sqrt(q * q + r2 * r2);
        float r3 = MathF.Sqrt(s * s + r2 * r2);

        float cx = FishHorizonCenter.X, cy = FishHorizonCenter.Y;
        float c1x = CosDeg(t1), s1y = SinDeg(t1);
        float c2x = CosDeg(t2), s2y = SinDeg(t2);
        float c3x = CosDeg(t3), s3y = SinDeg(t3);

        geo = new StereographicGeo
        {
            Xo = cx, Yo = cy, R = r2,
            Theta1 = t1, Theta2 = t2, Theta3 = t3,
            P = p, Q = q, S = s,
            R1 = r1, R2 = r2_, R3 = r3,
            Ct1x = c1x, Ct1y = s1y, Ct2x = c2x, Ct2y = s2y, Ct3x = c3x, Ct3y = s3y,
            D1p = r2 * p / (r1 + r2), D1n = -r2 * (r1 + r2) / p,
            D2p = r2 * q / (r2_ + r2), D2n = -r2 * (r2_ + r2) / q,
            D3p = r2 * s / (r3 + r2), D3n = -r2 * (r3 + r2) / s,
            Ts1 = t1, Ts2 = t2, Ts3 = t3,
        };
        return true;
    }
    private static float DistanceToLine(Float2 p, Float2 a, Float2 b)
    {
        return Dist(p, ProjectLine(p, a, b));
    }

    /// <summary>Deep‑clone persistent ruler state (excludes transient stroke/lock fields).</summary>
    public RulerState Clone()
    {
        var c = new RulerState
        {
            Kind = Kind,
            Visible = Visible,
            SnapEnabled = SnapEnabled,
            ForceSnap = ForceSnap,
            SnapStrength = SnapStrength,
            Origin = Origin,
            AngleDeg = AngleDeg,
            EllA = EllA, EllB = EllB, EllC = EllC, EllD = EllD,
            SymmetryOrigin = SymmetryOrigin,
            SymmetryAngleDeg = SymmetryAngleDeg,
            Vp = Vp,
            HorizonOrigin = HorizonOrigin,
            HorizonAngleDeg = HorizonAngleDeg,
            Vp0 = Vp0, Vp1 = Vp1, Vp2 = Vp2,
            FishR0 = FishR0, FishR1 = FishR1,
            FishG0 = FishG0, FishG1 = FishG1,
            FishB0 = FishB0, FishB1 = FishB1,
            FishHorizonCenter = FishHorizonCenter,
            FishHorizonRadius = FishHorizonRadius,
            FishTheta1Deg = FishTheta1Deg,
            FishTheta2Deg = FishTheta2Deg,
            FishTheta3Deg = FishTheta3Deg,
            FishGlobalAngleDeg = FishGlobalAngleDeg,
            FisheyeP = FisheyeP,
            FisheyePMode = FisheyePMode,
            PerspectiveLine0Enabled = PerspectiveLine0Enabled,
            PerspectiveLine1Enabled = PerspectiveLine1Enabled,
            PerspectiveLine2Enabled = PerspectiveLine2Enabled,
        };
        return c;
    }

    /// <summary>Copy all persistent state from another RulerState onto this one.</summary>
    public void CopyFrom(RulerState src)
    {
        Kind = src.Kind;
        Visible = src.Visible;
        SnapEnabled = src.SnapEnabled;
        ForceSnap = src.ForceSnap;
        SnapStrength = src.SnapStrength;
        Origin = src.Origin;
        AngleDeg = src.AngleDeg;
        EllA = src.EllA; EllB = src.EllB; EllC = src.EllC; EllD = src.EllD;
        SymmetryOrigin = src.SymmetryOrigin;
        SymmetryAngleDeg = src.SymmetryAngleDeg;
        Vp = src.Vp;
        HorizonOrigin = src.HorizonOrigin;
        HorizonAngleDeg = src.HorizonAngleDeg;
        Vp0 = src.Vp0; Vp1 = src.Vp1; Vp2 = src.Vp2;
        FishR0 = src.FishR0; FishR1 = src.FishR1;
        FishG0 = src.FishG0; FishG1 = src.FishG1;
        FishB0 = src.FishB0; FishB1 = src.FishB1;
        FishHorizonCenter = src.FishHorizonCenter;
        FishHorizonRadius = src.FishHorizonRadius;
        FishTheta1Deg = src.FishTheta1Deg;
        FishTheta2Deg = src.FishTheta2Deg;
        FishTheta3Deg = src.FishTheta3Deg;
        FishGlobalAngleDeg = src.FishGlobalAngleDeg;
        FisheyeP = src.FisheyeP;
        FisheyePMode = src.FisheyePMode;
        PerspectiveLine0Enabled = src.PerspectiveLine0Enabled;
        PerspectiveLine1Enabled = src.PerspectiveLine1Enabled;
        PerspectiveLine2Enabled = src.PerspectiveLine2Enabled;
    }
}

/// <summary>Undo/redo command for ruler state changes.</summary>
public sealed class RulerEditCommand : IDocumentCommand
{
    private readonly RulerState _before;
    private readonly RulerState _after;

    public string Name => "标尺编辑";

    public RulerEditCommand(RulerState before, RulerState after)
    {
        _before = before.Clone();
        _after = after.Clone();
    }

    public void Undo(Document doc) => doc.Rulers.CopyFrom(_before);
    public void Redo(Document doc) => doc.Rulers.CopyFrom(_after);
}
