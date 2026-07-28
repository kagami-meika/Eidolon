using System;
using System.Collections.Generic;
using Eidolon.Core;
namespace Eidolon.Brush;
public sealed class StrokeSession
{
	private readonly Document _doc;

	private readonly RasterLayer _layer;

	private readonly BrushPreset _preset;

	private readonly ColorRgba8 _color;

	private readonly Stabilizer _stabilizer = new Stabilizer();

	private readonly HashSet<long> _touchedKeys = new HashSet<long>();

	private readonly Dictionary<long, Tile> _before = new Dictionary<long, Tile>();

	private readonly Float2[] _histPos = new Float2[4];

	private readonly float[] _histPr = new float[4];

	private int _histCount;

	private List<Float2>? _willowPath;

	private double _willowLastFillTime;

	private List<double>[]? _scanEdges;

	private List<double>[]? _scanDeltas;

	private readonly HashSet<long> _willowLastPaintedKeys = new HashSet<long>();

	private readonly HashSet<long> _willowFrameKeys = new HashSet<long>();

	private IntRect _willowLastFillRect;

	private const float WillowMinPointDist = 1.5f;

	private const int WillowPreviewMaxPoints = 256;

	// Geometric densify target for Willow fill edges (~sub-pixel chords).

	private const float WillowDensifyMaxSeg = 0.75f;

	private const int WillowDensifyMaxPreview = 1536;

	private const int WillowDensifyMaxFinal = 6144;

	private List<Float2>? _willowDensified;

	private static float[] s_diskLut = new float[257];

	private const int DiskLutSize = 256;

	private const float DiskLutMaxNd = 1.35f;

	private static readonly float DiskLutMaxNd2 = 1.8225001f;

	private Float2? _lastFilteredPos;

	private float _lastFilteredPressure = 1f;

	private float _carryDist;

	private bool _active;

	private readonly float _globalStabilizer;

	private readonly bool _willowOverlap;

	private Float2 _prevCenter;

	private bool _hasPrevCenter;

	public bool IsActive => _active;

	public IntRect DirtyRect { get; private set; }

	public StrokeSession(Document doc, RasterLayer layer, BrushPreset preset, ColorRgba8 color, float globalStabilizer = 0.35f, bool willowOverlap = true)
	{
		_doc = doc;
		_layer = layer;
		_preset = preset;
		_color = color;
		_globalStabilizer = globalStabilizer;
		_willowOverlap = willowOverlap;
	}

	public void Begin(in PointerSample sample)
	{
		_active = true;
		_touchedKeys.Clear();
		_before.Clear();
		_lastFilteredPos = null;
		_lastFilteredPressure = 1f;
		_carryDist = 0f;
		_histCount = 0;
		_willowPath = null;
		_willowDensified?.Clear();
		_willowLastFillTime = 0.0;
		_willowLastPaintedKeys.Clear();
		_willowFrameKeys.Clear();
		_willowLastFillRect = default(IntRect);
		DirtyRect = default(IntRect);
		_hasPrevCenter = false;
		if (_preset.Kind == BrushToolKind.WillowLeaf)
		{
			float strength = ((_preset.Params.StabilizerStrength > 0f) ? _preset.Params.StabilizerStrength : _globalStabilizer);
			_stabilizer.Reset(strength);
			Float2 item = _stabilizer.Filter(sample.DocumentPos, sample.TimeSec);
			_willowPath = new List<Float2>(256) { item };
			_stabilizer.FilterPressure(sample.Pressure, sample.TimeSec);
		}
		else
		{
			float strength2 = ((_preset.Params.StabilizerStrength > 0f) ? _preset.Params.StabilizerStrength : _globalStabilizer);
			_stabilizer.Reset(strength2);
			Move(sample with
			{
				Phase = PointerPhase.Press
			});
		}
	}

	public IntRect Move(in PointerSample sample)
	{
		if (!_active)
		{
			return default(IntRect);
		}
		if ((_layer.Locks & LayerLocks.Pixels) != LayerLocks.None)
		{
			return default(IntRect);
		}
		if (_willowPath != null)
		{
			double timeSec = sample.TimeSec;
			Float2 @float = _stabilizer.Filter(sample.DocumentPos, timeSec);
			List<Float2>? willowPath = _willowPath;
			Float2 float2 = willowPath[willowPath.Count - 1];
			float num = @float.X - float2.X;
			float num2 = @float.Y - float2.Y;
			if (_willowPath.Count == 1 || num * num + num2 * num2 >= 2.25f)
			{
				_willowPath.Add(@float);
			}
			else
			{
				List<Float2>? willowPath2 = _willowPath;
				willowPath2[willowPath2.Count - 1] = @float;
			}
			if (_willowPath.Count >= 3)
			{
				bool flag = _willowPath.Count <= 8;
				double num3 = ((_willowPath.Count > 300) ? 0.05 : ((_willowPath.Count > 120) ? 0.033 : 0.016));
				if (flag || timeSec - _willowLastFillTime >= num3)
				{
					_willowLastFillTime = timeSec;
					WillowIncrementalFill(preview: true);
				}
			}
			return DirtyRect;
		}
		double timeSec2 = sample.TimeSec;
		Float2 float3 = _stabilizer.Filter(sample.DocumentPos, timeSec2);
		float num4 = Math.Clamp(_stabilizer.FilterPressure(sample.Pressure, timeSec2), 0.001f, 1f);
		float num5 = EffectiveSize(num4);
		float spacing = Math.Max(0.25f, num5 * Math.Max(0.02f, _preset.Params.Spacing));
		Float2? lastFilteredPos = _lastFilteredPos;
		if (!lastFilteredPos.HasValue)
		{
			Stamp(float3, num4, num5);
			_lastFilteredPos = float3;
			_lastFilteredPressure = num4;
			PushHist(float3, num4);
			return DirtyRect;
		}
		float num6 = float3.X - _lastFilteredPos.Value.X;
		float num7 = float3.Y - _lastFilteredPos.Value.Y;
		float num8 = MathF.Sqrt(num6 * num6 + num7 * num7);
		if (num8 < 0.0001f)
		{
			return DirtyRect;
		}
		PushHist(float3, num4);
		StampAlongNewSegment(spacing, num8);
		return DirtyRect;
	}

