using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Eidolon.Brush;
using Eidolon.Core;
using Eidolon.Input;

namespace Eidolon.App.Controls;

public enum CanvasTool
{
    Brush,
    Fill,
    RectSelect,
    Lasso,
    MagicWand,
    Gradient,
    VectorPen,
    VectorNode,    // select/move/delete nodes
    VectorCloseFill, // close path + fill
    VectorSpline,  // click-to-place Catmull-Rom control points
    VectorEraser,  // erase vector strokes by hit / drag
    TextPlace,
    FrameRect,
    Select   // move/edit ruler control points
}

public sealed class CanvasView : FrameworkElement
{
    private readonly ViewportState _viewport = new();
    private WriteableBitmap? _bitmap;
    private byte[]? _pixels;
    private int _stride;
    private StrokeSession? _stroke;
    private bool _panning;
    private Point _lastPanPoint;
    private bool _spaceDown;
    private BrushPreset _preset = BrushPreset.DefaultPencil();
    private float _stabilizer = 0.35f;
    private float _brushSize = 8f;
    private CanvasTool _tool = CanvasTool.Brush;
    private bool _selDragging;
    private bool _frameDragging;
    private Point _selStartScreen;
    private Float2 _selStartDoc;
    private Float2 _selCurrentDoc;
    private readonly List<Float2> _lassoPts = new();
    private bool _gradDragging;
    private Float2 _gradP0;
    private Float2 _gradP1;
    private readonly System.Windows.Media.Brush[] _vpFill = new System.Windows.Media.Brush[]
    {
        new SolidColorBrush(Color.FromRgb(220, 60, 60)),
        new SolidColorBrush(Color.FromRgb(40, 170, 70)),
        new SolidColorBrush(Color.FromRgb(50, 100, 220)),
    };
    private bool _showGradPreview;
    public SelectionMode SelectMode { get; set; } = SelectionMode.Replace;
    public GradientType GradientKind { get; set; } = GradientType.Linear;
    public bool GradientToTransparent { get; set; }
    private VectorStroke? _vectorStroke;
    private bool _vectorDrawing;
    public float VectorBaseWidth { get; set; } = 3f;
    private List<VectorStroke>? _vectorStrokeBefore;
    private int _selStrokeIndex = -1;
    private int _selPointIndex = -1;
    private bool _nodeDragging;
    private Float2 _nodeDragLast;
    private List<VectorStroke>? _nodeEditBefore;
    /// <summary>Spline tool: placing control points until right-click finishes.</summary>
    private bool _splinePlacing;
    private bool _vectorErasing;
    private bool _straightLine;
    private Float2? _straightOrigin;
    private readonly WinTabTabletService _wintab = new();
    private readonly PointerPenService _pointerPen = new();
    private HwndSource? _hwndSource;
    private bool _stylusDrawing;
    private long _lastStylusTicks;
    private bool _rulerHandleDragging;
    private RulerHandle _dragHandle = RulerHandle.None;
    private Float2 _rulerDragStartDoc;
    private Float2 _rulerDragLastDoc;
    /// <summary>World-space base polar for Shift+theta relative 45° steps.</summary>
    private float? _fishSnapBaseWorldDeg;
    /// <summary>True when Shift+Alt dragging FishP (rotate around O, 45° snap, keep radius).</summary>
    private bool _fishPRotSnapActive;
    /// <summary>Base angle (deg) for FishP Shift+Alt 45° rotation.</summary>
    private float _fishPRotSnapBaseDeg;
    /// <summary>Ruler state snapshot before a drag for undo.</summary>
    private RulerState? _rulerBeforeSnapshot;
    private enum RulerEditMode { Handle, Translate, Rotate, Scale }
    private RulerEditMode _rulerEditMode = RulerEditMode.Handle;
    private StrokeSession? _mirrorStroke;

    public Document? Document { get; private set; }
    public ViewportState Viewport => _viewport;
    public WinTabTabletService WinTab => _wintab;
    public PointerPenService PointerPen => _pointerPen;
    public string PenStatus =>
        _pointerPen.HasRecentSample ? _pointerPen.Status :
        _wintab.HasRecentPacket ? _wintab.Status :
        (_wintab.IsAvailable ? _wintab.Status : _pointerPen.Status);

    public event EventHandler? DocumentChanged;
    public event EventHandler? HistoryChanged;
    public event EventHandler? StatusChanged;

    public float Stabilizer
    {
        get => _stabilizer;
        set => _stabilizer = Math.Clamp(value, 0f, 1f);
    }

    public float BrushSize
    {
        get => _brushSize;
        set
        {
            _brushSize = Math.Clamp(value, 1f, 500f);
            _preset.Params.SizePx = _brushSize;
        }
    }

