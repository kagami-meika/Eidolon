using System.Runtime.InteropServices;

namespace Eidolon.Input;

/// <summary>WM_POINTER pen pressure (Windows Ink stack).</summary>
public sealed class PointerPenService
{
    public const int WM_POINTERUPDATE = 0x0245;
    public const int WM_POINTERDOWN = 0x0246;
    public const int WM_POINTERUP = 0x0247;
    public const int WM_POINTERENTER = 0x0249;
    public const int WM_POINTERLEAVE = 0x024A;

    public float LastPressure { get; private set; } = -1f;
    public bool HasRecentSample { get; private set; }
    public long LastSampleTicks { get; private set; }
    public bool LastIsEraser { get; private set; }
    public bool InContact { get; private set; }
    public string Status { get; private set; } = "Pointer: waiting";
    public int SampleCount { get; private set; }
    public uint LastRawPressure { get; private set; }

    public bool ProcessMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg is not (WM_POINTERUPDATE or WM_POINTERDOWN or WM_POINTERUP or WM_POINTERENTER or WM_POINTERLEAVE))
            return false;

        uint pointerId = (uint)(wParam.ToInt64() & 0xFFFF);
        if (!GetPointerType(pointerId, out int type))
            return false;

        // PT_POINTER=1 PT_TOUCH=2 PT_PEN=3 PT_MOUSE=4 PT_TOUCHPAD=5
        if (type != 3)
            return false;

        // Prefer frame history for smoother pressure
        if (TryReadHistory(pointerId))
            return false;

        var info = new POINTER_PEN_INFO();
        if (!GetPointerPenInfo(pointerId, ref info))
            return false;

        ApplyPenInfo(in info, msg == WM_POINTERUP);
        return false;
    }

    private bool TryReadHistory(uint pointerId)
    {
        // GetPointerPenInfoHistory
        int count = 64;
        int size = Marshal.SizeOf<POINTER_PEN_INFO>();
        IntPtr buf = Marshal.AllocHGlobal(size * count);
        try
        {
            int entries = count;
            if (!GetPointerPenInfoHistory(pointerId, ref entries, buf) || entries <= 0)
                return false;
            // newest is usually last or first depending on API — docs: index 0 is current
            var info = Marshal.PtrToStructure<POINTER_PEN_INFO>(buf);
            ApplyPenInfo(in info, false);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private void ApplyPenInfo(in POINTER_PEN_INFO info, bool up)
    {
        uint flags = info.pointerInfo.pointerFlags;
        InContact = (flags & POINTER_FLAG_INCONTACT) != 0 || (flags & POINTER_FLAG_DOWN) != 0;
        if (up || (flags & POINTER_FLAG_UP) != 0)
            InContact = false;

        LastRawPressure = info.pressure;
        float p;
        // PEN_MASK_PRESSURE = 0x1
        bool hasPressure = (info.penMask & 0x1) != 0 || info.pressure > 0;
        if (hasPressure && info.pressure > 0)
            p = Math.Clamp(info.pressure / 1024f, 0.001f, 1f);
        else if (InContact)
            p = 0.5f; // contact without pressure channel
        else
            return;

        LastPressure = p;
        HasRecentSample = true;
        LastSampleTicks = Environment.TickCount64;
        LastIsEraser = (info.penFlags & 0x1) != 0;
        SampleCount++;
        Status = $"Pointer P={p:F2} raw={info.pressure}";
    }

    public float GetPressureOrDefault(float fallback)
    {
        if (HasRecentSample && Environment.TickCount64 - LastSampleTicks < 150)
            return Math.Max(LastPressure, 0.001f);
        return fallback;
    }

    private const uint POINTER_FLAG_INCONTACT = 0x00000004;
    private const uint POINTER_FLAG_DOWN = 0x00010000;
    private const uint POINTER_FLAG_UP = 0x00040000;

    [DllImport("user32.dll")]
    private static extern bool GetPointerType(uint pointerId, out int pointerType);

    [DllImport("user32.dll")]
    private static extern bool GetPointerPenInfo(uint pointerId, ref POINTER_PEN_INFO penInfo);

    [DllImport("user32.dll")]
    private static extern bool GetPointerPenInfoHistory(uint pointerId, ref int entriesCount, IntPtr penInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_INFO
    {
        public uint pointerType;
        public uint pointerId;
        public uint frameId;
        public uint pointerFlags;
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int inputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_PEN_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint penFlags;
        public uint penMask;
        public uint pressure;
        public uint rotation;
        public int tiltX;
        public int tiltY;
    }
}