	private void PushHist(Float2 p, float pressure)
	{
		if (_histCount < 4)
		{
			_histPos[_histCount] = p;
			_histPr[_histCount] = pressure;
			_histCount++;
			return;
		}
		_histPos[0] = _histPos[1];
		_histPos[1] = _histPos[2];
		_histPos[2] = _histPos[3];
		_histPos[3] = p;
		_histPr[0] = _histPr[1];
		_histPr[1] = _histPr[2];
		_histPr[2] = _histPr[3];
		_histPr[3] = pressure;
	}

	private void StampAlongNewSegment(float spacing, float chord)
	{
		int n = _histCount;
		Float2 p1 = _histPos[n - 2];
		Float2 p2 = _histPos[n - 1];
		float pr1 = _histPr[n - 2];
		float pr2 = _histPr[n - 1];
		Float2 p0 = (n >= 3) ? _histPos[n - 3] : p1;
		float pr0 = (n >= 3) ? _histPr[n - 3] : pr1;
		// Online: no future sample beyond p2 - reflect chord for end tangent.
		Float2 p3 = new Float2(p2.X + (p2.X - p1.X), p2.Y + (p2.Y - p1.Y));
		float pr3 = Math.Clamp(pr2 + (pr2 - pr1), 0.001f, 1f);
		bool useSpline = n >= 3;
		// Boost micro-steps on sharp bends so spacing walk stays on a smooth arc.
		float curveBoost = 1f;
		if (useSpline)
		{
			float ax = p1.X - p0.X, ay = p1.Y - p0.Y;
			float bx = p2.X - p1.X, by = p2.Y - p1.Y;
			float al = MathF.Sqrt(ax * ax + ay * ay);
			float bl = MathF.Sqrt(bx * bx + by * by);
			if (al > 1e-4f && bl > 1e-4f)
			{
				float dot = Math.Clamp((ax * bx + ay * by) / (al * bl), -1f, 1f);
				curveBoost = 1f + (1f - dot) * 1.5f;
			}
		}
		// ~0.35-0.75px micro-steps (also spacing-relative). Old spacing*0.18 + hard 128
		// left fast flicks visibly faceted.
		float stepPx = Math.Max(0.35f, Math.Min(0.75f, spacing * 0.12f));
		int steps = Math.Max(1, (int)MathF.Ceiling(chord * curveBoost / stepPx));
		int stepCap = Math.Clamp((int)(chord * 4f) + 64, 64, 512);
		steps = Math.Min(steps, stepCap);
		Float2 b0 = p1, b3 = p2;
		Float2 b1, b2;
		float prB0 = pr1, prB3 = pr2, prB1, prB2;
		if (useSpline)
		{
			// Catmull-Rom to cubic Bezier; soften handles with Krita-like velocity similarity.
			b1 = new Float2(p1.X + (p2.X - p0.X) / 6f, p1.Y + (p2.Y - p0.Y) / 6f);
			b2 = new Float2(p2.X - (p3.X - p1.X) / 6f, p2.Y - (p3.Y - p1.Y) / 6f);
			float t1x = p1.X - p0.X, t1y = p1.Y - p0.Y;
			float t2x = p2.X - p1.X, t2y = p2.Y - p1.Y;
			float t1l = MathF.Sqrt(t1x * t1x + t1y * t1y);
			float t2l = MathF.Sqrt(t2x * t2x + t2y * t2y);
			if (t1l > 1e-4f && t2l > 1e-4f)
			{
				float sim = Math.Clamp((t1x * t2x + t1y * t2y) / (t1l * t2l), 0f, 1f);
				sim = Math.Max(0.5f, sim);
				float coeff = 0.8f * (1f - Math.Max(0f, sim - 0.8f));
				float hx1 = b1.X - b0.X, hy1 = b1.Y - b0.Y;
				float hx2 = b2.X - b3.X, hy2 = b2.Y - b3.Y;
				b1 = new Float2(b0.X + hx1 * coeff / 0.8f, b0.Y + hy1 * coeff / 0.8f);
				b2 = new Float2(b3.X + hx2 * coeff / 0.8f, b3.Y + hy2 * coeff / 0.8f);
				if (t1l < t2l)
					b1 = new Float2(b0.X + (b1.X - b0.X) * sim, b0.Y + (b1.Y - b0.Y) * sim);
				else if (t2l < t1l)
					b2 = new Float2(b3.X + (b2.X - b3.X) * sim, b3.Y + (b2.Y - b3.Y) * sim);
			}
			prB1 = pr1 + (pr2 - pr0) / 6f;
			prB2 = pr2 - (pr3 - pr1) / 6f;
		}
		else
		{
			b1 = p1;
			b2 = p2;
			prB1 = pr1;
			prB2 = pr2;
		}
		Float2 prev = p1;
		float prevPr = pr1;
		float carry = _carryDist;
		for (int s = 1; s <= steps; s++)
		{
			float t = (float)s / (float)steps;
			Float2 cur;
			float curPr;
			if (useSpline)
			{
				cur = CubicBezier(b0, b1, b2, b3, t);
				curPr = CubicBezier(prB0, prB1, prB2, prB3, t);
			}
			else
			{
				cur = new Float2(p1.X + (p2.X - p1.X) * t, p1.Y + (p2.Y - p1.Y) * t);
				curPr = pr1 + (pr2 - pr1) * t;
			}
			curPr = Math.Clamp(curPr, 0.001f, 1f);
			float sdx = cur.X - prev.X;
			float sdy = cur.Y - prev.Y;
			float dist = MathF.Sqrt(sdx * sdx + sdy * sdy);
			if (dist < 1e-6f)
			{
				prev = cur;
				prevPr = curPr;
				continue;
			}
			float total = dist + carry;
			for (float pos = spacing - carry; pos <= dist; pos += spacing)
			{
				float u = pos / dist;
				Float2 dabPos = new Float2(prev.X + sdx * u, prev.Y + sdy * u);
				float dabPressure = Math.Clamp(prevPr + (curPr - prevPr) * u, 0.001f, 1f);
				Stamp(dabPos, dabPressure, EffectiveSize(dabPressure));
			}
			carry = total % spacing;
			prev = cur;
			prevPr = curPr;
		}
		_carryDist = carry;
		_lastFilteredPos = p2;
		_lastFilteredPressure = pr2;
	}

