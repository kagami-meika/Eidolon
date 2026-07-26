using System.Runtime.InteropServices;

namespace Eidolon.Input;

/// <summary>WinTab pressure. LOGCONTEXT uses Win32 BOOL (4-byte) packing.</summary>
public sealed class WinTabTabletService : IDisposable
{
    private IntPtr _ctx;
    private readonly object _gate = new();
    private IntPtr _pollBuf;
    private int _pollBufBytes;

    public bool IsAvailable { get; private set; }
    public string Status { get; private set; } = "WinTab: not found";
    public uint MaxPressure { get; private set; } = 1023;
    public float LastPressure { get; private set; } = -1f;
    public bool HasRecentPacket { get; private set; }
    public long LastPacketTicks { get; private set; }
    public int PacketCount { get; private set; }
    public string LastError { get; private set; } = "";

    public void TryInitialize(IntPtr hwnd)
    {
        Shutdown();
        try
        {
            uint sz = WTInfoA(0, 0, IntPtr.Zero);
            // sz==0 can still mean DLL loaded with no devices

            int lcSize = Marshal.SizeOf<LOGCONTEXT>();
            IntPtr buf = Marshal.AllocHGlobal(lcSize);
            LOGCONTEXT lc;
            try
            {
                // WTI_DEFCONTEXT=3, WTI_DEFSYSCTX=4
                bool got = WTInfoA(3, 0, buf) != 0;
                if (!got) got = WTInfoA(4, 0, buf) != 0;
                if (!got)
                {
                    IsAvailable = false;
                    Status = "WinTab: no default context";
                    LastError = "WTInfo DEFCONTEXT failed";
                    return;
                }
                lc = Marshal.PtrToStructure<LOGCONTEXT>(buf)!;
            }
            finally { Marshal.FreeHGlobal(buf); }

            // Force message mode + our packet format
            lc.lcName = "Eidolon";
            lc.lcOptions = (lc.lcOptions | CXO_MESSAGES) & ~CXO_SYSTEM; // app-owned; still get packets
            // Actually keep SYSTEM so cursor works: many drivers need it
            lc.lcOptions = lc.lcOptions | CXO_MESSAGES | CXO_SYSTEM;
            lc.lcMsgBase = WT_DEFBASE;
            lc.lcPktData = PACKETDATA;
            lc.lcPktMode = 0;
            lc.lcMoveMask = PACKETDATA;
            // Ensure full pressure range in output if possible
            // lcOutExt often already set from default

            _ctx = WTOpenA(hwnd, ref lc, true);
            if (_ctx == IntPtr.Zero)
            {
                // retry without clearing system
                lc.lcOptions = CXO_MESSAGES | CXO_SYSTEM;
                _ctx = WTOpenA(hwnd, ref lc, true);
            }
            if (_ctx == IntPtr.Zero)
            {
                IsAvailable = false;
                Status = "WinTab: WTOpen failed";
                LastError = "WTOpen returned 0";
                return;
            }

            WTQueueSizeSet(_ctx, 256);

            IntPtr abuf = Marshal.AllocHGlobal(Marshal.SizeOf<AXIS>());
            try
            {
                if (WTInfoA(WTI_DEVICES, DVC_NPRESSURE, abuf) != 0)
                {
                    var axis = Marshal.PtrToStructure<AXIS>(abuf);
                    int range = axis.axMax - axis.axMin;
                    if (range > 0) MaxPressure = (uint)range;
                }
            }
            finally { Marshal.FreeHGlobal(abuf); }

            if (MaxPressure == 0) MaxPressure = 1023;
            IsAvailable = true;
            Status = $"WinTab OK (Pmax={MaxPressure})";
        }
        catch (DllNotFoundException)
        {
            IsAvailable = false;
            Status = "WinTab: no Wintab32.dll";
            LastError = "DllNotFound";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Status = "WinTab: " + ex.Message;
            LastError = ex.Message;
        }
    }

    public bool ProcessMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (_ctx == IntPtr.Zero) return false;
        if (msg != WT_PACKET) return false;