    public CanvasTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            // Release any capture so UI outside canvas stays clickable
            if (_rulerHandleDragging)
            {
                _rulerHandleDragging = false; _fishSnapBaseWorldDeg = null;
                _fishPRotSnapActive = false;
                _rulerBeforeSnapshot = null;
                _dragHandle = RulerHandle.None;
                if (IsMouseCaptured) ReleaseMouseCapture();
            }
            _tool = value;
            UpdateRulerPreviewFlag();
            if (Document is not null)
            {
                Document.Rulers.HoverDoc = null;
                Document.Rulers.ActiveHandle = RulerHandle.None;
            }
            InvalidateVisual();
        }
    }

    private void UpdateRulerPreviewFlag()
    {
        if (Document is null) return;
        // Preview is a ruler visibility feature, independent of the active tool.
        Document.Rulers.PreviewEnabled = Document.Rulers.Visible;
        if (!Document.Rulers.PreviewEnabled)
            Document.Rulers.HoverDoc = null;
    }

    public void UpdateRulerPreviewFromUi() => UpdateRulerPreviewFlag();

    public bool StraightLineMode
    {
        get => _straightLine;
        set => _straightLine = value;
    }

    public BrushPreset Preset
    {
        get => _preset;
        set
        {
            _preset = value;
            _preset.Params.SizePx = _brushSize;
        }
    }

    public bool LockAlphaBrush
    {
        get => _preset.Params.LockAlpha;
        set => _preset.Params.LockAlpha = value;
    }

    public CanvasView()
    {
        Focusable = true;
        ImeInput.ConfigureForCanvas(this);
        // Default: crisp pixels when zoomed in. Zoomed-out mode is applied in OnRender.
        ApplyViewportBitmapScaling(_viewport.Scale);
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);
        Stylus.SetIsTouchFeedbackEnabled(this, false);
        Loaded += OnLoaded;
        CompositionTarget.Rendering += OnCompositionRender;
        Unloaded += (_, _) =>
        {
            CompositionTarget.Rendering -= OnCompositionRender;
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }
            _wintab.Dispose();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var src = PresentationSource.FromVisual(this) as HwndSource;
        if (src != null)
        {
            _hwndSource = src;
            src.AddHook(WndProc);
            _wintab.TryInitialize(src.Handle);
            Eidolon.App.Logging.AppLog.Info($"Pen init WinTab={_wintab.Status} err={_wintab.LastError}", "Pen");
            try
            {
                var tablets = Tablet.TabletDevices;
                Eidolon.App.Logging.AppLog.Info($"WPF TabletDevices count={tablets.Count}", "Pen");
                foreach (TabletDevice td in tablets)
                {
                    Eidolon.App.Logging.AppLog.Info($"  tablet name={td.Name} type={td.Type} product={td.ProductId}", "Pen");
                    try
                    {
                        var cap = td.TabletHardwareCapabilities;
                        Eidolon.App.Logging.AppLog.Info($"  caps={cap}", "Pen");
                    }
                    catch { /* ignore */ }
                }
            }
            catch (Exception ex)
            {
                Eidolon.App.Logging.AppLog.Warn("TabletDevices enum failed: " + ex.Message, "Pen");
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        _pointerPen.ProcessMessage(msg, wParam, lParam);
        _wintab.ProcessMessage(msg, wParam, lParam);
        return IntPtr.Zero;
    }

    public void SetDocument(Document doc)
    {
        Document = doc;
        doc.History.Changed += (_, _) =>
        {
            FullRedraw();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        };
        AllocateBitmap();
        FullRedraw();
        UpdateRulerPreviewFlag();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void NewDocument(int width, int height) => SetDocument(new Document(width, height));

    public void Undo()
    {
        if (Document is null) return;
        Document.History.Undo(Document);
    }

    public void Redo()
    {
        if (Document is null) return;
        Document.History.Redo(Document);
    }

    public void FullRedraw()
    {
        if (Document is null || _bitmap is null || _pixels is null) return;
        EnsureTextCaches();
        var full = new IntRect(0, 0, Document.Width, Document.Height);
        Compositor.CompositeToPbgra(Document, _pixels, _stride, full);
        _bitmap.WritePixels(new Int32Rect(0, 0, Document.Width, Document.Height), _pixels, _stride, 0);
        InvalidateVisual();
    }

    public void RedrawDirty(IntRect dirty)
    {
        if (Document is null || _bitmap is null || _pixels is null || dirty.IsEmpty) return;
        dirty = dirty.Inflate(2).ClampTo(Document.Width, Document.Height);
        if (dirty.IsEmpty) return;
        Compositor.CompositeToPbgra(Document, _pixels, _stride, dirty);
        // Copy dirty rect to contiguous buffer for WritePixels
        int bw = dirty.Width;
        int bh = dirty.Height;
        var chunk = new byte[bw * bh * 4];
        for (int y = 0; y < bh; y++)
        {
            int src = (dirty.Y + y) * _stride + dirty.X * 4;
            int dst = y * bw * 4;
            Buffer.BlockCopy(_pixels, src, chunk, dst, bw * 4);
        }
        _bitmap.WritePixels(new Int32Rect(dirty.X, dirty.Y, bw, bh), chunk, bw * 4, 0);
        InvalidateVisual();
    }

    private void AllocateBitmap()
    {
        if (Document is null) return;
        _stride = Document.Width * 4;
        _pixels = new byte[_stride * Document.Height];
        _bitmap = new WriteableBitmap(Document.Width, Document.Height, 96, 96, PixelFormats.Pbgra32, null);
        ApplyViewportBitmapScaling(_viewport.Scale);
    }

    /// <summary>
    /// Zoomed in: nearest-neighbor (pixel-crisp). Zoomed out: high-quality Fant
    /// resampling (WPF's best downscale; Lanczos-like smoothness, no blocky shrink).
    /// </summary>
    private void ApplyViewportBitmapScaling(float scale)
    {
        // WPF has no true Lanczos kernel; HighQuality maps to Fant polyphase filtering,
        // which is the intended high-quality downscale path for canvas preview.
        var mode = scale < 0.999f
            ? BitmapScalingMode.HighQuality
            : BitmapScalingMode.NearestNeighbor;
        RenderOptions.SetBitmapScalingMode(this, mode);
        if (_bitmap is not null)
            RenderOptions.SetBitmapScalingMode(_bitmap, mode);
        // Aliased edges help integer zooms; let the smoother path anti-alias when shrinking.
        RenderOptions.SetEdgeMode(this, scale < 0.999f ? EdgeMode.Unspecified : EdgeMode.Aliased);
    }

    private int _hudFrameSkip;
    private void OnCompositionRender(object? sender, EventArgs e)
    {
        if (!IsVisible || !IsLoaded) return;
        // throttle HUD/poll to ~15fps equivalent to avoid UI thrash
        _hudFrameSkip++;
        if (_hudFrameSkip % 4 != 0) return;
        if (!(_stroke is { IsActive: true } || _vectorDrawing || _pointerPen.HasRecentSample || _wintab.HasRecentPacket))
            return;
        try { _wintab.Poll(); } catch { /* ignore */ }
        float p = -1f;
        string src = _hudPressureSrc;
        float ptr = _pointerPen.GetPressureOrDefault(-1f);
        if (ptr > 0) { p = ptr; src = "Pointer"; }
        float wt = _wintab.GetPressureOrDefault(-1f);
        if (wt > 0) { p = wt; src = "WinTab"; }
        if (p > 0 && Math.Abs(p - _hudPressure) > 0.02f)
        {
            _hudPressure = p;
            _hudPressureSrc = src;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Keep WinTab queue drained even if WT_PACKET messages are missed
        if (IsVisible)
        {
            try { _wintab.Poll(); } catch { /* ignore */ }
        }

        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE4, 0xE1, 0xDB)), null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        if (Document is null || _bitmap is null) return;

        var m = _viewport.CreateMatrix((float)ActualWidth, (float)ActualHeight, Document.Width, Document.Height);
        // Snap translation when scale is near-integer for sharper pixels
        float sc = _viewport.Scale;
        ApplyViewportBitmapScaling(sc);
        if (Math.Abs(sc - MathF.Round(sc)) < 0.001f && sc >= 1f)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            m.M31 = (float)(Math.Round(m.M31 * dpi) / dpi);
            m.M32 = (float)(Math.Round(m.M32 * dpi) / dpi);
        }
        var group = new TransformGroup();
        group.Children.Add(new MatrixTransform(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32));
        dc.PushTransform(group);

        if (Document.Background.Kind == DocumentBackgroundKind.Transparent)
            dc.DrawRectangle(CreateCheckerBrush(), null, new Rect(0, 0, Document.Width, Document.Height));

        // Document blit: NN when zoomed in, HighQuality (Fant) when zoomed out.
        var scaleMode = sc < 0.999f
            ? BitmapScalingMode.HighQuality
            : BitmapScalingMode.NearestNeighbor;
        var imgBrush = new ImageBrush(_bitmap)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            Viewport = new Rect(0, 0, Document.Width, Document.Height),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.None
        };
        RenderOptions.SetBitmapScalingMode(imgBrush, scaleMode);
        dc.DrawRectangle(imgBrush, null, new Rect(0, 0, Document.Width, Document.Height));
        DrawSelectionOverlay(dc);
        DrawToolPreview(dc);
        DrawVectorOverlay(dc);
        DrawRulers(dc);
        dc.Pop();

        // Pressure diagnostic HUD (top-left, screen space)
        var hudText = new FormattedText(
            $"P={_hudPressure:F2}  src={_hudPressureSrc}\n{PenStatus}\nKeys:1 soft 2 mid 3 hard 0 auto",
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            14,
            Brushes.DarkRed,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), null, new Rect(8, 8, hudText.Width + 16, hudText.Height + 12));
        dc.DrawText(hudText, new Point(16, 12));
        // pressure bar
        dc.DrawRectangle(Brushes.LightGray, null, new Rect(8, 8 + hudText.Height + 16, 120, 10));
        dc.DrawRectangle(Brushes.IndianRed, null, new Rect(8, 8 + hudText.Height + 16, 120 * Math.Clamp(_hudPressure, 0, 1), 10));
    }


    private void DrawSelectionOverlay(DrawingContext dc)
    {
        if (Document is null) return;
        var sel = Document.Selection;
        if (sel.IsEmpty || !sel.OutlineVisible) return;
        var b = sel.Bounds;
        if (b.IsEmpty) return;

        // Marching ants via dashed pen animation from tick
        double phase = (Environment.TickCount64 / 80.0) % 8.0;
        var pen1 = new Pen(Brushes.Black, 1.0 / Math.Max(_viewport.Scale, 0.01))
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, phase)
        };
        var pen2 = new Pen(Brushes.White, 1.0 / Math.Max(_viewport.Scale, 0.01))
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, phase + 4)
        };
        var rect = new Rect(b.X, b.Y, b.Width, b.Height);
        dc.DrawRectangle(null, pen1, rect);
        dc.DrawRectangle(null, pen2, rect);
    }

    private void DrawToolPreview(DrawingContext dc)
    {
        if (Document is null) return;
        double inv = 1.0 / Math.Max(_viewport.Scale, 0.01);
        var pen = new Pen(Brushes.DodgerBlue, inv) { };

        if (_selDragging && (_tool == CanvasTool.RectSelect))
        {
            double x0 = Math.Min(_selStartDoc.X, _selCurrentDoc.X);
            double y0 = Math.Min(_selStartDoc.Y, _selCurrentDoc.Y);
            double x1 = Math.Max(_selStartDoc.X, _selCurrentDoc.X);
            double y1 = Math.Max(_selStartDoc.Y, _selCurrentDoc.Y);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 30, 120, 255)), pen,
                new Rect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0)));
        }

        if (_tool == CanvasTool.Lasso && _lassoPts.Count > 0)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(_lassoPts[0].X, _lassoPts[0].Y), true, false);
                for (int i = 1; i < _lassoPts.Count; i++)
                    ctx.LineTo(new Point(_lassoPts[i].X, _lassoPts[i].Y), true, false);
                if (_selDragging)
                    ctx.LineTo(new Point(_selCurrentDoc.X, _selCurrentDoc.Y), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(35, 30, 120, 255)), pen, geo);
        }

        if (_showGradPreview || _gradDragging)
        {
            var p0 = new Point(_gradP0.X, _gradP0.Y);
            var p1 = new Point(_gradP1.X, _gradP1.Y);
            dc.DrawLine(new Pen(Brushes.OrangeRed, inv * 1.5), p0, p1);
            dc.DrawEllipse(Brushes.OrangeRed, null, p0, 3 * inv, 3 * inv);
            dc.DrawEllipse(Brushes.OrangeRed, null, p1, 3 * inv, 3 * inv);
        }

        if (_tool == CanvasTool.FrameRect && _frameDragging)
        {
            double x0 = Math.Min(_selStartDoc.X, _selCurrentDoc.X);
            double y0 = Math.Min(_selStartDoc.Y, _selCurrentDoc.Y);
            double x1 = Math.Max(_selStartDoc.X, _selCurrentDoc.X);
            double y1 = Math.Max(_selStartDoc.Y, _selCurrentDoc.Y);
            dc.DrawRectangle(null, new Pen(Brushes.Black, inv * 1.5), new Rect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0)));
        }
    }

    private static ImageBrush CreateCheckerBrush()
    {
        const int s = 16;
        var wb = new WriteableBitmap(s * 2, s * 2, 96, 96, PixelFormats.Pbgra32, null);
        var pix = new byte[s * 2 * s * 2 * 4];
        for (int y = 0; y < s * 2; y++)
        for (int x = 0; x < s * 2; x++)
        {
            bool light = ((x / s) ^ (y / s)) == 0;
            byte v = light ? (byte)200 : (byte)160;
            int i = (y * s * 2 + x) * 4;
            pix[i] = pix[i + 1] = pix[i + 2] = v;
            pix[i + 3] = 255;
        }
        wb.WritePixels(new Int32Rect(0, 0, s * 2, s * 2), pix, s * 2 * 4, 0);
        return new ImageBrush(wb)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, s * 2, s * 2),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (Document is null) return;
        var pos = e.GetPosition(this);

        if (e.ChangedButton == MouseButton.Middle || _spaceDown)
        {
            _panning = true;
            _lastPanPoint = pos;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            // Spline: right-click finishes placement (optionally snap-join open endpoint)
            if (_tool == CanvasTool.VectorSpline && _splinePlacing)
            {
                FinishSplinePlacement(ScreenToDoc(pos), joinIfEndpoint: true);
                e.Handled = true;
                return;
            }
            // Cancel in-progress spline without commit if only 0-1 points? finish handles that
            DoEyedropper(pos);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            if (Environment.TickCount64 - _lastStylusTicks < 80) { e.Handled = true; return; }
            bool needCapture = HandleToolDown(pos, GetPressure(e));
            // Only capture for continuous gestures — never for one-shot tools
            if (needCapture)
                CaptureMouse();
            // Do not mark Handled on miss: allow bubbling only inside canvas; still stop double-processing
            e.Handled = true;
        }
    }

    private void DoEyedropper(Point pos)
    {
        if (Document is null || _pixels is null) return;
        var sample = ToSample(pos, 1f, PointerPhase.Press);
        int x = (int)sample.DocumentPos.X;
        int y = (int)sample.DocumentPos.Y;
        if ((uint)x >= (uint)Document.Width || (uint)y >= (uint)Document.Height) return;
        int i = y * _stride + x * 4;
        byte bb = _pixels[i], bg = _pixels[i + 1], br = _pixels[i + 2], ba = _pixels[i + 3];
        if (ba > 0 && ba < 255)
        {
            float a = ba / 255f;
            br = (byte)Math.Clamp((int)(br / a + 0.5f), 0, 255);
            bg = (byte)Math.Clamp((int)(bg / a + 0.5f), 0, 255);
            bb = (byte)Math.Clamp((int)(bb / a + 0.5f), 0, 255);
        }
        Document.Colors.Foreground = new ColorRgba8(br, bg, bb, 255);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <returns>true if an interaction started and mouse capture is needed</returns>
    private bool HandleToolDown(Point pos, float pressure)
    {
        if (Document is null) return false;
        var docPt = ScreenToDoc(pos);

        switch (_tool)
        {
            case CanvasTool.Select:
            {
                if (Document.Rulers.Kind == RulerKind.None) return false;
                float hit = 12f / Math.Max(_viewport.Scale, 0.01f);
                var h = Document.Rulers.HitTest(docPt, hit);
                bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
                bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

                // Fisheye6: Shift+circle-handle → 45° discrete drag on the unit circle (not global scale).
                bool fishSnapHandle = shift && !ctrl && !alt
                    && Document.Rulers.Kind == RulerKind.Fisheye6
                    && RulerState.IsFisheyeCircleHandle(h);

                // Fisheye6: Shift+Alt on FishP → rotate P around O with 45° snap, preserving radius.
                bool fishPRotSnap = shift && alt && !ctrl
                    && Document.Rulers.Kind == RulerKind.Fisheye6
                    && h == RulerHandle.FishP;

                if (fishPRotSnap)
                {
                    if (h == RulerHandle.None) return false;
                    _dragHandle = h;
                    Document.Rulers.ActiveHandle = h;
                    _rulerEditMode = RulerEditMode.Handle;
                    _fishPRotSnapActive = true;
                    _fishPRotSnapBaseDeg = MathF.Atan2(
                        Document.Rulers.FisheyeP.Y - Document.Rulers.FishHorizonCenter.Y,
                        Document.Rulers.FisheyeP.X - Document.Rulers.FishHorizonCenter.X) * 180f / MathF.PI;
                }
                else if ((!ctrl && !alt && !shift) || fishSnapHandle)
                {
                    if (h == RulerHandle.None) return false; // miss → no capture
                    _dragHandle = h;
                    Document.Rulers.ActiveHandle = h;
                    _rulerEditMode = RulerEditMode.Handle;
                    _fishPRotSnapActive = false;
                }
                else
                {
                    // modifier transforms: hit handle OR near axis/centroid
                    if (h == RulerHandle.None)
                    {
                        float cd = Dist2(docPt, Document.Rulers.Centroid());
                        if (cd > hit * 6f) return false;
                    }
                    else
                    {
                        _dragHandle = h;
                        Document.Rulers.ActiveHandle = h;
                    }
                    if (ctrl) _rulerEditMode = RulerEditMode.Translate;
                    else if (alt) _rulerEditMode = RulerEditMode.Rotate;
                    else _rulerEditMode = RulerEditMode.Scale;
                    _fishPRotSnapActive = false;
                }

                _rulerDragStartDoc = docPt;
                _rulerDragLastDoc = docPt;
                _fishSnapBaseWorldDeg = null;
                if (fishSnapHandle && RulerState.IsFisheyeCircleHandle(_dragHandle))
                    _fishSnapBaseWorldDeg = Document.Rulers.GetFishThetaWorldDeg(_dragHandle);
                _rulerHandleDragging = true;
                _rulerBeforeSnapshot = Document.Rulers.Clone();
                InvalidateVisual();
                return true;
            }
            case CanvasTool.Fill:
                DoFill(pos);
                return false; // one-shot
            case CanvasTool.RectSelect:
                _selDragging = true;
                _selStartScreen = pos;
                _selStartDoc = docPt;
                _selCurrentDoc = docPt;
                InvalidateVisual();
                return true;
            case CanvasTool.Lasso:
                _selDragging = true;
                _lassoPts.Clear();
                _lassoPts.Add(docPt);
                _selCurrentDoc = docPt;
                InvalidateVisual();
                return true;
            case CanvasTool.MagicWand:
            {
                var layer = Document.ActiveRasterLayer;
                if (layer is null) return false;
                int x = (int)docPt.X, y = (int)docPt.Y;
                Document.Selection.MagicWand(layer.Surface, x, y, 32, contiguous: true, SelectMode);
                InvalidateVisual();
                StatusChanged?.Invoke(this, EventArgs.Empty);
                return false; // one-shot, no capture
            }
            case CanvasTool.Gradient:
                _gradDragging = true;
                _showGradPreview = true;
                _gradP0 = docPt;
                _gradP1 = docPt;
                InvalidateVisual();
                return true;
            case CanvasTool.VectorPen:
                BeginVectorStroke(docPt, pressure, spline: false);
                return true;
            case CanvasTool.VectorSpline:
                // click-to-place control points (no drag capture)
                PlaceSplineControlPoint(docPt, pressure);
                return false;
            case CanvasTool.VectorNode:
                return BeginVectorNodeEdit(docPt);
            case CanvasTool.VectorCloseFill:
                ApplyVectorCloseFill(docPt);
                return false; // one-shot — never capture (was blocking UI)
            case CanvasTool.VectorEraser:
                return BeginVectorErase(docPt);
            case CanvasTool.FrameRect:
                _frameDragging = true;
                _selStartDoc = docPt;
                _selCurrentDoc = docPt;
                InvalidateVisual();
                Eidolon.App.Logging.AppLog.Debug($"Frame drag begin {docPt.X:F0},{docPt.Y:F0}", "Frame");
                return true;
            default:
            {
                float pp = pressure;
                Eidolon.App.Logging.AppLog.Debug($"ToolDown brush p={pp:F3} src={_hudPressureSrc}", "Pen");
                BeginStroke(pos, pp);
                return true;
            }
        }
    }

    private Float2 ScreenToDoc(Point screen)
    {
        if (Document is null) return new Float2(0, 0);
        return _viewport.ScreenToDocument(
            new Float2((float)screen.X, (float)screen.Y),
            (float)ActualWidth, (float)ActualHeight, Document.Width, Document.Height);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Document is null) return;
        var pos = e.GetPosition(this);

        // Ruler hover preview only when pointer is over canvas content and preview kinds active
        if (!_rulerHandleDragging && e.LeftButton != MouseButtonState.Pressed
            && IsMouseOver && Document.Rulers.WantsHoverPreview)
        {
            Document.Rulers.HoverDoc = ScreenToDoc(pos);
            InvalidateVisual();
        }

        if (_panning && (e.MiddleButton == MouseButtonState.Pressed || (_spaceDown && e.LeftButton == MouseButtonState.Pressed)))
        {
            var dx = pos.X - _lastPanPoint.X;
            var dy = pos.Y - _lastPanPoint.Y;
            _viewport.Pan = new Float2(_viewport.Pan.X + (float)dx, _viewport.Pan.Y + (float)dy);
            _lastPanPoint = pos;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (_rulerHandleDragging)
            {
                var cur = ScreenToDoc(pos);
                var r = Document.Rulers;
                switch (_rulerEditMode)
                {
                    case RulerEditMode.Translate:
                        r.Translate(cur - _rulerDragLastDoc);
                        break;
                    case RulerEditMode.Rotate:
                    {
                        var piv = r.Centroid();
                        float a0 = MathF.Atan2(_rulerDragLastDoc.Y - piv.Y, _rulerDragLastDoc.X - piv.X);
                        float a1 = MathF.Atan2(cur.Y - piv.Y, cur.X - piv.X);
                        r.Rotate(piv, (a1 - a0) * 180f / MathF.PI);
                        break;
                    }
                    case RulerEditMode.Scale:
                    {
                        var piv = r.Centroid();
                        float d0 = Dist2(piv, _rulerDragLastDoc);
                        float d1 = Dist2(piv, cur);
                        if (d0 > 1e-3f) r.ScaleUniform(piv, d1 / d0);
                        break;
                    }
                    default:
                        if (_dragHandle != RulerHandle.None)
                        {
                            if (_fishPRotSnapActive && _dragHandle == RulerHandle.FishP)
                            {
                                // Shift+Alt on FishP: rotate around O with 45° snap, constant radius
                                var o = r.FishHorizonCenter;
                                float drx = r.FisheyeP.X - o.X, dry = r.FisheyeP.Y - o.Y;
                                float dist = MathF.Sqrt(drx * drx + dry * dry);
                                float curDeg = MathF.Atan2(cur.Y - o.Y, cur.X - o.X) * 180f / MathF.PI;
                                float delta = RulerState.SignedDeltaDeg(curDeg - _fishPRotSnapBaseDeg);
                                float stepped = MathF.Round(delta / 45f) * 45f;
                                float newDeg = _fishPRotSnapBaseDeg + stepped;
                                float rad = newDeg * MathF.PI / 180f;
                                r.FisheyeP = new Float2(
                                    o.X + dist * MathF.Cos(rad),
                                    o.Y + dist * MathF.Sin(rad));
                            }
                            else
                            {
                                bool snap45 = r.Kind == RulerKind.Fisheye6
                                    && RulerState.IsFisheyeCircleHandle(_dragHandle)
                                    && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                                if (snap45 && _fishSnapBaseWorldDeg is null)
                                    _fishSnapBaseWorldDeg = r.GetFishThetaWorldDeg(_dragHandle);
                                if (!snap45)
                                    _fishSnapBaseWorldDeg = null;
                                r.SetHandle(_dragHandle, cur, snap45, _fishSnapBaseWorldDeg);
                            }
                        }
                        break;
                }
                _rulerDragLastDoc = cur;
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (_selDragging && (_tool == CanvasTool.RectSelect || _tool == CanvasTool.Lasso))
            {
                _selCurrentDoc = ScreenToDoc(pos);
                if (_tool == CanvasTool.Lasso)
                    _lassoPts.Add(_selCurrentDoc);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (_gradDragging && _tool == CanvasTool.Gradient)
            {
                _gradP1 = ScreenToDoc(pos);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (_nodeDragging && _tool == CanvasTool.VectorNode)
            {
                ContinueVectorNodeDrag(ScreenToDoc(pos));
                e.Handled = true;
                return;
            }
            if (_vectorErasing && _tool == CanvasTool.VectorEraser)
            {
                ContinueVectorErase(ScreenToDoc(pos));
                e.Handled = true;
                return;
            }
            if (_vectorDrawing && _tool == CanvasTool.VectorPen)
            {
                ContinueVectorStroke(ScreenToDoc(pos), GetPressure(e));
                e.Handled = true;
                return;
            }
            if (_frameDragging && _tool == CanvasTool.FrameRect)
            {
                _selCurrentDoc = ScreenToDoc(pos);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (!_stylusDrawing && _stroke is { IsActive: true })
            {
                ContinueStroke(pos, GetPressure(e));
                e.Handled = true;
            }
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_panning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
        {
            _panning = false;
            if (e.ChangedButton == MouseButton.Middle || _spaceDown)
                ReleaseMouseCapture();
            e.Handled = true;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            if (_rulerHandleDragging)
            {
                EndRulerDrag();
                e.Handled = true;
                return;
            }
            // Frame tool is independent of selection drag
            if (_frameDragging)
            {
                FinishFrameRect();
                _frameDragging = false;
                ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            if (_selDragging && (_tool == CanvasTool.RectSelect || _tool == CanvasTool.Lasso))
            {
                FinishSelectionDrag();
                _selDragging = false;
                ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            // safety clear
            if (_selDragging)
            {
                _selDragging = false;
                _lassoPts.Clear();
                ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            if (_gradDragging)
            {
                FinishGradient();
                _gradDragging = false;
                ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            if (_nodeDragging)
            {
                EndVectorNodeDrag();
                if (IsMouseCaptured) ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            if (_vectorErasing)
            {
                EndVectorErase();
                if (IsMouseCaptured) ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            if (_vectorDrawing)
            {
                EndVectorStroke();
                if (IsMouseCaptured) ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            if (_stroke is { IsActive: true })
            {
                EndStroke();
                if (IsMouseCaptured) ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            // Safety: never leave capture stuck after one-shot tools
            if (IsMouseCaptured && !_panning && !_rulerHandleDragging && !_selDragging
                && !_gradDragging && !_frameDragging && !_splinePlacing)
            {
                ReleaseMouseCapture();
            }
        }
    }

    private void FinishSelectionDrag()
    {
        if (Document is null) return;
        if (_tool == CanvasTool.RectSelect)
        {
            int x0 = (int)MathF.Floor(Math.Min(_selStartDoc.X, _selCurrentDoc.X));
            int y0 = (int)MathF.Floor(Math.Min(_selStartDoc.Y, _selCurrentDoc.Y));
            int x1 = (int)MathF.Ceiling(Math.Max(_selStartDoc.X, _selCurrentDoc.X));
            int y1 = (int)MathF.Ceiling(Math.Max(_selStartDoc.Y, _selCurrentDoc.Y));
            var rect = IntRect.FromMinMax(x0, y0, x1 - 1, y1 - 1);
            if (rect.Width > 0 && rect.Height > 0)
                Document.Selection.ApplyRect(rect, SelectMode);
        }
        else if (_tool == CanvasTool.Lasso && _lassoPts.Count >= 3)
        {
            var pts = _lassoPts.Select(p => ((int)MathF.Round(p.X), (int)MathF.Round(p.Y))).ToList();
            Document.Selection.ApplyLasso(pts, SelectMode);
        }
        _lassoPts.Clear();
        InvalidateVisual();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FinishGradient()
    {
        if (Document?.ActiveRasterLayer is not { } layer) return;
        var c0 = Document.Colors.Foreground;
        var c1 = GradientToTransparent
            ? new ColorRgba8(Document.Colors.Background.R, Document.Colors.Background.G, Document.Colors.Background.B, 0)
            : Document.Colors.Background;

        var keysBefore = layer.Surface.Tiles.Keys.ToList();
        var before = layer.Surface.SnapshotTiles(keysBefore);
        var dirty = GradientFill.Apply(
            layer, _gradP0, _gradP1, c0, c1, GradientKind,
            Document.Selection.IsEmpty ? null : Document.Selection,
            (layer.Locks & LayerLocks.Transparency) != 0 || _preset.Params.LockAlpha);
        if (dirty.IsEmpty) { _showGradPreview = false; InvalidateVisual(); return; }

        var keysAfter = layer.Surface.Tiles.Keys.ToList();
        var allKeys = keysBefore.Union(keysAfter).Distinct().ToList();
        foreach (var k in allKeys)
            if (!before.ContainsKey(k)) before[k] = new Tile(layer.Surface.TileSize);
        var after = layer.Surface.SnapshotTiles(allKeys);
        Document.History.PushAlreadyDone(new TileEditCommand(layer.Id, before, after, "Gradient"), Document);
        _showGradPreview = false;
        RedrawDirty(dirty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Document is null) return;
        var pos = e.GetPosition(this);
        float factor = e.Delta > 0 ? 1.1f : 1f / 1.1f;
        _viewport.ZoomAt(factor, new Float2((float)pos.X, (float)pos.Y),
            (float)ActualWidth, (float)ActualHeight, Document.Width, Document.Height);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Chinese IME / text fields: do not steal bare keys
        if (ImeInput.ShouldIgnoreHotkey(e))
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.Key == Key.Space) { _spaceDown = true; e.Handled = true; }
        if (e.Key == Key.X && Document != null
            && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
        {
            Document.Colors.Swap();
            StatusChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        if (e.Key == Key.E
            && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
        {
            _preset.Params.EraseMode = !_preset.Params.EraseMode;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        // Pressure force test: 1/2/3/0 — skip if IME might need digits (only when canvas focused)
        if (e.Key == Key.D1 || e.Key == Key.NumPad1) { _forcePressure = 0.15f; _hudPressure = 0.15f; _hudPressureSrc = "KeyForce"; InvalidateVisual(); e.Handled = true; }
        if (e.Key == Key.D2 || e.Key == Key.NumPad2) { _forcePressure = 0.50f; _hudPressure = 0.50f; _hudPressureSrc = "KeyForce"; InvalidateVisual(); e.Handled = true; }
        if (e.Key == Key.D3 || e.Key == Key.NumPad3) { _forcePressure = 1.00f; _hudPressure = 1.00f; _hudPressureSrc = "KeyForce"; InvalidateVisual(); e.Handled = true; }
        if (e.Key == Key.D0 || e.Key == Key.NumPad0) { _forcePressure = null; _hudPressureSrc = "Device"; InvalidateVisual(); e.Handled = true; }
        if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control) { ClearSelection(); e.Handled = true; }
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { SelectAll(); e.Handled = true; }
        if (e.Key == Key.I && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { InvertSelection(); e.Handled = true; }
        if (e.Key == Key.Escape)
        {
            if (_splinePlacing) CancelSplinePlacement();
            ClearSelection();
            _showGradPreview = false;
            _selDragging = false;
            _frameDragging = false;
            _lassoPts.Clear();
            InvalidateVisual();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (ImeInput.ShouldIgnoreHotkey(e) && e.Key != Key.Space)
        {
            base.OnKeyUp(e);
            return;
        }
        if (e.Key == Key.Space) { _spaceDown = false; e.Handled = true; }
        base.OnKeyUp(e);
    }

    private float _lastStylusPressure = 1f;
    private float _hudPressure = 1f;
    private string _hudPressureSrc = "?";
    /// <summary>When set (0..1), overrides device pressure. Toggle with keys 1/2/3/0.</summary>
    private float? _forcePressure;
    private int _pressureLogCounter;

    /// <summary>
    /// Priority: WM_POINTER pen > WinTab > WPF Stylus PressureFactor > mouse=1.
    /// </summary>
    private float ResolvePressure(StylusPointCollection? stylusPts, MouseEventArgs? mouse)
    {
        if (_forcePressure is float forced)
        {
            _hudPressure = forced;
            _hudPressureSrc = "KeyForce";
            return forced;
        }

        try { _wintab.Poll(); } catch { /* ignore */ }

        float best = -1f;
        string src = "none";

        // 1) WM_POINTER pen
        float ptr = _pointerPen.GetPressureOrDefault(-1f);
        if (ptr > 0f) { best = ptr; src = "Pointer"; }

        // 2) WinTab
        float wt = _wintab.GetPressureOrDefault(-1f);
        if (wt > 0f && wt > best) { best = wt; src = "WinTab"; }

        // 3) WPF Stylus raw NormalPressure if available
        float sty = ReadStylusPressure(stylusPts, mouse);
        if (sty > 0f && (best < 0f || Math.Abs(sty - 0.5f) > 0.02f || src == "none"))
        {
            // Prefer stylus if pointer/wintab missing; if both exist keep higher of recent
            if (best < 0f) { best = sty; src = "Stylus"; }
            else if (src != "Pointer" && src != "WinTab") { best = sty; src = "Stylus"; }
        }

        if (best < 0f)
        {
            best = 1f;
            src = mouse != null ? "Mouse" : "Default";
        }

        best = Math.Clamp(best, 0.01f, 1f);
        _lastStylusPressure = best;
        _hudPressure = best;
        _hudPressureSrc = src;
        LogPressure(src, best);
        return best;
    }

    private float ReadStylusPressure(StylusPointCollection? stylusPts, MouseEventArgs? mouse)
    {
        StylusPointCollection? pts = stylusPts;
        if ((pts == null || pts.Count == 0) && mouse?.StylusDevice != null)
        {
            try { pts = mouse.StylusDevice.GetStylusPoints(this); } catch { /* ignore */ }
        }
        if ((pts == null || pts.Count == 0) && Stylus.CurrentStylusDevice != null)
        {
            try { pts = Stylus.CurrentStylusDevice.GetStylusPoints(this); } catch { /* ignore */ }
        }
        if (pts == null || pts.Count == 0) return -1f;

        var sp = pts[^1];
        try
        {
            if (sp.HasProperty(StylusPointProperties.NormalPressure))
            {
                int raw = sp.GetPropertyValue(StylusPointProperties.NormalPressure);
                // WPF often already scales PressureFactor; raw may be 0..max
                // Use PressureFactor first if non-default
                float pf0 = (float)sp.PressureFactor;
                if (!float.IsNaN(pf0) && pf0 > 0f && pf0 < 1f)
                    return Math.Clamp(pf0, 0.01f, 1f);
                if (raw > 1024) return Math.Clamp(raw / 65535f, 0.01f, 1f);
                if (raw > 1) return Math.Clamp(raw / 1023f, 0.01f, 1f);
                if (raw == 1) return 1f;
            }
        }
        catch { /* fall back */ }

        float pf = (float)sp.PressureFactor;
        if (float.IsNaN(pf) || pf <= 0f) return -1f;
        return Math.Clamp(pf, 0.01f, 1f);
    }

    private void LogPressure(string src, float p)
    {
        _pressureLogCounter++;
        if (_pressureLogCounter <= 8 || _pressureLogCounter % 20 == 0)
            Eidolon.App.Logging.AppLog.Debug($"pressure src={src} p={p:F3} ptrN={_pointerPen.SampleCount} wtN={_wintab.PacketCount} rawPtr={_pointerPen.LastRawPressure}", "Pen");
    }

    private float GetPressure(MouseEventArgs e) => ResolvePressure(null, e);



    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        Focus();
        if (Document is null) { base.OnStylusDown(e); return; }
        if (_spaceDown) { base.OnStylusDown(e); return; }
        var pos = e.GetPosition(this);
        float pressure = ResolvePressure(e.GetStylusPoints(this), null);
        _lastStylusTicks = Environment.TickCount64;
        if (_tool == CanvasTool.Brush)
        {
            _stylusDrawing = true;
            BeginStroke(pos, pressure);
            CaptureStylus();
        }
        else
        {
            HandleToolDown(pos, pressure);
            CaptureMouse();
        }
        e.Handled = true;
    }

    protected override void OnStylusMove(StylusEventArgs e)
    {
        if (_stroke is { IsActive: true })
        {
            var pos = e.GetPosition(this);
            float pressure = ResolvePressure(e.GetStylusPoints(this), null);
            _lastStylusTicks = Environment.TickCount64;
            ContinueStroke(pos, pressure);
            e.Handled = true;
            return;
        }
        base.OnStylusMove(e);
    }

    protected override void OnStylusUp(StylusEventArgs e)
    {
        if (_stroke is { IsActive: true })
        {
            _stylusDrawing = false;
            _lastStylusTicks = Environment.TickCount64;
            EndStroke();
            ReleaseStylusCapture();
            e.Handled = true;
            return;
        }
        base.OnStylusUp(e);
    }



    private PointerSample ToSample(Point screen, float pressure, PointerPhase phase)
    {
        var doc = Document!;
        var d = _viewport.ScreenToDocument(
            new Float2((float)screen.X, (float)screen.Y),
            (float)ActualWidth, (float)ActualHeight, doc.Width, doc.Height);

        if (_straightLine && _straightOrigin is Float2 o)
        {
            float dx = MathF.Abs(d.X - o.X);
            float dy = MathF.Abs(d.Y - o.Y);
            if (dx >= dy) d = new Float2(d.X, o.Y);
            else d = new Float2(o.X, d.Y);
        }

        double time = Environment.TickCount64 / 1000.0;
        // Choose a track from several raw velocity samples before applying snap.
        if (phase is PointerPhase.Move or PointerPhase.Press)
            doc.Rulers.ObserveStrokePoint(d, time);
        d = doc.Rulers.Snap(d);

        return new PointerSample(time, d, pressure, phase);
    }

    private static float Dist2(Float2 a, Float2 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static bool RulerStateEquals(RulerState a, RulerState b)
    {
        return a.Kind == b.Kind && a.Visible == b.Visible
            && a.SnapEnabled == b.SnapEnabled && a.ForceSnap == b.ForceSnap
            && Math.Abs(a.SnapStrength - b.SnapStrength) < 0.001f
            && a.Origin == b.Origin && Math.Abs(a.AngleDeg - b.AngleDeg) < 0.001f
            && a.EllA == b.EllA && a.EllB == b.EllB && a.EllC == b.EllC && a.EllD == b.EllD
            && a.SymmetryOrigin == b.SymmetryOrigin && Math.Abs(a.SymmetryAngleDeg - b.SymmetryAngleDeg) < 0.001f
            && a.Vp == b.Vp
            && a.HorizonOrigin == b.HorizonOrigin && Math.Abs(a.HorizonAngleDeg - b.HorizonAngleDeg) < 0.001f
            && a.Vp0 == b.Vp0 && a.Vp1 == b.Vp1 && a.Vp2 == b.Vp2
            && a.FishR0 == b.FishR0 && a.FishR1 == b.FishR1
            && a.FishG0 == b.FishG0 && a.FishG1 == b.FishG1
            && a.FishB0 == b.FishB0 && a.FishB1 == b.FishB1
            && a.FishHorizonCenter == b.FishHorizonCenter
            && Math.Abs(a.FishHorizonRadius - b.FishHorizonRadius) < 0.01f
            && Math.Abs(a.FishTheta1Deg - b.FishTheta1Deg) < 0.001f
            && Math.Abs(a.FishTheta2Deg - b.FishTheta2Deg) < 0.001f
            && Math.Abs(a.FishTheta3Deg - b.FishTheta3Deg) < 0.001f
            && Math.Abs(a.FishGlobalAngleDeg - b.FishGlobalAngleDeg) < 0.001f
            && a.FisheyeP == b.FisheyeP
            && a.FisheyePMode == b.FisheyePMode
            && a.PerspectiveLine0Enabled == b.PerspectiveLine0Enabled
            && a.PerspectiveLine1Enabled == b.PerspectiveLine1Enabled
            && a.PerspectiveLine2Enabled == b.PerspectiveLine2Enabled;
    }

    private void EndRulerDrag()
    {
        _rulerHandleDragging = false; _fishSnapBaseWorldDeg = null;
        _fishPRotSnapActive = false;

        // Push undo if state actually changed
        if (_rulerBeforeSnapshot is not null && Document is not null)
        {
            var after = Document.Rulers.Clone();
            if (!RulerStateEquals(_rulerBeforeSnapshot, after))
                Document.History.PushAlreadyDone(new RulerEditCommand(_rulerBeforeSnapshot, after), Document);
            _rulerBeforeSnapshot = null;
        }

        _dragHandle = RulerHandle.None;
        _rulerEditMode = RulerEditMode.Handle;
        if (Document is not null) Document.Rulers.ActiveHandle = RulerHandle.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (Document is null) return;
        if (!_rulerHandleDragging)
        {
            Document.Rulers.HoverDoc = null;
            InvalidateVisual();
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        AbortInteractionForFocusChange();
    }

    protected override void OnLostStylusCapture(StylusEventArgs e)
    {
        base.OnLostStylusCapture(e);
        AbortInteractionForFocusChange();
    }

    /// <summary>
    /// Host window lost activation: finish/cancel in-progress gestures and pause tablet context.
    /// Prevents stuck capture/stroke state that freezes input after Alt-Tab.
    /// </summary>
    public void NotifyHostDeactivated()
    {
        AbortInteractionForFocusChange();
        try { _wintab.SetEnabled(false); } catch { /* ignore */ }
    }

    /// <summary>Host window reactivated: re-enable tablet context and recover focus.</summary>
    public void NotifyHostActivated()
    {
        try { _wintab.SetEnabled(true); } catch { /* ignore */ }
        try
        {
            if (IsVisible && IsLoaded)
            {
                Focus();
                InvalidateVisual();
            }
        }
        catch { /* ignore */ }
    }

    private void AbortInteractionForFocusChange()
    {
        try
        {
            if (_stroke is { IsActive: true })
                EndStroke();
            else
                _stroke = null;

            if (_vectorDrawing)
                EndVectorStroke();

            if (_splinePlacing)
                CancelSplinePlacement();

            if (_nodeDragging)
                EndVectorNodeDrag();

            if (_vectorErasing)
                EndVectorErase();

            if (_gradDragging)
            {
                // Drop incomplete gradient instead of committing a partial drag after focus loss.
                _gradDragging = false;
                _showGradPreview = false;
            }

            if (_frameDragging)
            {
                _frameDragging = false;
                _lassoPts.Clear();
            }

            if (_selDragging)
            {
                _selDragging = false;
                _lassoPts.Clear();
            }

            if (_rulerHandleDragging)
                EndRulerDrag();

            _panning = false;
            _stylusDrawing = false;
            _mirrorStroke = null;
            _straightOrigin = null;

            if (IsMouseCaptured)
                ReleaseMouseCapture();
            if (IsStylusCaptured)
                ReleaseStylusCapture();
        }
        catch
        {
            // Ensure flags clear even if one end-handler throws.
            _stroke = null;
            _vectorDrawing = false;
            _vectorStroke = null;
            _vectorStrokeBefore = null;
            _splinePlacing = false;
            _nodeDragging = false;
            _vectorErasing = false;
            _gradDragging = false;
            _frameDragging = false;
            _selDragging = false;
            _rulerHandleDragging = false; _fishSnapBaseWorldDeg = null;
            _fishPRotSnapActive = false;
            _rulerBeforeSnapshot = null;
            _panning = false;
            _stylusDrawing = false;
            _mirrorStroke = null;
            try { if (IsMouseCaptured) ReleaseMouseCapture(); } catch { /* ignore */ }
            try { if (IsStylusCaptured) ReleaseStylusCapture(); } catch { /* ignore */ }
        }

        InvalidateVisual();
    }

    private void DrawRulers(DrawingContext dc)
    {
        if (Document is null) return;
        var r = Document.Rulers;
        if (!r.Visible || r.Kind == RulerKind.None) return;

        double inv = 1.0 / Math.Max(_viewport.Scale, 0.01);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 40, 90, 200)), 1.8 * inv);
        var penAccent = new Pen(new SolidColorBrush(Color.FromArgb(220, 200, 60, 40)), 2.1 * inv);
        var penSoft = new Pen(new SolidColorBrush(Color.FromArgb(100, 40, 90, 200)), 1.4 * inv);
        // RGB for fisheye / multi-VP channels
        Pen[] chPen =
        {
            new(new SolidColorBrush(Color.FromArgb(200, 220, 50, 50)), 2.0 * inv),
            new(new SolidColorBrush(Color.FromArgb(200, 40, 180, 70)), 2.0 * inv),
            new(new SolidColorBrush(Color.FromArgb(200, 50, 100, 230)), 2.0 * inv)
        };
        Pen[] chSoft =
        {
            new(new SolidColorBrush(Color.FromArgb(140, 220, 50, 50)), 1.5 * inv),
            new(new SolidColorBrush(Color.FromArgb(140, 40, 180, 70)), 1.5 * inv),
            new(new SolidColorBrush(Color.FromArgb(140, 50, 100, 230)), 1.5 * inv)
        };
        // Dark gray for fish horizon circle + H/V lines
        var penFishGrid = new Pen(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), 1.4 * inv);
        // Cyan for P / P' / P‑P'‑pointer circle preview
        var penPCyan = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)), 1.8 * inv);
        var penPSoft = new Pen(new SolidColorBrush(Color.FromArgb(120, 0x00, 0xBC, 0xD4)), 1.5 * inv);
        var brushPCyan = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4));
        double handle = (_tool == CanvasTool.Select ? 3.5 : 2.5) * inv;
        var handleFill = System.Windows.Media.Brushes.White;
        var handleActive = System.Windows.Media.Brushes.OrangeRed;
        var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)), 1.1 * inv);

        void Dot(Float2 p, bool active = false, System.Windows.Media.Brush? fill = null)
        {
            double s = active ? handle * 1.35 : handle;
            dc.DrawEllipse(active ? handleActive : (fill ?? handleFill), handlePen, new Point(p.X, p.Y), s, s);
        }

        void LineInf(Float2 a, Float2 b, Pen? use = null)
        {
            var d = b - a;
            float len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len < 1e-4f) return;
            float ux = d.X / len, uy = d.Y / len;
            // Use a large extension so guides remain visible at any zoom/pan
            float ext = Math.Max(Document.Width, Document.Height) * 20f;
            dc.DrawLine(use ?? pen,
                new Point(a.X - ux * ext, a.Y - uy * ext),
                new Point(a.X + ux * ext, a.Y + uy * ext));
        }

        void LineThrough(Float2 point, Float2 dir, Pen use)
        {
            float len = MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            if (len < 1e-6f) return;
            float ux = dir.X / len, uy = dir.Y / len;
            float ext = Math.Max(Document.Width, Document.Height) * 20f;
            dc.DrawLine(use,
                new Point(point.X - ux * ext, point.Y - uy * ext),
                new Point(point.X + ux * ext, point.Y + uy * ext));
        }

        void LineSeg(Float2 a, Float2 b, Pen? use = null) =>
            dc.DrawLine(use ?? pen, new Point(a.X, a.Y), new Point(b.X, b.Y));

        void Circle(DocCircle c, Pen use)
        {
            if (!c.IsValid) return;
            // Never DrawEllipse with huge radius. Draw each visible arc segment separately
            // so gaps are not bridged by a straight bowstring chord.
            float pad = Math.Max(Document.Width, Document.Height) * 0.05f;
            var segs = c.SampleVisibleSegments(-pad, -pad, Document.Width + pad, Document.Height + pad, maxSegLen: 5f);
            foreach (var poly in segs)
            {
                if (poly.Count < 2) continue;
                if (c.IsLine || poly.Count == 2)
                {
                    dc.DrawLine(use, new Point(poly[0].X, poly[0].Y), new Point(poly[^1].X, poly[^1].Y));
                    continue;
                }
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(poly[0].X, poly[0].Y), false, false);
                    for (int i = 1; i < poly.Count; i++)
                        ctx.LineTo(new Point(poly[i].X, poly[i].Y), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, use, geo);
            }
        }

        switch (r.Kind)
        {
            case RulerKind.Straight:
            {
                float rad = r.AngleDeg * MathF.PI / 180f;
                var dir = new Float2(MathF.Cos(rad), MathF.Sin(rad));
                LineInf(r.Origin, r.Origin + dir * 100f);
                break;
            }

            case RulerKind.Ellipse:
            {
                LineSeg(r.EllA, r.EllB, penSoft);
                LineSeg(r.EllB, r.EllC, penSoft);
                LineSeg(r.EllC, r.EllD, penSoft);
                LineSeg(r.EllD, r.EllA, penSoft);
                Float2? prev = null;
                foreach (var pt in r.SampleEllipse(72))
                {
                    if (prev is Float2 p0) LineSeg(p0, pt);
                    prev = pt;
                }
                break;
            }

            case RulerKind.Symmetry:
                LineInf(r.SymmetryOrigin,
                    r.SymmetryOrigin + new Float2(MathF.Cos(r.SymmetryAngleDeg * MathF.PI / 180f), MathF.Sin(r.SymmetryAngleDeg * MathF.PI / 180f)),
                    penAccent);
                break;

            case RulerKind.VanishingPoint:
            case RulerKind.Perspective1:
                // VP handle + hover previews (H/V for P1)
                break;

            case RulerKind.Perspective2:
            case RulerKind.Perspective3:
                // horizon line always; VPs on it
                LineInf(r.HorizonOrigin, r.HorizonOrigin + r.HorizonDir(), pen);
                break;

                        case RulerKind.Fisheye6:
                // Horizon circle + H/V grid in dark gray
                Circle(new DocCircle(r.FishHorizonCenter, r.FishHorizonRadius), penFishGrid);
                LineInf(r.FishHorizonCenter, r.FishHorizonCenter + new Float2(1, 0), penFishGrid);
                LineInf(r.FishHorizonCenter, r.FishHorizonCenter + new Float2(0, 1), penFishGrid);
                // P reference point and its inverse
                if (r.FisheyePMode != FisheyePMode.Off)
                {
                    // P handle (cyan filled dot)
                    Dot(r.FisheyeP, active: false, fill: brushPCyan);
                    // P' inverse point (smaller, less opaque)
                    if (r.FisheyePInverse() is Float2 pi)
                    {
                        double ps = handle * 0.7;
                        dc.DrawEllipse(brushPCyan, penPCyan, new Point(pi.X, pi.Y), ps, ps);
                    }
                }
                // Stereographic: 6 vanishing points, 3 vanishing-line circles
                if (r.TryComputeFisheyeGeo(out var geo))
                {
                    // vanishing-line circles
                    for (int a = 0; a < 3; a++)
                    {
                        var lc = new DocCircle(geo.VanishingLineCircleCenter(a), geo.VanishingLineCircleRadius(a));
                        Circle(lc, chSoft[a]);
                    }
                    // 6 vanishing points (paired)
                    for (int a = 0; a < 3; a++)
                    {
                        Dot(geo.VanishingPoint(a, true), active: false, fill: _vpFill[a]);
                        Dot(geo.VanishingPoint(a, false), active: false, fill: _vpFill[a]);
                    }
                }
                break;
        }

        // Hover / stroke previews (draw through tip with explicit direction so H/V/perp always show)
        Float2? tip = r.StrokeAnchor ?? r.HoverDoc;
        if (r.WantsHoverPreview && tip is Float2 tipPt)
        {
            switch (r.Kind)
            {
                case RulerKind.VanishingPoint:
                    LineThrough(tipPt, r.Vp - tipPt, chSoft[0]);
                    break;
                case RulerKind.Perspective1:
                    LineThrough(tipPt, r.Vp - tipPt, chSoft[0]);
                    LineThrough(tipPt, new Float2(1, 0), chSoft[1]); // horizontal
                    LineThrough(tipPt, new Float2(0, 1), chSoft[2]); // vertical
                    break;
                case RulerKind.Perspective2:
                    LineThrough(tipPt, r.Vp0 - tipPt, chSoft[0]);
                    LineThrough(tipPt, r.Vp1 - tipPt, chSoft[1]);
                    LineThrough(tipPt, r.HorizonNormal(), chSoft[2]); // ⊥ horizon
                    break;
                case RulerKind.Perspective3:
                    LineThrough(tipPt, r.Vp0 - tipPt, chSoft[0]);
                    LineThrough(tipPt, r.Vp1 - tipPt, chSoft[1]);
                    LineThrough(tipPt, r.Vp2 - tipPt, chSoft[2]);
                    break;
            }
        }
        foreach (var (circ, ch) in r.PreviewFisheyeCircles())
        {
            int i = Math.Clamp(ch, (byte)0, (byte)2);
            Circle(circ, chSoft[i]);
        }
        // P‑P'‑pointer circle preview (when P mode is VisualOnly or Snappable)
        if (r.FisheyePMode != FisheyePMode.Off && r.Kind == RulerKind.Fisheye6 && tip is Float2 tipP)
        {
            if (r.FisheyePInverse() is Float2 pi)
            {
                if (DocCircle.From3Points(r.FisheyeP, pi, tipP) is DocCircle c)
                    Circle(c, penPSoft);
            }
        }

        // Handles (color-coded for fisheye)
        foreach (var (h, p) in r.EnumerateHandles())
        {
            System.Windows.Media.Brush? fill = h switch
            {
                RulerHandle.FishR0 or RulerHandle.FishR1 => new SolidColorBrush(Color.FromRgb(220, 60, 60)),
                RulerHandle.FishG0 or RulerHandle.FishG1 => new SolidColorBrush(Color.FromRgb(40, 170, 70)),
                RulerHandle.FishB0 or RulerHandle.FishB1 => new SolidColorBrush(Color.FromRgb(50, 100, 220)),
                RulerHandle.Vp0 => new SolidColorBrush(Color.FromRgb(220, 60, 60)),
                RulerHandle.Vp1 => new SolidColorBrush(Color.FromRgb(40, 170, 70)),
                RulerHandle.Vp2 => new SolidColorBrush(Color.FromRgb(50, 100, 220)),
                _ => null
            };
            Dot(p, active: h == r.ActiveHandle || h == _dragHandle, fill: fill);
        }
    }



    private void BeginVectorStroke(Float2 docPt, float pressure, bool spline)
    {
        if (Document is null) return;
        if (Document.ActiveLayer is not VectorLayer vl)
        {
            vl = Document.AddVectorLayer();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
        _vectorStrokeBefore = vl.CloneStrokes();
        float w = VectorBaseWidth * (0.2f + 0.8f * Math.Clamp(pressure, 0.01f, 1f));
        _vectorStroke = new VectorStroke
        {
            Color = Document.Colors.Foreground,
            BaseWidth = VectorBaseWidth,
            PathMode = spline ? VectorPathMode.Spline : VectorPathMode.Polyline,
            FillColor = Document.Colors.Foreground
        };
        _vectorStroke.Points.Add(new StrokePoint(docPt.X, docPt.Y, pressure, w));
        vl.Strokes.Add(_vectorStroke);
        RebuildVectorCacheExceptCurrent(vl);
        StampLive(vl, docPt.X, docPt.Y, w, _vectorStroke.Color);
        _vectorDrawing = true;
        Document.IsDirty = true;
        FullRedraw();
        Eidolon.App.Logging.AppLog.Debug("Vector polyline begin", "Draw");
    }

    private void ContinueVectorStroke(Float2 docPt, float pressure)
    {
        if (!_vectorDrawing || _vectorStroke is null || Document is null) return;
        if (Document.ActiveLayer is not VectorLayer vl) return;
        float w = VectorBaseWidth * (0.2f + 0.8f * Math.Clamp(pressure, 0.01f, 1f));
        var last = _vectorStroke.Points[^1];
        float dx = docPt.X - last.X, dy = docPt.Y - last.Y;
        if (dx * dx + dy * dy < 0.64f) return;
        _vectorStroke.Points.Add(new StrokePoint(docPt.X, docPt.Y, pressure, w));

        if (vl.RasterCache is null) RebuildVectorCacheExceptCurrent(vl);
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max(1, (int)MathF.Ceiling(dist / Math.Max(0.5f, w * 0.4f)));
        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            float x = last.X + dx * t;
            float y = last.Y + dy * t;
            float ww = last.Width + (w - last.Width) * t;
            StampLive(vl, x, y, ww, _vectorStroke.Color);
        }
        float maxR = Math.Max(last.Width, w) * 0.5f + 2;
        int x0 = (int)MathF.Floor(Math.Min(last.X, docPt.X) - maxR);
        int y0 = (int)MathF.Floor(Math.Min(last.Y, docPt.Y) - maxR);
        int x1 = (int)MathF.Ceiling(Math.Max(last.X, docPt.X) + maxR);
        int y1 = (int)MathF.Ceiling(Math.Max(last.Y, docPt.Y) + maxR);
        RedrawDirty(IntRect.FromMinMax(x0, y0, x1, y1));
    }

    private void EndVectorStroke()
    {
        if (Document?.ActiveLayer is VectorLayer vl && _vectorStroke is not null && _vectorStrokeBefore is not null)
        {
            vl.InvalidateCache();
            var after = vl.CloneStrokes();
            Document.History.PushAlreadyDone(
                new VectorLayerEditCommand(vl.Id, _vectorStrokeBefore, after, "VectorStroke"), Document);
        }
        _vectorDrawing = false;
        _vectorStroke = null;
        _vectorStrokeBefore = null;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        FullRedraw();
        Eidolon.App.Logging.AppLog.Debug("Vector stroke end", "Draw");
    }

    // ---- Spline: click to place control points; right-click finishes ----

    private void PlaceSplineControlPoint(Float2 docPt, float pressure)
    {
        if (Document is null) return;
        if (Document.ActiveLayer is not VectorLayer vl)
        {
            vl = Document.AddVectorLayer();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        float w = VectorBaseWidth * (0.2f + 0.8f * Math.Clamp(pressure, 0.01f, 1f));
        if (!_splinePlacing || _vectorStroke is null)
        {
            _vectorStrokeBefore = vl.CloneStrokes();
            _vectorStroke = new VectorStroke
            {
                Color = Document.Colors.Foreground,
                BaseWidth = VectorBaseWidth,
                PathMode = VectorPathMode.Spline,
                FillColor = Document.Colors.Foreground
            };
            vl.Strokes.Add(_vectorStroke);
            _splinePlacing = true;
        }

        // avoid duplicate if click too close to last
        if (_vectorStroke.Points.Count > 0)
        {
            var last = _vectorStroke.Points[^1];
            float dx = docPt.X - last.X, dy = docPt.Y - last.Y;
            if (dx * dx + dy * dy < 4f) return;
        }

        _vectorStroke.Points.Add(new StrokePoint(docPt.X, docPt.Y, pressure, w));
        RebuildFullVectorCache(vl);
        Document.IsDirty = true;
        FullRedraw();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FinishSplinePlacement(Float2 docPt, bool joinIfEndpoint)
    {
        if (!_splinePlacing || _vectorStroke is null || Document?.ActiveLayer is not VectorLayer vl)
        {
            _splinePlacing = false;
            return;
        }

        float hit = 12f / Math.Max(_viewport.Scale, 0.01f);

        // Right-click on an endpoint that has degree 1 → join (connect) to that node
        if (joinIfEndpoint && TryFindOpenEndpoint(vl, docPt, hit, out int joinSi, out int joinPi, out var joinPos))
        {
            // If joining to another stroke's endpoint, merge paths
            if (joinSi != vl.Strokes.IndexOf(_vectorStroke) && joinSi >= 0)
            {
                var other = vl.Strokes[joinSi];
                // append other's points (order depending on which end)
                if (joinPi == 0)
                {
                    // connect to start of other: reverse other and append
                    for (int i = 0; i < other.Points.Count; i++)
                        _vectorStroke.Points.Add(other.Points[i]);
                }
                else
                {
                    for (int i = other.Points.Count - 1; i >= 0; i--)
                        _vectorStroke.Points.Add(other.Points[i]);
                }
                vl.Strokes.RemoveAt(joinSi);
            }
            else
            {
                // same stroke: snap last to that endpoint and close if it's the start
                if (joinPi == 0 && _vectorStroke.Points.Count >= 3)
                {
                    _vectorStroke.Closed = true;
                    // optional: don't duplicate start point
                }
                else
                {
                    _vectorStroke.Points.Add(new StrokePoint(joinPos.X, joinPos.Y, 1f, VectorBaseWidth));
                }
            }
        }
        else if (_vectorStroke.Points.Count >= 1)
        {
            // place final control point at right-click position if far enough
            var last = _vectorStroke.Points[^1];
            float dx = docPt.X - last.X, dy = docPt.Y - last.Y;
            if (dx * dx + dy * dy >= 4f)
                _vectorStroke.Points.Add(new StrokePoint(docPt.X, docPt.Y, 1f, VectorBaseWidth));
        }

        // discard if fewer than 2 points
        if (_vectorStroke.Points.Count < 2)
        {
            vl.Strokes.Remove(_vectorStroke);
            if (_vectorStrokeBefore is not null)
                vl.ReplaceStrokes(_vectorStrokeBefore);
            _splinePlacing = false;
            _vectorStroke = null;
            _vectorStrokeBefore = null;
            FullRedraw();
            return;
        }

        vl.InvalidateCache();
        var after = vl.CloneStrokes();
        if (_vectorStrokeBefore is not null)
        {
            Document.History.PushAlreadyDone(
                new VectorLayerEditCommand(vl.Id, _vectorStrokeBefore, after, "VectorSpline"), Document);
        }
        _splinePlacing = false;
        _vectorStroke = null;
        _vectorStrokeBefore = null;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        FullRedraw();
        Eidolon.App.Logging.AppLog.Debug("Vector spline finished", "Draw");
    }

    /// <summary>Find endpoint of a path that has only one incident segment (open end).</summary>
    private static bool TryFindOpenEndpoint(VectorLayer vl, Float2 doc, float radius,
        out int strokeIndex, out int pointIndex, out Float2 pos)
    {
        strokeIndex = -1;
        pointIndex = -1;
        pos = default;
        float best = radius * radius;
        for (int si = 0; si < vl.Strokes.Count; si++)
        {
            var s = vl.Strokes[si];
            if (s.Closed || s.Points.Count < 1) continue;
            // endpoints only
            foreach (int pi in new[] { 0, s.Points.Count - 1 })
            {
                // degree-1: open path endpoints always degree 1
                var p = s.Points[pi];
                float dx = p.X - doc.X, dy = p.Y - doc.Y;
                float d2 = dx * dx + dy * dy;
                if (d2 <= best)
                {
                    best = d2;
                    strokeIndex = si;
                    pointIndex = pi;
                    pos = new Float2(p.X, p.Y);
                }
            }
        }
        return strokeIndex >= 0;
    }

    // ---- Vector eraser ----

    private bool BeginVectorErase(Float2 docPt)
    {
        if (Document?.ActiveLayer is not VectorLayer vl) return false;
        _vectorStrokeBefore = vl.CloneStrokes();
        _vectorErasing = true;
        EraseVectorAt(vl, docPt);
        return true;
    }

    private void ContinueVectorErase(Float2 docPt)
    {
        if (!_vectorErasing || Document?.ActiveLayer is not VectorLayer vl) return;
        EraseVectorAt(vl, docPt);
    }

    private void EndVectorErase()
    {
        if (Document?.ActiveLayer is VectorLayer vl && _vectorErasing && _vectorStrokeBefore is not null)
        {
            // only push history if something changed
            if (!StrokesEqual(_vectorStrokeBefore, vl.Strokes))
            {
                var after = vl.CloneStrokes();
                Document.History.PushAlreadyDone(
                    new VectorLayerEditCommand(vl.Id, _vectorStrokeBefore, after, "VectorErase"), Document);
                HistoryChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        _vectorErasing = false;
        _vectorStrokeBefore = null;
        FullRedraw();
    }

    private void EraseVectorAt(VectorLayer vl, Float2 docPt)
    {
        float hit = Math.Max(8f, VectorBaseWidth * 1.5f) / Math.Max(_viewport.Scale, 0.01f);
        bool removed = false;
        // Prefer whole-stroke hit
        if (VectorRasterizer.HitTestStroke(vl, docPt, hit, out int si))
        {
            vl.Strokes.RemoveAt(si);
            if (_selStrokeIndex == si) { _selStrokeIndex = -1; _selPointIndex = -1; }
            else if (_selStrokeIndex > si) _selStrokeIndex--;
            removed = true;
        }
        else if (VectorRasterizer.HitTestNode(vl, docPt, hit, out si, out int pi))
        {
            var s = vl.Strokes[si];
            if (s.Points.Count <= 2)
            {
                vl.Strokes.RemoveAt(si);
                if (_selStrokeIndex == si) { _selStrokeIndex = -1; _selPointIndex = -1; }
                else if (_selStrokeIndex > si) _selStrokeIndex--;
            }
            else
            {
                s.Points.RemoveAt(pi);
            }
            removed = true;
        }
        if (removed)
        {
            // cancel in-progress spline if erased
            if (_splinePlacing && _vectorStroke is not null && !vl.Strokes.Contains(_vectorStroke))
            {
                _splinePlacing = false;
                _vectorStroke = null;
                _vectorStrokeBefore = null;
            }
            vl.InvalidateCache();
            FullRedraw();
        }
    }

    private static bool StrokesEqual(List<VectorStroke> a, List<VectorStroke> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Points.Count != b[i].Points.Count) return false;
            if (a[i].Closed != b[i].Closed || a[i].Filled != b[i].Filled) return false;
        }
        return true;
    }

    private bool BeginVectorNodeEdit(Float2 docPt)
    {
        if (Document?.ActiveLayer is not VectorLayer vl) return false;
        float hit = 10f / Math.Max(_viewport.Scale, 0.01f);
        if (VectorRasterizer.HitTestNode(vl, docPt, hit, out int si, out int pi))
        {
            _nodeEditBefore = vl.CloneStrokes();
            _selStrokeIndex = si;
            _selPointIndex = pi;
            _nodeDragging = true;
            _nodeDragLast = docPt;
            // Delete with Alt
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                var stroke = vl.Strokes[si];
                if (stroke.Points.Count <= 2)
                    vl.Strokes.RemoveAt(si);
                else
                    stroke.Points.RemoveAt(pi);
                vl.InvalidateCache();
                var after = vl.CloneStrokes();
                Document.History.PushAlreadyDone(
                    new VectorLayerEditCommand(vl.Id, _nodeEditBefore, after, "VectorDeleteNode"), Document);
                _nodeDragging = false;
                _nodeEditBefore = null;
                _selStrokeIndex = -1;
                _selPointIndex = -1;
                HistoryChanged?.Invoke(this, EventArgs.Empty);
                FullRedraw();
                return true;
            }
            InvalidateVisual();
            return true;
        }
        // click empty: select stroke only
        if (VectorRasterizer.HitTestStroke(vl, docPt, hit, out si))
        {
            _selStrokeIndex = si;
            _selPointIndex = -1;
            InvalidateVisual();
            return false; // no capture needed
        }
        _selStrokeIndex = -1;
        _selPointIndex = -1;
        InvalidateVisual();
        return false;
    }

    private void ContinueVectorNodeDrag(Float2 docPt)
    {
        if (!_nodeDragging || Document?.ActiveLayer is not VectorLayer vl) return;
        if (_selStrokeIndex < 0 || _selStrokeIndex >= vl.Strokes.Count) return;
        var stroke = vl.Strokes[_selStrokeIndex];
        if (_selPointIndex < 0 || _selPointIndex >= stroke.Points.Count) return;
        var p = stroke.Points[_selPointIndex];
        stroke.Points[_selPointIndex] = p.WithPos(docPt);
        vl.InvalidateCache();
        FullRedraw();
    }

    private void EndVectorNodeDrag()
    {
        if (Document?.ActiveLayer is VectorLayer vl && _nodeEditBefore is not null && _nodeDragging)
        {
            vl.InvalidateCache();
            var after = vl.CloneStrokes();
            Document.History.PushAlreadyDone(
                new VectorLayerEditCommand(vl.Id, _nodeEditBefore, after, "VectorMoveNode"), Document);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
        _nodeDragging = false;
        _nodeEditBefore = null;
        FullRedraw();
    }

    private bool ApplyVectorCloseFill(Float2 docPt)
    {
        if (Document?.ActiveLayer is not VectorLayer vl) return false;
        float hit = 14f / Math.Max(_viewport.Scale, 0.01f);
        int si = _selStrokeIndex;
        if (si < 0 || si >= vl.Strokes.Count)
        {
            if (!VectorRasterizer.HitTestStroke(vl, docPt, hit, out si)
                && !VectorRasterizer.HitTestNode(vl, docPt, hit, out si, out _))
                return false;
        }
        if (si < 0 || si >= vl.Strokes.Count) return false;
        var stroke = vl.Strokes[si];
        if (stroke.Points.Count < 3) return false;

        var before = vl.CloneStrokes();
        stroke.Closed = true;
        stroke.Filled = true;
        stroke.FillColor = Document.Colors.Foreground;
        // keep stroke color as outline
        if (stroke.Color.A == 0)
            stroke.Color = Document.Colors.Foreground;
        vl.InvalidateCache();
        var after = vl.CloneStrokes();
        Document.History.PushAlreadyDone(
            new VectorLayerEditCommand(vl.Id, before, after, "VectorCloseFill"), Document);
        _selStrokeIndex = si;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        FullRedraw();
        return true;
    }

    private void CancelSplinePlacement()
    {
        if (!_splinePlacing || Document?.ActiveLayer is not VectorLayer vl || _vectorStroke is null)
        {
            _splinePlacing = false;
            _vectorStroke = null;
            _vectorStrokeBefore = null;
            return;
        }
        vl.Strokes.Remove(_vectorStroke);
        if (_vectorStrokeBefore is not null)
            vl.ReplaceStrokes(_vectorStrokeBefore);
        _splinePlacing = false;
        _vectorStroke = null;
        _vectorStrokeBefore = null;
        FullRedraw();
    }

    private void DrawVectorOverlay(DrawingContext dc)
    {
        if (Document?.ActiveLayer is not VectorLayer vl) return;
        if (_tool is not (CanvasTool.VectorNode or CanvasTool.VectorCloseFill or CanvasTool.VectorSpline
            or CanvasTool.VectorPen or CanvasTool.VectorEraser))
            return;
        if (vl.Strokes.Count == 0 && !_splinePlacing) return;

        double inv = 1.0 / Math.Max(_viewport.Scale, 0.01f);
        var pathPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 40, 120, 220)), 1.0 * inv)
        {
            DashStyle = DashStyles.Dash
        };
        var selPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 220, 80, 40)), 1.4 * inv);
        var nodeFill = System.Windows.Media.Brushes.White;
        var nodeActive = System.Windows.Media.Brushes.OrangeRed;
        var nodePen = new Pen(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)), 1.0 * inv);
        double nodeR = 4.5 * inv;

        for (int si = 0; si < vl.Strokes.Count; si++)
        {
            var stroke = vl.Strokes[si];
            if (stroke.Points.Count == 0) continue;
            bool selected = si == _selStrokeIndex || ReferenceEquals(stroke, _vectorStroke);
            var pen = selected ? selPen : pathPen;

            // control polygon for spline / node edit / placing
            if (selected || stroke.PathMode == VectorPathMode.Spline || _tool == CanvasTool.VectorNode || _splinePlacing)
            {
                for (int i = 1; i < stroke.Points.Count; i++)
                {
                    var a = stroke.Points[i - 1];
                    var b = stroke.Points[i];
                    dc.DrawLine(pen, new Point(a.X, a.Y), new Point(b.X, b.Y));
                }
                if (stroke.Closed && stroke.Points.Count > 2)
                {
                    var a = stroke.Points[^1];
                    var b = stroke.Points[0];
                    dc.DrawLine(pen, new Point(a.X, a.Y), new Point(b.X, b.Y));
                }
            }

            if (_tool == CanvasTool.VectorNode || _tool == CanvasTool.VectorSpline || selected || _splinePlacing)
            {
                for (int pi = 0; pi < stroke.Points.Count; pi++)
                {
                    var p = stroke.Points[pi];
                    bool active = selected && pi == _selPointIndex;
                    // open endpoints slightly larger for join affordance
                    double r = (!stroke.Closed && (pi == 0 || pi == stroke.Points.Count - 1) && _tool == CanvasTool.VectorSpline)
                        ? nodeR * 1.35 : nodeR;
                    dc.DrawEllipse(active ? nodeActive : nodeFill, nodePen, new Point(p.X, p.Y), r, r);
                }
            }
        }
    }

    private void RebuildVectorCacheExceptCurrent(VectorLayer vl)
    {
        if (Document is null) return;
        var cache = new TileSurface(Document.Width, Document.Height);
        foreach (var s in vl.Strokes)
        {
            if (ReferenceEquals(s, _vectorStroke)) continue;
            VectorRasterizer.DrawStroke(cache, s, 1f);
        }
        vl.RasterCache = cache;
        vl.CacheDirty = false;
    }

    private void RebuildFullVectorCache(VectorLayer vl)
    {
        if (Document is null) return;
        var cache = new TileSurface(Document.Width, Document.Height);
        foreach (var s in vl.Strokes)
            VectorRasterizer.DrawStroke(cache, s, 1f);
        vl.RasterCache = cache;
        vl.CacheDirty = false;
    }

    private static void StampLive(VectorLayer vl, float x, float y, float width, ColorRgba8 color)
    {
        if (vl.RasterCache is null) return;
        VectorRasterizer.StampCircle(vl.RasterCache, x, y, Math.Max(0.5f, width * 0.5f), color, 1f);
        vl.CacheDirty = false;
    }

    private void PlaceText(Float2 docPt)
    {
        if (Document is null) return;
        var layer = Document.AddTextLayer();
        layer.X = docPt.X;
        layer.Y = docPt.Y;
        layer.Color = Document.Colors.Foreground;
        layer.Content = "Text";
        layer.CacheDirty = true;
        TextRasterizer.RebuildCache(layer, Document.Width, Document.Height);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        FullRedraw();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FinishFrameRect()
    {
        if (Document is null) return;
        // Independent panel tool: always target a FrameLayer (create if needed)
        FrameLayer fl;
        if (Document.ActiveLayer is FrameLayer existing)
            fl = existing;
        else
        {
            fl = Document.AddFrameLayer();
            Document.ActiveLayerId = fl.Id;
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
        int x0 = (int)MathF.Floor(Math.Min(_selStartDoc.X, _selCurrentDoc.X));
        int y0 = (int)MathF.Floor(Math.Min(_selStartDoc.Y, _selCurrentDoc.Y));
        int x1 = (int)MathF.Ceiling(Math.Max(_selStartDoc.X, _selCurrentDoc.X));
        int y1 = (int)MathF.Ceiling(Math.Max(_selStartDoc.Y, _selCurrentDoc.Y));
        x0 = Math.Clamp(x0, 0, Document.Width - 1);
        y0 = Math.Clamp(y0, 0, Document.Height - 1);
        x1 = Math.Clamp(x1, 0, Document.Width);
        y1 = Math.Clamp(y1, 0, Document.Height);
        if (x1 <= x0 + 1 || y1 <= y0 + 1)
        {
            Eidolon.App.Logging.AppLog.Debug($"Frame too small {x0},{y0}-{x1},{y1}", "Frame");
            return;
        }
        var rect = new IntRect(x0, y0, x1 - x0, y1 - y0);
        fl.Frames.Add(new FrameRect { Bounds = rect });
        fl.InvalidateCache();
        Document.IsDirty = true;
        Eidolon.App.Logging.AppLog.Info($"Frame added {rect.X},{rect.Y} {rect.Width}x{rect.Height} on {fl.Name}", "Frame");
        FullRedraw();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EnsureTextCaches()
    {
        if (Document is null) return;
        foreach (var n in Document.Root.Children)
        {
            if (n is TextLayer tl && (tl.CacheDirty || tl.RasterCache is null))
                TextRasterizer.RebuildCache(tl, Document.Width, Document.Height);
        }
    }

    private void DoFill(Point screen)
    {
        if (Document?.ActiveRasterLayer is not { } layer) return;
        var sample = ToSample(screen, 1f, PointerPhase.Press);
        int x = (int)sample.DocumentPos.X;
        int y = (int)sample.DocumentPos.Y;
        var keysBefore = layer.Surface.Tiles.Keys.ToList();
        var before = layer.Surface.SnapshotTiles(keysBefore);
        // also capture tiles that may be created - snapshot empty for potential region is hard; capture all after and diff
        var dirty = FloodFill.Fill(layer, x, y, Document.Colors.Foreground, 32,
            (layer.Locks & LayerLocks.Transparency) != 0 || _preset.Params.LockAlpha,
            Document.Selection.IsEmpty ? null : Document.Selection);
        if (dirty.IsEmpty) return;
        var keysAfter = layer.Surface.Tiles.Keys.ToList();
        var allKeys = keysBefore.Union(keysAfter).Distinct().ToList();
        // rebuild before for new keys as empty
        foreach (var k in allKeys)
            if (!before.ContainsKey(k)) before[k] = new Tile(layer.Surface.TileSize);
        var after = layer.Surface.SnapshotTiles(allKeys);
        Document.History.PushAlreadyDone(new TileEditCommand(layer.Id, before, after, "Fill"), Document);
        RedrawDirty(dirty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
    private void BeginStroke(Point screen, float pressure)
    {
        if (Document?.ActiveRasterLayer is not { } layer) return;
        if ((layer.Locks & LayerLocks.Pixels) != 0) return;

        _preset.Params.SizePx = _brushSize;
        var p = _preset.Params;
        var strokePreset = new BrushPreset
        {
            Name = _preset.Name,
            Kind = _preset.Kind,
            Params = new BrushParameters
            {
                SizePx = p.SizePx,
                MinSizeRatio = p.MinSizeRatio,
                Opacity = p.Opacity,
                Flow = p.Flow,
                Hardness = p.Hardness,
                SoftEdge = p.SoftEdge,
                Blend = p.Blend,
                Spacing = p.Spacing,
                AntiAlias = p.AntiAlias,
                EraseMode = p.EraseMode,
                LockAlpha = p.LockAlpha || (layer.Locks & LayerLocks.Transparency) != 0,
                StabilizerStrength = p.StabilizerStrength,
                SizeByPressure = p.SizeByPressure,
                OpacityByPressure = p.OpacityByPressure,
                FlowByPressure = p.FlowByPressure,
                TextureStrength = p.TextureStrength,
                TextureScale = p.TextureScale,
                TextureSeed = p.TextureSeed,
                SmudgeStrength = p.SmudgeStrength
            }
        };

        var first = ToSample(screen, pressure, PointerPhase.Press);
        _straightOrigin = first.DocumentPos;
        Document.Rulers.BeginStrokeConstraint(first.DocumentPos);

        _stroke = new StrokeSession(Document, layer, strokePreset, Document.Colors.Foreground, _stabilizer,
            AppSettings.Current.WillowOverlap);
        _stroke.Begin(first);
        RedrawDirty(_stroke.DirtyRect);

        // Symmetry: mirror stroke on same layer
        if (Document.Rulers.ShouldMirrorStroke)
        {
            var mpos = Document.Rulers.MirrorAcrossSymmetry(first.DocumentPos);
            var msample = new PointerSample(first.TimeSec, mpos, first.Pressure, first.Phase);
            _mirrorStroke = new StrokeSession(Document, layer, strokePreset, Document.Colors.Foreground, _stabilizer,
                AppSettings.Current.WillowOverlap);
            _mirrorStroke.Begin(msample);
            RedrawDirty(_mirrorStroke.DirtyRect);
        }
        else _mirrorStroke = null;
    }

    private void ContinueStroke(Point screen, float pressure)
    {
        if (_stroke is null || Document is null) return;
        var sample = ToSample(screen, pressure, PointerPhase.Move);
        var dirty = _stroke.Move(sample);
        RedrawDirty(dirty);
        if (_mirrorStroke is not null)
        {
            var mpos = Document.Rulers.MirrorAcrossSymmetry(sample.DocumentPos);
            var msample = new PointerSample(sample.TimeSec, mpos, sample.Pressure, sample.Phase);
            RedrawDirty(_mirrorStroke.Move(msample));
        }
    }

    private void EndStroke()
    {
        if (_stroke is null || Document is null) return;
        var cmd = _stroke.End();
        if (_mirrorStroke is not null)
        {
            var mcmd = _mirrorStroke.End();
            // merge tile edits if both present
            if (cmd != null && mcmd != null)
            {
                // push both as sequential commands (one operation each is ok; user wanted one frame per op for timelapse)
                Document.History.PushAlreadyDone(cmd, Document);
                Document.History.PushAlreadyDone(mcmd, Document);
            }
            else if (cmd != null) Document.History.PushAlreadyDone(cmd, Document);
            else if (mcmd != null) Document.History.PushAlreadyDone(mcmd, Document);
            _mirrorStroke = null;
        }
        else if (cmd != null)
            Document.History.PushAlreadyDone(cmd, Document);
        Document.Rulers.EndStrokeConstraint();
        _stroke = null;
        _straightOrigin = null;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RotateView(float deltaDeg)
    {
        _viewport.RotationDeg = (_viewport.RotationDeg + deltaDeg) % 360f;
        InvalidateVisual();
    }

    public void ResetView()
    {
        _viewport.Scale = 1f;
        _viewport.Pan = new Float2(0, 0);
        _viewport.RotationDeg = 0;
        _viewport.MirrorX = false;
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        if (Document is null) return;
        Document.Selection.Clear();
        InvalidateVisual();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void InvertSelection()
    {
        if (Document is null) return;
        var sel = Document.Selection;
        int w = Document.Width, h = Document.Height;
        bool wasEmpty = sel.IsEmpty;
        if (wasEmpty)
        {
            sel.ApplyRect(new IntRect(0, 0, w, h), SelectionMode.Replace);
        }
        else
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte a = sel.Get(x, y);
                sel.Set(x, y, (byte)(255 - a));
            }
            sel.RecalcBounds();
        }
        InvalidateVisual();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAll()
    {
        if (Document is null) return;
        Document.Selection.ApplyRect(new IntRect(0, 0, Document.Width, Document.Height), SelectionMode.Replace);
        InvalidateVisual();
    }

    public void ToggleMirror()
    {
        _viewport.MirrorX = !_viewport.MirrorX;
        InvalidateVisual();
    }
}