	private void WillowIncrementalFill(bool preview = false)
	{
		List<Float2> willowPath = _willowPath;
		if (willowPath.Count < 3)
		{
			return;
		}
		IReadOnlyList<Float2> controlPath = willowPath;
		if (preview && willowPath.Count > WillowPreviewMaxPoints)
		{
			controlPath = BuildWillowPreviewPath(willowPath, WillowPreviewMaxPoints);
		}
		// Control path stays thin; fill outline densified to ~0.75px chords.
		IReadOnlyList<Float2> readOnlyList = DensifyWillowOutline(
			controlPath,
			WillowDensifyMaxSeg,
			preview ? WillowDensifyMaxPreview : WillowDensifyMaxFinal);
		int count = readOnlyList.Count;
		if (count < 3)
		{
			return;
		}
		RestoreWillowLastPainted();
		_willowFrameKeys.Clear();
		double num = double.MaxValue;
		double num2 = double.MaxValue;
		double num3 = double.MinValue;
		double num4 = double.MinValue;
		for (int i = 0; i < count; i++)
		{
			Float2 @float = readOnlyList[i];
			if ((double)@float.X < num)
			{
				num = @float.X;
			}
			if ((double)@float.Y < num2)
			{
				num2 = @float.Y;
			}
			if ((double)@float.X > num3)
			{
				num3 = @float.X;
			}
			if ((double)@float.Y > num4)
			{
				num4 = @float.Y;
			}
		}
		int num5 = Math.Max(0, (int)Math.Ceiling(num - 0.5));
		int num6 = Math.Min(_layer.Surface.Width - 1, (int)Math.Floor(num3 - 0.5));
		int num7 = Math.Max(0, (int)Math.Ceiling(num2 - 0.5));
		int num8 = Math.Min(_layer.Surface.Height - 1, (int)Math.Floor(num4 - 0.5));
		if (num6 < num5 || num8 < num7)
		{
			return;
		}
		int num9 = num8 - num7 + 1;
		if (_scanEdges == null || _scanEdges.Length < num9)
		{
			_scanEdges = new List<double>[num9];
		}
		for (int j = 0; j < num9; j++)
		{
			if (_scanEdges[j] == null)
			{
				_scanEdges[j] = new List<double>(8);
			}
			else
			{
				_scanEdges[j].Clear();
			}
		}
		bool willowOverlap = _willowOverlap;
		List<double>[] array = null;
		if (willowOverlap)
		{
			if (_scanDeltas == null || _scanDeltas.Length < num9)
			{
				_scanDeltas = new List<double>[num9];
			}
			array = _scanDeltas;
			for (int k = 0; k < num9; k++)
			{
				if (array[k] == null)
				{
					array[k] = new List<double>(8);
				}
				else
				{
					array[k].Clear();
				}
			}
		}
		for (int l = 0; l < count; l++)
		{
			int index = (l + 1) % count;
			double num10 = readOnlyList[l].X;
			double num11 = readOnlyList[l].Y;
			double num12 = readOnlyList[index].X;
			double num13 = readOnlyList[index].Y;
			double value = num13 - num11;
			if (Math.Abs(value) < 1E-09)
			{
				continue;
			}
			double num14;
			double num15;
			double num16;
			double num17;
			if (num11 < num13)
			{
				num14 = num10;
				num15 = num11;
				num16 = num12;
				num17 = num13;
			}
			else
			{
				num14 = num12;
				num15 = num13;
				num16 = num10;
				num17 = num11;
			}
			double num18 = (num16 - num14) * (num16 - num14) + (num17 - num15) * (num17 - num15);
			if (num18 < 1E-12)
			{
				continue;
			}
			double item = ((num11 < num13) ? 1.0 : (-1.0));
			int num19 = Math.Max(num7, (int)Math.Ceiling(num15 - 0.5));
			int num20 = Math.Min(num8, (int)Math.Floor(num17 - 0.5 - 1E-12));
			if (num19 > num20)
			{
				continue;
			}
			double num21 = 1.0 / (num17 - num15);
			double num22 = num16 - num14;
			for (int m = num19; m <= num20; m++)
			{
				int num23 = m - num7;
				double num24 = (double)m + 0.5;
				double num25 = (num24 - num15) * num21;
				if (num25 < 0.0)
				{
					num25 = 0.0;
				}
				else if (num25 > 1.0)
				{
					num25 = 1.0;
				}
				double item2 = num14 + num22 * num25;
				_scanEdges[num23].Add(item2);
				if (willowOverlap)
				{
					array[num23].Add(item);
				}
			}
		}
		int tileSize = _layer.Surface.TileSize;
		float opacity = Math.Clamp(_preset.Params.Opacity, 0f, 1f);
		float sr = (float)(int)_color.R / 255f;
		float sg = (float)(int)_color.G / 255f;
		float sb = (float)(int)_color.B / 255f;
		bool lockAlpha = _preset.Params.LockAlpha || (_layer.Locks & LayerLocks.Transparency) != 0;
		for (int n = 0; n < num9; n++)
		{
			List<double> list = _scanEdges[n];
			if (list.Count < 2)
			{
				continue;
			}
			int y = num7 + n;
			if (willowOverlap)
			{
				List<double> list2 = array[n];
				SortEdgesByXD(list, list2);
				double num26 = 0.0;
				int num27 = int.MinValue;
				for (int num28 = 0; num28 < list.Count; num28++)
				{
					num26 += list2[num28];
					if (num27 == int.MinValue && Math.Abs(num26) > 0.5)
					{
						num27 = Math.Max(num5, (int)Math.Ceiling(list[num28] - 0.5));
					}
					else if (num27 != int.MinValue && Math.Abs(num26) < 0.5)
					{
						int num29 = Math.Min(num6, (int)Math.Floor(list[num28] - 0.5 - 1E-12));
						if (num27 <= num29)
						{
							FillScanlineSpan(y, num27, num29, tileSize, sr, sg, sb, opacity, lockAlpha);
						}
						num27 = int.MinValue;
					}
				}
				continue;
			}
			SortEdgesSimpleD(list);
			int num30 = list.Count & -2;
			for (int num31 = 0; num31 + 1 < num30; num31 += 2)
			{
				int num32 = Math.Max(num5, (int)Math.Ceiling(list[num31] - 0.5));
				int num33 = Math.Min(num6, (int)Math.Floor(list[num31 + 1] - 0.5 - 1E-12));
				if (num32 <= num33)
				{
					FillScanlineSpan(y, num32, num33, tileSize, sr, sg, sb, opacity, lockAlpha);
				}
			}
		}
		_willowLastPaintedKeys.Clear();
		foreach (long willowFrameKey in _willowFrameKeys)
		{
			_willowLastPaintedKeys.Add(willowFrameKey);
		}
		IntRect intRect = (_willowLastFillRect = IntRect.FromMinMax(num5, num7, num6, num8));
		DirtyRect = (DirtyRect.IsEmpty ? intRect : IntRect.Union(DirtyRect, intRect));
	}