        lock (_gate)
        {
            var pkt = new PACKET();
            IntPtr hCtx = lParam != IntPtr.Zero ? lParam : _ctx;
            uint serial = unchecked((uint)(wParam.ToInt64() & 0xFFFFFFFF));
            if (!WTPacket(hCtx, serial, ref pkt))
            {
                if (!WTPacket(_ctx, serial, ref pkt))
                    return false;
            }

            ApplyPacket(in pkt);
        }
        return false;
    }

    /// <summary>Drain any queued packets (call from composition/render).</summary>
    public void Poll()
    {
        if (_ctx == IntPtr.Zero) return;
        try
        {
            lock (_gate)
            {
                if (_ctx == IntPtr.Zero) return;
                // Reuse buffer for up to 32 packets (avoid alloc every frame).
                int pktSize = Marshal.SizeOf<PACKET>();
                int need = pktSize * 32;
                if (_pollBuf == IntPtr.Zero || _pollBufBytes < need)
                {
                    if (_pollBuf != IntPtr.Zero) Marshal.FreeHGlobal(_pollBuf);
                    _pollBuf = Marshal.AllocHGlobal(need);
                    _pollBufBytes = need;
                }

                int n = WTPacketsGet(_ctx, 32, _pollBuf);
                if (n <= 0) return;
                for (int i = 0; i < n; i++)
                {
                    var pkt = Marshal.PtrToStructure<PACKET>(_pollBuf + i * pktSize);
                    ApplyPacket(in pkt);
                }
            }
        }
        catch
        {
            // Driver/context may be invalid after focus change; keep UI responsive.
        }
    }

    /// <summary>Enable/disable tablet context around window activation changes.</summary>
    public void SetEnabled(bool enabled)
    {
        if (_ctx == IntPtr.Zero) return;
        try
        {
            lock (_gate)
            {
                if (_ctx == IntPtr.Zero) return;
                WTEnable(_ctx, enabled);
                if (enabled)
                    WTOverlap(_ctx, true);
            }
        }
        catch
        {
            // ignore driver failures
        }
    }

    private void ApplyPacket(in PACKET pkt)
    {
        float p;
        if (MaxPressure > 0)
            p = pkt.pkNormalPressure / (float)MaxPressure;
        else
            p = 0f;
        p = Math.Clamp(p, 0f, 1f);

        // Hover packets may have 0 pressure — keep last contact pressure if button down missing
        bool tipDown = (pkt.pkButtons & 0x1) != 0
                       || (pkt.pkStatus & 0x0008) != 0; // TPS_PROXIMITY-ish varies

        if (pkt.pkNormalPressure == 0 && !tipDown)
            return;

        if (p <= 0f) p = 0.01f;

        LastPressure = p;
        HasRecentPacket = true;
        LastPacketTicks = Environment.TickCount64;
        PacketCount++;
        Status = $"WinTab P={p:F2} n={PacketCount}";
    }

    public float GetPressureOrDefault(float fallback)
    {
        if (HasRecentPacket && Environment.TickCount64 - LastPacketTicks < 150)
            return Math.Max(LastPressure, 0.001f);
        return fallback;
    }

    public void Shutdown()
    {
        lock (_gate)
        {
            if (_ctx != IntPtr.Zero)
            {
                try { WTClose(_ctx); } catch { /* ignore */ }
                _ctx = IntPtr.Zero;
            }
            if (_pollBuf != IntPtr.Zero)
            {
                try { Marshal.FreeHGlobal(_pollBuf); } catch { /* ignore */ }
                _pollBuf = IntPtr.Zero;
                _pollBufBytes = 0;
            }
            IsAvailable = false;
        }
    }

    public void Dispose() => Shutdown();

    private const uint CXO_SYSTEM = 0x0001;
    private const uint CXO_MESSAGES = 0x0004;
    private const uint WT_DEFBASE = 0x7FF0;
    private const int WT_PACKET = (int)WT_DEFBASE + 0;
    private const uint WTI_DEVICES = 100;
    private const uint DVC_NPRESSURE = 15;

    private const uint PK_STATUS = 0x0002;
    private const uint PK_TIME = 0x0004;
    private const uint PK_CURSOR = 0x0020;
    private const uint PK_BUTTONS = 0x0040;
    private const uint PK_X = 0x0080;
    private const uint PK_Y = 0x0100;
    private const uint PK_NORMAL_PRESSURE = 0x0400;
    private const uint PK_ORIENTATION = 0x1000;
    private const uint PACKETDATA =
        PK_STATUS | PK_TIME | PK_CURSOR | PK_BUTTONS | PK_X | PK_Y | PK_NORMAL_PRESSURE | PK_ORIENTATION;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct LOGCONTEXT
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)]
        public string lcName;
        public uint lcOptions;
        public uint lcStatus;
        public uint lcLocks;
        public uint lcMsgBase;
        public uint lcDevice;
        public uint lcPktRate;
        public uint lcPktData;
        public uint lcPktMode;
        public uint lcMoveMask;
        public uint lcBtnDnMask;
        public uint lcBtnUpMask;
        public int lcInOrgX, lcInOrgY, lcInOrgZ;
        public int lcInExtX, lcInExtY, lcInExtZ;
        public int lcOutOrgX, lcOutOrgY, lcOutOrgZ;
        public int lcOutExtX, lcOutExtY, lcOutExtZ;
        // FIX32 often as int
        public int lcSensX, lcSensY, lcSensZ;
        // Win32 BOOL is 4 bytes — critical
        public int lcSysMode;
        public int lcSysOrgX, lcSysOrgY;
        public int lcSysExtX, lcSysExtY;
        public int lcSysSensX, lcSysSensY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AXIS
    {
        public int axMin, axMax, axUnits, axResolution;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PACKET
    {
        public uint pkStatus;
        public uint pkTime;
        public uint pkCursor;
        public uint pkButtons;
        public int pkX;
        public int pkY;
        public uint pkNormalPressure;
        public int orAzimuth;
        public int orAltitude;
        public int orTwist;
    }

    [DllImport("Wintab32.dll", CharSet = CharSet.Ansi)]
    private static extern uint WTInfoA(uint wCategory, uint nIndex, IntPtr lpOutput);

    [DllImport("Wintab32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr WTOpenA(IntPtr hWnd, ref LOGCONTEXT lpLogCtx, bool fEnable);

    [DllImport("Wintab32.dll")]
    private static extern bool WTClose(IntPtr hCtx);

    [DllImport("Wintab32.dll")]
    private static extern bool WTEnable(IntPtr hCtx, bool fEnable);

    [DllImport("Wintab32.dll")]
    private static extern bool WTOverlap(IntPtr hCtx, bool fToTop);

    [DllImport("Wintab32.dll")]
    private static extern bool WTPacket(IntPtr hCtx, uint wSerial, ref PACKET lpPkt);

    [DllImport("Wintab32.dll")]
    private static extern bool WTQueueSizeSet(IntPtr hCtx, int nPkts);

    [DllImport("Wintab32.dll")]
    private static extern int WTPacketsGet(IntPtr hCtx, int cMaxPkts, IntPtr lpPkts);
}
