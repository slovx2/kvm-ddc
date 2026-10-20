// ddcswitch：通过 DDC/CI（VCP 0x60）读取/切换显示器输入源
// 用法：ddcswitch.exe list | get | set <值>    手动读取/切换
//       ddcswitch.exe service                    由服务管理器启动（SYSTEM），负责拉起 agent
//       ddcswitch.exe agent                      在控制台会话里监听 Win+Ctrl+Shift+S
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;

static class Ddc
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint current, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool SetVCPFeature(IntPtr h, byte code, uint value);

    const byte VCP_INPUT = 0x60;
    // U2725QE 实测：DP(Windows)=15，雷电(Mac)=25
    public const uint MAC_INPUT = 25;

    static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ddcswitch.log");
    static readonly object LogLock = new object();

    public static void Log(string msg)
    {
        string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [pid {1}] {2}",
            DateTime.Now, System.Diagnostics.Process.GetCurrentProcess().Id, msg);
        lock (LogLock)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
        }
    }

    static int Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
        uint target = 0;
        switch (cmd)
        {
            case "service":
                ServiceBase.Run(new SwitchService());
                return 0;
            case "agent":
                return Agent.Run();
            case "list":
            case "get":
                return Run(cmd, 0);
            case "set":
                if (args.Length >= 2 && uint.TryParse(args[1], out target)) return Run(cmd, target);
                break;
        }
        Log("用法：ddcswitch.exe list | get | set <值> | service | agent");
        return 2;
    }

    public static int Run(string cmd, uint target)
    {
        var hMonitors = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, dc, r, d) => { hMonitors.Add(h); return true; }, IntPtr.Zero);
        Log(string.Format("[{0}] 找到 {1} 个逻辑显示器", cmd, hMonitors.Count));

        int ok = 0;
        foreach (IntPtr hMon in hMonitors)
        {
            uint n;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out n) || n == 0) continue;
            var pms = new PHYSICAL_MONITOR[n];
            if (!GetPhysicalMonitorsFromHMONITOR(hMon, n, pms)) continue;
            foreach (var pm in pms)
            {
                uint cur, max;
                bool readOk = GetVCPFeatureAndVCPFeatureReply(pm.hPhysicalMonitor, VCP_INPUT, IntPtr.Zero, out cur, out max);
                Log(string.Format("  {0}: 读取{1} 当前输入源={2}", pm.szPhysicalMonitorDescription,
                    readOk ? "成功" : "失败(err=" + Marshal.GetLastWin32Error() + ")", readOk ? cur.ToString() : "?"));
                if (cmd == "set")
                {
                    bool setOk = SetVCPFeature(pm.hPhysicalMonitor, VCP_INPUT, target);
                    Log(string.Format("  设置输入源={0} {1}", target, setOk ? "成功" : "失败(err=" + Marshal.GetLastWin32Error() + ")"));
                    if (setOk) ok++;
                }
                else if (readOk) ok++;
            }
            DestroyPhysicalMonitors(n, pms);
        }
        return ok > 0 ? 0 : 1;
    }
}