	private void RestoreWillowLastPainted()
	{
		if (_willowLastPaintedKeys.Count == 0)
		{
			return;
		}
		foreach (long willowLastPaintedKey in _willowLastPaintedKeys)
		{
			if (_before.TryGetValue(willowLastPaintedKey, out Tile value))
			{
				int tx = (int)(willowLastPaintedKey >> 32);
				int ty = (int)(willowLastPaintedKey & 0xFFFFFFFFu);
				Tile orCreateTile = _layer.Surface.GetOrCreateTile(tx, ty);
				Array.Copy(value.Pixels, orCreateTile.Pixels, value.Pixels.Length);
				orCreateTile.Version++;
				_touchedKeys.Add(willowLastPaintedKey);
			}
		}
	}

	private static List<Float2> BuildWillowPreviewPath(List<Float2> path, int maxPoints)
	{
		int count = path.Count;
		if (count <= maxPoints)
		{
			return path;
		}
		List<Float2> list = new List<Float2>(maxPoints);
		int num = maxPoints - 1;
		for (int i = 0; i < num; i++)
		{
			int index = (int)((long)i * (long)(count - 2) / (num - 1));
			list.Add(path[index]);
		}
		list.Add(path[count - 1]);
		return list;
	}

	/// <summary>
	/// Densify Willow outline with linear micro-chords for scanline fill.
	/// Linear only so axis-aligned corners stay exact (CR overshoot breaks half-open bounds).
	/// </summary>
	private List<Float2> DensifyWillowOutline(IReadOnlyList<Float2> path, float maxSegLen, int maxOutPoints)
	{
		int n = path.Count;
		if (n < 3 || maxSegLen < 1e-3f)
		{
			return path as List<Float2> ?? CopyPath(path);
		}
		_willowDensified ??= new List<Float2>(Math.Min(maxOutPoints, 512));
		List<Float2> result = _willowDensified;
		result.Clear();
		int hardCap = Math.Max(n, maxOutPoints);
		float maxSeg2 = maxSegLen * maxSegLen;
		// Linear micro-chords only: Catmull-Rom overshoots corners and breaks axis-aligned half-open fill.
		for (int i = 0; i < n; i++)
		{
			Float2 a = path[i];
			Float2 b = path[(i + 1) % n];
			result.Add(a);
			float dx = b.X - a.X;
			float dy = b.Y - a.Y;
			float chord2 = dx * dx + dy * dy;
			if (chord2 <= maxSeg2)
			{
				continue;
			}
			float chord = MathF.Sqrt(chord2);
			int steps = Math.Max(2, (int)MathF.Ceiling(chord / maxSegLen));
			int remainingVerts = n - i;
			int room = hardCap - result.Count - remainingVerts;
			if (room <= 1)
			{
				continue;
			}
			if (steps - 1 > room)
			{
				steps = room + 1;
			}
			for (int s = 1; s < steps; s++)
			{
				float t = (float)s / (float)steps;
				result.Add(new Float2(a.X + dx * t, a.Y + dy * t));
			}
		}
		return result.Count >= 3 ? result : (path as List<Float2> ?? CopyPath(path));
	}

