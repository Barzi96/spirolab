using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SpiroLab.Device
{
    public interface IMirTransport
    {
        bool IsOpen { get; }
        string DevicePath { get; }
        bool Open(out string error);
        void Close();
        byte State();
        byte[] ReadSda(int length);
        bool WaitReady(int timeoutMs);
        byte[] Get(byte command, int length);
        bool Send(byte command);
        byte[] ReadBlock(byte command, int attempts, int delayMs);
    }
    /// <summary>
    /// Direct transport to the MIR spirolab over MIRUSB.SYS (no mirwsp.dll).
    ///
    /// Every command is a vendor control transfer wrapped in
    /// DeviceIoControl(IOCTL_VENDOR_REQUEST) with a 12 byte WDM setup packet:
    ///   OUT: RequestType 0x40, Request = command, no data
    ///   IN : RequestType 0xC1, Request = command, Length = byte count
    /// Bulk endpoints are reached through the driver device path + "\0" (IN)
    /// and + "\1" (OUT) and are only used for the SDA block and the
    /// cod_LAST / cod_CURVE result blocks.
    /// </summary>
    public sealed class MirUsb : IMirTransport, IDisposable
    {
        // ---- win32 ------------------------------------------------
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        private const uint IOCTL_VENDOR_REQUEST = 0x00222450;

        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_ALLCLASSES = 0x04;
        private const uint SPDRP_HARDWAREID = 0x01;

        private const int ERROR_IO_PENDING = 997;

        [StructLayout(LayoutKind.Sequential)]
        private struct WDM_SETUP_PACKET
        {
            public byte RequestType;
            public byte Request;
            public ushort Value;
            public ushort Index;
            public ushort Length;
            public uint ioBuffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OVERLAPPED
        {
            public IntPtr InternalLow;
            public IntPtr InternalHigh;
            public uint OffsetLow;
            public uint OffsetHigh;
            public IntPtr EventHandle;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string name, uint access, uint share,
            IntPtr sec, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr h, uint code, IntPtr inBuf, uint inSize,
            IntPtr outBuf, uint outSize, out uint returned, IntPtr overlapped);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevsExW(IntPtr classGuid, IntPtr enumerator,
            IntPtr deviceInfoSet, uint flags, IntPtr deviceInfoSetToCompar, string machineName, IntPtr reserved);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data,
            uint property, out uint type, byte[] buffer, uint size, out uint required);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data,
            StringBuilder id, uint size, out uint required);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr h, IntPtr buf, uint n, out uint read, IntPtr ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr h, IntPtr buf, uint n, out uint written, IntPtr ov);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateEvent(IntPtr attr, bool manual, bool initial, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr h, uint ms);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr h, IntPtr ov, out uint transferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(IntPtr h, IntPtr operation);

        // ---- state -------------------------------------------------
        private static readonly List<System.Threading.Timer> CleanupTimers=new List<System.Threading.Timer>();
        private IntPtr _h = IntPtr.Zero;
        private IntPtr _bulkIn = IntPtr.Zero;
        private IntPtr _bulkOut = IntPtr.Zero;
        private GCHandle _setupPin;
        private readonly byte[] _setupScratch = new byte[16];

        public bool IsOpen { get { return _h != IntPtr.Zero; } }
        public int LastError { get; private set; }
        public bool CleanupPending { get; private set; }
        public string DevicePath { get; private set; }
        public string SerialNumber { get; private set; }
        public byte Pid { get; private set; }
        public bool BulkInOk { get { return _bulkIn != IntPtr.Zero; } }

        // ---- device paths ------------------------------------------
        public static List<string> FindDevicePaths(string vid)
        {
            List<string> list = new List<string>();
            IntPtr set = SetupDiGetClassDevsExW(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_ALLCLASSES, IntPtr.Zero, null, IntPtr.Zero);
            if (set == IntPtr.Zero || set.ToInt64() == -1) return list;

            try
            {
                SP_DEVINFO_DATA did = new SP_DEVINFO_DATA();
                did.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref did); i++)
                {
                    byte[] buf = new byte[2048];
                    uint need, type;
                    if (!SetupDiGetDeviceRegistryPropertyW(set, ref did, SPDRP_HARDWAREID,
                            out type, buf, (uint)buf.Length, out need))
                        continue;

                    string hw = Encoding.Unicode.GetString(buf, 0, (int)Math.Max(0, need - 2)).TrimEnd('\0');
                    if (hw.IndexOf("VID_" + vid, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    StringBuilder sb = new StringBuilder(512);
                    if (!SetupDiGetDeviceInstanceIdW(set, ref did, sb, 512, out need)) continue;
                    string inst = sb.ToString().TrimEnd('\0');

                    string guid = null;
                    try
                    {
                        using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                                @"SYSTEM\CurrentControlSet\Enum\" + inst + @"\Device Parameters"))
                        {
                            if (k != null)
                            {
                                object v = k.GetValue("DeviceInterfaceGUIDs");
                                string[] arr = v as string[];
                                if (arr != null && arr.Length > 0) guid = arr[0];
                                else if (v is string) guid = (string)v;
                            }
                        }
                    }
                    catch { }

                    if (string.IsNullOrEmpty(guid)) guid = "{a5dcbf10-6530-11d2-901f-00c04fb951ed}";

                    list.Add(@"\\?\" + inst.Replace('\\', '#').ToLowerInvariant()
                             + "#" + guid.ToLowerInvariant());
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        // ---- open / close ------------------------------------------
        public bool Open(out string error)
        {
            error = null;
            if(CleanupPending) { error="درایور لغو عملیات را تأیید نکرد؛ اتصال مجدد در این اجرا غیرفعال است."; return false; }
            if (IsOpen) { error = null; return true; }

            List<string> paths = FindDevicePaths("152B");
            if (paths.Count == 0)
            {
                error = "هیچ دستگاه MIR با VID_152B پیدا نشد. کابل USB و درایور MIRUSB را بررسی کنید.";
                return false;
            }

            _setupPin = GCHandle.Alloc(_setupScratch, GCHandleType.Pinned);

            foreach (string p in paths)
            {
                IntPtr h = CreateFileW(p, GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                    OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, IntPtr.Zero);

                if (h == IntPtr.Zero || h.ToInt64() == -1) { LastError=Marshal.GetLastWin32Error(); continue; }

                _h = h;
                DevicePath = p;
                OpenBulk();
                return true;
            }

            error = "دستگاه پیدا شد ولی باز نشد. اگر MIR Spiro باز است آن را ببندید و دوباره تلاش کنید.";
            FreeSetupPin();
            return false;
        }

        private void OpenBulk()
        {
            CloseBulk();
            _bulkIn = CreateFileW(DevicePath + "\\0", GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (_bulkIn.ToInt64() == -1) _bulkIn = IntPtr.Zero;

            _bulkOut = CreateFileW(DevicePath + "\\1", GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (_bulkOut.ToInt64() == -1) _bulkOut = IntPtr.Zero;
        }

        public void Close()
        {
            CloseBulk();
            if (_h != IntPtr.Zero) { CloseHandle(_h); _h = IntPtr.Zero; }
            FreeSetupPin();
            DevicePath = null;
        }

        private void CloseBulk()
        {
            if (_bulkIn != IntPtr.Zero) { CloseHandle(_bulkIn); _bulkIn = IntPtr.Zero; }
            if (_bulkOut != IntPtr.Zero) { CloseHandle(_bulkOut); _bulkOut = IntPtr.Zero; }
        }

        private void FreeSetupPin()
        {
            if (_setupPin.IsAllocated) _setupPin.Free();
        }

        public void Dispose() { Close(); }

        // ---- control transfers -------------------------------------
        private IntPtr Setup(byte reqType, byte req, ushort value, ushort index, ushort length)
        {
            if (!_setupPin.IsAllocated) _setupPin = GCHandle.Alloc(_setupScratch, GCHandleType.Pinned);
            WDM_SETUP_PACKET sp = new WDM_SETUP_PACKET();
            sp.RequestType = reqType;
            sp.Request = req;
            sp.Value = value;
            sp.Index = index;
            sp.Length = length;
            sp.ioBuffer = 0;
            Marshal.StructureToPtr(sp, _setupPin.AddrOfPinnedObject(), false);
            return _setupPin.AddrOfPinnedObject();
        }

        // Control handle is overlapped: pass an actual OVERLAPPED and wait.
        // Passing NULL on an overlapped handle can incorrectly appear successful.
        private bool Control(byte type, byte command, byte[] data, out uint count)
        {
            count=0; if(_h==IntPtr.Zero || CleanupPending) return false;
            IntPtr ov=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(OVERLAPPED)));
            IntPtr ev=CreateEvent(IntPtr.Zero,true,false,null);
            GCHandle pin=default(GCHandle); bool release=true;
            try
            {
                if(ev==IntPtr.Zero) { LastError=Marshal.GetLastWin32Error(); return false; }
                if(data!=null) pin=GCHandle.Alloc(data,GCHandleType.Pinned);
                Marshal.StructureToPtr(new OVERLAPPED { EventHandle=ev },ov,false);
                IntPtr setup=Setup(type,command,0,0,(ushort)(data==null?0:data.Length));
                bool ok=DeviceIoControl(_h,IOCTL_VENDOR_REQUEST,setup,12,data==null?IntPtr.Zero:pin.AddrOfPinnedObject(),(uint)(data==null?0:data.Length),out count,ov);
                if(ok) { LastError=0; return true; }
                LastError=Marshal.GetLastWin32Error(); if(LastError!=ERROR_IO_PENDING) return false;
                if(WaitForSingleObject(ev,2000)==0) { ok=GetOverlappedResult(_h,ov,out count,false); LastError=ok?0:Marshal.GetLastWin32Error(); return ok; }
                CancelIoEx(_h,ov); LastError=1460;
                if(WaitForSingleObject(ev,2000)!=0) { release=false; Quarantine(ev,ov,pin); }
                return false;
            }
            finally { if(release) { if(pin.IsAllocated) pin.Free(); Marshal.FreeHGlobal(ov); CloseHandle(ev); } }
        }
        // A broken kernel driver cannot be forced to acknowledge cancellation by
        // user code. Keep buffers pinned until the event signals; never free OS-
        // owned memory or block the UI/worker indefinitely. Disable this session.
        private void Quarantine(IntPtr ev,IntPtr ov,GCHandle pin)
        {
            CleanupPending=true;
            // Setup is also referenced by a pending control IOCTL.
            GCHandle setupPin=_setupPin; _setupPin=default(GCHandle);
            IntPtr failedHandle=_h; _h=IntPtr.Zero;
            int released=0;
            System.Threading.Timer timer=null;
            timer=new System.Threading.Timer(delegate
            {
                if(WaitForSingleObject(ev,0)!=0 || System.Threading.Interlocked.Exchange(ref released,1)!=0) return;
                if(pin.IsAllocated) pin.Free(); if(setupPin.IsAllocated) setupPin.Free();
                Marshal.FreeHGlobal(ov); CloseHandle(ev); CloseHandle(failedHandle);
                lock(CleanupTimers) CleanupTimers.Remove(timer); timer.Dispose();
            },null,100,100);
            lock(CleanupTimers) CleanupTimers.Add(timer);
        }
        public bool Send(byte cmd) { uint count; return Control(0x40,cmd,null,out count); }
        public byte[] Get(byte cmd,int length)
        {
            if(length<=0 || length>65535) return null;
            byte[] data=new byte[length]; uint count;
            if(!Control(0xC1,cmd,data,out count)) return null;
            if(count!=length) { LastError=13; return null; }
            return data;
        }

        // ---- bulk ---------------------------------------------------
        // Both the data buffer and OVERLAPPED must remain at stable addresses
        // until the OS completes or acknowledges cancellation of the operation.
        private bool Transfer(IntPtr handle, byte[] data, bool write, int timeout, out uint count)
        {
            count = 0;
            if (CleanupPending || handle == IntPtr.Zero || data == null || data.Length == 0) return false;
            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, null);
            if (ev == IntPtr.Zero) return false;
            IntPtr ov = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(OVERLAPPED)));
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(new OVERLAPPED { EventHandle = ev }, ov, false);
                bool ok = write ? WriteFile(handle, pin.AddrOfPinnedObject(), (uint)data.Length, out count, ov)
                                : ReadFile(handle, pin.AddrOfPinnedObject(), (uint)data.Length, out count, ov);
                if (ok) return true;
                if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return false;
                if (WaitForSingleObject(ev, (uint)Math.Max(1, timeout)) == 0)
                    return GetOverlappedResult(handle, ov, out count, false);
                CancelIoEx(handle,ov);
                if(WaitForSingleObject(ev,2000)!=0)
                {
                    CleanupPending=true;
                    GCHandle held=pin; IntPtr heldOv=ov,heldEvent=ev;
                    pin=default(GCHandle); ov=IntPtr.Zero; ev=IntPtr.Zero;
                    if(handle==_bulkIn) _bulkIn=IntPtr.Zero; if(handle==_bulkOut) _bulkOut=IntPtr.Zero;
                    int released=0;
                    System.Threading.Timer cleaner=null;
                    cleaner=new System.Threading.Timer(delegate { if(WaitForSingleObject(heldEvent,0)!=0 || System.Threading.Interlocked.Exchange(ref released,1)!=0) return; held.Free(); Marshal.FreeHGlobal(heldOv); CloseHandle(heldEvent); CloseHandle(handle); lock(CleanupTimers) CleanupTimers.Remove(cleaner); cleaner.Dispose(); },null,100,100);
                    lock(CleanupTimers) CleanupTimers.Add(cleaner);
                }
                LastError=1460;
                return false;
            }
            finally { if(pin.IsAllocated) pin.Free(); if(ov!=IntPtr.Zero) Marshal.FreeHGlobal(ov); if(ev!=IntPtr.Zero) CloseHandle(ev); }
        }
        public bool BulkWrite(byte[] data)
        {
            uint count;
            return Transfer(_bulkOut, data, true, 2000, out count) && count == data.Length;
        }
        public byte[] BulkRead(int length, int timeoutMs)
        {
            if (length <= 0 || length > 200000) return null;
            byte[] data = new byte[length]; uint count;
            if (!Transfer(_bulkIn, data, false, timeoutMs, out count)) return null;
            if (count < length) Array.Resize(ref data, (int)count);
            return data;
        }

        // ---- high level ---------------------------------------------
        /// <summary>One byte device state: 0x00 = ready.</summary>
        public byte State()
        {
            byte[] d = Get(0xFF, 1);
            return (d != null && d.Length >= 1) ? d[0] : (byte)0xFF;
        }

        /// <summary>Poll the state until the device reports ready.</summary>
        public bool WaitReady(int timeoutMs)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed.TotalMilliseconds < timeoutMs)
            {
                if (State() == 0x00) return true;
                System.Threading.Thread.Sleep(50);
            }
            return false;
        }

        /// <summary>cod_ON + read of the SDA block (device identification).</summary>
        public byte[] ReadSda(int length)
        {
            if (!Send(0x00)) return null;
            System.Threading.Thread.Sleep(60);
            return BulkRead(length, 2000);
        }

        /// <summary>Read a length prefixed result block (cod_LAST / cod_CURVE).</summary>
        public byte[] ReadBlock(byte cmd, int attempts, int delayMs)
        {
            for (int i = 0; i < attempts; i++)
            {
                byte[] head = Get(cmd, 4);
                if (head != null && head.Length == 4)
                {
                    int len = (head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3];
                    if (len > 0 && len <= 200000)
                    {
                        byte[] data = BulkRead(len, 3000);
                        if (data != null && data.Length == len) return data;
                    }
                }
                System.Threading.Thread.Sleep(delayMs);
            }
            return null;
        }

        public static string Hex(byte[] b)
        {
            if (b == null) return "";
            StringBuilder sb = new StringBuilder(b.Length * 3);
            for (int i = 0; i < b.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(b[i].ToString("X2"));
            }
            return sb.ToString();
        }

        public static string Ascii(byte[] b)
        {
            if (b == null) return "";
            StringBuilder sb = new StringBuilder(b.Length);
            for (int i = 0; i < b.Length; i++)
                sb.Append(b[i] >= 32 && b[i] < 127 ? (char)b[i] : '.');
            return sb.ToString();
        }
    }
}