	private static List<Float2> CopyPath(IReadOnlyList<Float2> path)
	{
		List<Float2> copy = new List<Float2>(path.Count);
		for (int i = 0; i < path.Count; i++)
		{
			copy.Add(path[i]);
		}
		return copy;
	}

	private static Float2 CubicBezier(Float2 b0, Float2 b1, Float2 b2, Float2 b3, float t)
	{
		float u = 1f - t;
		float tt = t * t;
		float uu = u * u;
		float uuu = uu * u;
		float ttt = tt * t;
		return new Float2(
			uuu * b0.X + 3f * uu * t * b1.X + 3f * u * tt * b2.X + ttt * b3.X,
			uuu * b0.Y + 3f * uu * t * b1.Y + 3f * u * tt * b2.Y + ttt * b3.Y);
	}

	private static float CubicBezier(float b0, float b1, float b2, float b3, float t)
	{
		float u = 1f - t;
		float tt = t * t;
		float uu = u * u;
		float uuu = uu * u;
		float ttt = tt * t;
		return uuu * b0 + 3f * uu * t * b1 + 3f * u * tt * b2 + ttt * b3;
	}

	private void FillScanlineSpan(int y, int x0, int x1, int ts, float sr0, float sg0, float sb0, float opacity, bool lockAlpha)
	{
		int num = -1;
		int num2 = -1;
		long num3 = -1L;
		Tile tile = null;
		for (int i = x0; i <= x1; i++)
		{
			float num4 = _doc.Selection.Coverage(i, y);
			if (num4 <= 0.001f)
			{
				continue;
			}
			int num5 = i / ts;
			int num6 = y / ts;
			long num7 = TileSurface.Key(num5, num6);
			if (num7 != num3)
			{
				tile = null;
				num3 = num7;
				num = num5;
				num2 = num6;
				EnsureBefore(num7);
				_willowFrameKeys.Add(num7);
			}
			if (tile == null)
			{
				tile = _layer.Surface.GetOrCreateTile(num, num2);
			}
			int num8 = i - num * ts;
			int num9 = y - num2 * ts;
			int num10 = num9 * ts + num8;
			ColorRgba8 colorRgba = tile.Pixels[num10];
			float num11 = opacity * num4;
			if (!(num11 <= 0.001f) && (!lockAlpha || colorRgba.A != 0))
			{
				float num12 = num11;
				if (lockAlpha)
				{
					num12 *= (float)(int)colorRgba.A / 255f;
				}
				float num13 = (float)(int)colorRgba.A / 255f;
				float num14 = num12 + num13 * (1f - num12);
				float value;
				float value2;
				float value3;
				if (num14 <= 1E-06f)
				{
					value = (value2 = (value3 = 0f));
				}
				else
				{
					float num15 = (float)(int)colorRgba.R / 255f;
					float num16 = (float)(int)colorRgba.G / 255f;
					float num17 = (float)(int)colorRgba.B / 255f;
					value = (sr0 * num12 + num15 * num13 * (1f - num12)) / num14;
					value2 = (sg0 * num12 + num16 * num13 * (1f - num12)) / num14;
					value3 = (sb0 * num12 + num17 * num13 * (1f - num12)) / num14;
				}
				if (lockAlpha)
				{
					num14 = num13;
				}
				ColorRgba8[] pixels = tile.Pixels;
				pixels[num10] = new ColorRgba8((byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f), (byte)(Math.Clamp(value2, 0f, 1f) * 255f + 0.5f), (byte)(Math.Clamp(value3, 0f, 1f) * 255f + 0.5f), (byte)Math.Clamp((int)(num14 * 255f + 0.5f), 0, 255));
				tile.Version++;
			}
		}
	}

	private static Float2 CatmullRom(Float2 p0, Float2 p1, Float2 p2, Float2 p3, float t)
	{
		float num = t * t;
		float num2 = num * t;
		float x = 0.5f * (2f * p1.X + (0f - p0.X + p2.X) * t + (2f * p0.X - 5f * p1.X + 4f * p2.X - p3.X) * num + (0f - p0.X + 3f * p1.X - 3f * p2.X + p3.X) * num2);
		float y = 0.5f * (2f * p1.Y + (0f - p0.Y + p2.Y) * t + (2f * p0.Y - 5f * p1.Y + 4f * p2.Y - p3.Y) * num + (0f - p0.Y + 3f * p1.Y - 3f * p2.Y + p3.Y) * num2);
		return new Float2(x, y);
	}

	private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
	{
		float num = t * t;
		float num2 = num * t;
		return 0.5f * (2f * p1 + (0f - p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * num + (0f - p0 + 3f * p1 - 3f * p2 + p3) * num2);
	}

	public TileEditCommand? End()
	{
		if (!_active)
		{
			return null;
		}
		_active = false;
		if (_willowPath != null)
		{
			return EndWillowFill();
		}
		if (_touchedKeys.Count == 0)
		{
			return null;
		}
		Dictionary<long, Tile> dictionary = new Dictionary<long, Tile>();
		foreach (long touchedKey in _touchedKeys)
		{
			if (_layer.Surface.TryGetTile((int)(touchedKey >> 32), (int)(touchedKey & 0xFFFFFFFFu), out Tile tile))
			{
				dictionary[touchedKey] = tile.Clone();
			}
			else
			{
				dictionary[touchedKey] = new Tile(_layer.Surface.TileSize);
			}
		}
		return new TileEditCommand(_layer.Id, _before, dictionary, "Stroke");
	}

	private TileEditCommand? EndWillowFill()
	{
		WillowIncrementalFill();
		if (_touchedKeys.Count == 0)
		{
			return null;
		}
		Dictionary<long, Tile> dictionary = new Dictionary<long, Tile>();
		int tileSize = _layer.Surface.TileSize;
		foreach (long touchedKey in _touchedKeys)
		{
			if (_layer.Surface.TryGetTile((int)(touchedKey >> 32), (int)(touchedKey & 0xFFFFFFFFu), out Tile tile))
			{
				dictionary[touchedKey] = tile.Clone();
			}
			else
			{
				dictionary[touchedKey] = new Tile(tileSize);
			}
		}
		return new TileEditCommand(_layer.Id, _before, dictionary, "WillowLeaf fill");
	}

	private static void SortEdgesSimpleD(List<double> edges)
	{
		for (int i = 1; i < edges.Count; i++)
		{
			double num = edges[i];
			int num2 = i - 1;
			while (num2 >= 0 && edges[num2] > num)
			{
				edges[num2 + 1] = edges[num2];
				num2--;
			}
			edges[num2 + 1] = num;
		}
	}

	private static void SortEdgesByXD(List<double> edges, List<double> deltas)
	{
		for (int i = 1; i < edges.Count; i++)
		{
			double num = edges[i];
			double value = deltas[i];
			int num2 = i - 1;
			while (num2 >= 0 && edges[num2] > num)
			{
				edges[num2 + 1] = edges[num2];
				deltas[num2 + 1] = deltas[num2];
				num2--;
			}
			edges[num2 + 1] = num;
			deltas[num2 + 1] = value;
		}
	}

	private float EffectiveSize(float pressure)
	{
		float num = Math.Clamp(_preset.Params.MinSizeRatio, 0f, 1f);
		float x = Math.Clamp(pressure, 0f, 1f);
		x = (_preset.Params.SizeByPressure ? MathF.Pow(x, 0.9f) : 1f);
		float val = _preset.Params.SizePx * (num + (1f - num) * x);
		return Math.Max(0.5f, val);
	}

	private void EnsureBefore(long key)
	{
		if (!_before.ContainsKey(key))
		{
			int tx = (int)(key >> 32);
			int ty = (int)(key & 0xFFFFFFFFu);
			if (_layer.Surface.TryGetTile(tx, ty, out Tile tile))
			{
				_before[key] = tile.Clone();
			}
			else
			{
				_before[key] = new Tile(_layer.Surface.TileSize);
			}
			_touchedKeys.Add(key);
		}
	}

	private void Stamp(Float2 center, float pressure, float size)
	{
		bool eraseMode = _preset.Params.EraseMode;
		float num = Math.Clamp(pressure, 0.001f, 1f);
		float num2 = _preset.Params.Flow;
		if (_preset.Params.FlowByPressure)
		{
			num2 *= 0.2f + 0.8f * num;
		}
		float num3 = 1f;
		if (_preset.Params.OpacityByPressure)
		{
			num3 = 0.12f + 0.88f * MathF.Pow(num, 0.95f);
		}
		float num4 = Math.Clamp(_preset.Params.Opacity * num2 * num3, 0f, 1f);
		if (_preset.Kind == BrushToolKind.Airbrush)
		{
			num4 = Math.Clamp(_preset.Params.Opacity * num2 * (0.12f + 0.88f * num), 0f, 1f);
		}
		float num5 = Math.Clamp(_preset.Params.Hardness, 0f, 1f);
		float num6 = Math.Clamp(_preset.Params.SoftEdge, 0f, 1f);
		bool antiAlias = _preset.Params.AntiAlias;
		bool flag = _preset.Params.LockAlpha || (_layer.Locks & LayerLocks.Transparency) != 0;
		bool flag2 = _preset.Kind == BrushToolKind.Binary;
		float num7 = size * 0.5f;
		float num8 = (antiAlias ? 1.25f : 0.01f);
		int value = (int)MathF.Floor(center.X - num7 - num8);
		int value2 = (int)MathF.Ceiling(center.X + num7 + num8);
		int value3 = (int)MathF.Floor(center.Y - num7 - num8);
		int value4 = (int)MathF.Ceiling(center.Y + num7 + num8);
		value = Math.Clamp(value, 0, _layer.Surface.Width - 1);
		value2 = Math.Clamp(value2, 0, _layer.Surface.Width - 1);
		value3 = Math.Clamp(value3, 0, _layer.Surface.Height - 1);
		value4 = Math.Clamp(value4, 0, _layer.Surface.Height - 1);
		if (value2 < value || value4 < value3)
		{
			return;
		}
		int tileSize = _layer.Surface.TileSize;
		IntRect intRect = IntRect.FromMinMax(value, value3, value2, value4);
		DirtyRect = (DirtyRect.IsEmpty ? intRect : IntRect.Union(DirtyRect, intRect));
		float num9 = Math.Clamp(num5 * (1f - num6 * 0.35f), 0f, 0.98f);
		float num10 = (float)(int)_color.R / 255f;
		float num11 = (float)(int)_color.G / 255f;
		float num12 = (float)(int)_color.B / 255f;
		float aaWidth = (antiAlias ? 1f : 0.02f);
		float num13 = 0.00390625f;
		for (int i = 0; i <= 256; i++)
		{
			float x = (float)i * num13 * DiskLutMaxNd2;
			float num14 = MathF.Sqrt(x);
			float d = num14 * num7;
			float num15 = DiskCoverage(d, num7, aaWidth);
			if (num15 <= 0.001f)
			{
				s_diskLut[i] = 0f;
				continue;
			}
			if (num14 > num9 && num14 < 1f)
			{
				float num16 = (num14 - num9) / (1f - num9);
				num15 *= 1f - num16 * num16 * num16 * (num16 * (num16 * 6f - 15f) + 10f);
			}
			else if (num14 >= 1f)
			{
				num15 = 0f;
			}
			s_diskLut[i] = num15;
		}
		float num17 = 1f / (num7 * num7);
		for (int j = value3; j <= value4; j++)
		{
			for (int k = value; k <= value2; k++)
			{
				float num18 = (float)k + 0.5f - center.X;
				float num19 = (float)j + 0.5f - center.Y;
				float num20 = (num18 * num18 + num19 * num19) * num17;
				if (num20 >= DiskLutMaxNd2)
				{
					continue;
				}
				float num21 = num20 * (256f / DiskLutMaxNd2);
				int num22 = (int)num21;
				float num23 = num21 - (float)num22;
				float num24 = ((num22 < 256) ? (s_diskLut[num22] + (s_diskLut[num22 + 1] - s_diskLut[num22]) * num23) : s_diskLut[256]);
				if (num24 <= 0.001f)
				{
					continue;
				}
				if (flag2)
				{
					num24 = ((num24 >= 0.5f) ? 1f : 0f);
				}
				float num25 = Math.Clamp(_preset.Params.TextureStrength, 0f, 1f);
				if (num25 > 0.001f)
				{
					float scale = Math.Max(0.05f, _preset.Params.TextureScale);
					float num26 = HashGrain(k, j, _preset.Params.TextureSeed, scale);
					float value5 = 1f - num25 * (1f - num26);
					num24 *= Math.Clamp(value5, 0f, 1f);
				}
				float num27 = num24 * num4;
				float num28 = _doc.Selection.Coverage(k, j);
				if (num28 <= 0.001f)
				{
					continue;
				}
				num27 *= num28;
				if (num27 <= 0.001f)
				{
					continue;
				}
				int num29 = k / tileSize;
				int num30 = j / tileSize;
				long key = TileSurface.Key(num29, num30);
				EnsureBefore(key);
				Tile orCreateTile = _layer.Surface.GetOrCreateTile(num29, num30);
				int num31 = k - num29 * tileSize;
				int num32 = j - num30 * tileSize;
				int num33 = num32 * tileSize + num31;
				ColorRgba8 colorRgba = orCreateTile.Pixels[num33];
				if (eraseMode)
				{
					float num34 = (float)(int)colorRgba.A / 255f * (1f - num27);
					orCreateTile.Pixels[num33] = new ColorRgba8(colorRgba.R, colorRgba.G, colorRgba.B, (byte)Math.Clamp((int)(num34 * 255f + 0.5f), 0, 255));
					orCreateTile.Version++;
				}
				else if (_preset.Kind == BrushToolKind.Smudge)
				{
					float value6 = Math.Clamp(_preset.Params.SmudgeStrength, 0f, 1f) * num27;
					float x2 = center.X;
					float x3 = center.Y;
					if (_hasPrevCenter)
					{
						float num35 = center.X - _prevCenter.X;
						float num36 = center.Y - _prevCenter.Y;
						float num37 = MathF.Sqrt(num35 * num35 + num36 * num36);
						if (num37 > 0.001f)
						{
							float num38 = Math.Min(num7 * 0.85f, 12f);
							x2 = (float)k + 0.5f - num35 / num37 * num38;
							x3 = (float)j + 0.5f - num36 / num37 * num38;
						}
					}
					int x4 = (int)MathF.Floor(x2);
					int y = (int)MathF.Floor(x3);
					ColorRgba8 pixel = _layer.Surface.GetPixel(x4, y);
					float num39 = (float)(int)colorRgba.R / 255f;
					float num40 = (float)(int)colorRgba.G / 255f;
					float num41 = (float)(int)colorRgba.B / 255f;
					float num42 = (float)(int)colorRgba.A / 255f;
					float num43 = (float)(int)pixel.R / 255f;
					float num44 = (float)(int)pixel.G / 255f;
					float num45 = (float)(int)pixel.B / 255f;
					float num46 = (float)(int)pixel.A / 255f;
					float num47 = Math.Clamp(value6, 0f, 1f);
					float num48 = num39 * (1f - num47) + num43 * num47;
					float num49 = num40 * (1f - num47) + num44 * num47;
					float num50 = num41 * (1f - num47) + num45 * num47;
					float num51 = num42 * (1f - num47 * 0.35f) + num46 * (num47 * 0.35f);
					if (flag)
					{
						num51 = num42;
					}
					orCreateTile.Pixels[num33] = new ColorRgba8((byte)(num48 * 255f + 0.5f), (byte)(num49 * 255f + 0.5f), (byte)(num50 * 255f + 0.5f), (byte)Math.Clamp((int)(num51 * 255f + 0.5f), 0, 255));
					orCreateTile.Version++;
				}
				else if (!flag || colorRgba.A != 0)
				{
					float num52 = num27;
					if (flag)
					{
						num52 *= (float)(int)colorRgba.A / 255f;
					}
					float num53 = (float)(int)colorRgba.A / 255f;
					float num54 = num10;
					float num55 = num11;
					float num56 = num12;
					float num57 = (float)(int)colorRgba.R / 255f;
					float num58 = (float)(int)colorRgba.G / 255f;
					float num59 = (float)(int)colorRgba.B / 255f;
					if (_preset.Params.Blend > 0.001f && num53 > 0.001f)
					{
						float num60 = Math.Clamp(_preset.Params.Blend, 0f, 1f) * num;
						num54 = num54 * (1f - num60) + num57 * num60;
						num55 = num55 * (1f - num60) + num58 * num60;
						num56 = num56 * (1f - num60) + num59 * num60;
					}
					float num61 = num52 + num53 * (1f - num52);
					float value7;
					float value8;
					float value9;
					if (num61 <= 1E-06f)
					{
						value7 = (value8 = (value9 = 0f));
					}
					else
					{
						value7 = (num54 * num52 + num57 * num53 * (1f - num52)) / num61;
						value8 = (num55 * num52 + num58 * num53 * (1f - num52)) / num61;
						value9 = (num56 * num52 + num59 * num53 * (1f - num52)) / num61;
					}
					if (flag)
					{
						num61 = num53;
					}
					value7 = Math.Clamp(value7, 0f, 1f);
					value8 = Math.Clamp(value8, 0f, 1f);
					value9 = Math.Clamp(value9, 0f, 1f);
					orCreateTile.Pixels[num33] = new ColorRgba8((byte)(value7 * 255f + 0.5f), (byte)(value8 * 255f + 0.5f), (byte)(value9 * 255f + 0.5f), (byte)Math.Clamp((int)(num61 * 255f + 0.5f), 0, 255));
					orCreateTile.Version++;
				}
			}
		}
		_prevCenter = center;
		_hasPrevCenter = true;
	}

	private static float HashGrain(int x, int y, int seed, float scale)
	{
		float num = (float)x * scale * 0.15f;
		float num2 = (float)y * scale * 0.15f;
		int num3 = (int)MathF.Floor(num);
		int num4 = (int)MathF.Floor(num2);
		float num5 = num - (float)num3;
		float num6 = num2 - (float)num4;
		num5 = num5 * num5 * (3f - 2f * num5);
		num6 = num6 * num6 * (3f - 2f * num6);
		float num7 = Hash2(num3, num4, seed);
		float num8 = Hash2(num3 + 1, num4, seed);
		float num9 = Hash2(num3, num4 + 1, seed);
		float num10 = Hash2(num3 + 1, num4 + 1, seed);
		float num11 = num7 * (1f - num5) + num8 * num5;
		float num12 = num9 * (1f - num5) + num10 * num5;
		return num11 * (1f - num6) + num12 * num6;
	}

	private static float Hash2(int x, int y, int seed)
	{
		uint num = (uint)(x * 374761393 + y * 668265263 + seed * 362437);
		num = (num ^ (num >> 13)) * 1274126177;
		num ^= num >> 16;
		return (float)(num & 0xFFFFFF) / 16777215f;
	}

	private static float DiskCoverage(float d, float r, float aaWidth)
	{
		if (d <= r - aaWidth)
		{
			return 1f;
		}
		if (d >= r + aaWidth)
		{
			return 0f;
		}
		float value = (d - (r - aaWidth)) / Math.Max(2f * aaWidth, 0.0001f);
		value = Math.Clamp(value, 0f, 1f);
		return 1f - value * value * (3f - 2f * value);
	}
}
