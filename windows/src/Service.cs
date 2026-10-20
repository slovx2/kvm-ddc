// 服务：以 LocalSystem 运行，开机即启动（不需要登录）。
// 看门狗每 60 秒检查一次：控制台会话里没有 agent、agent 退出、或控制台会话变了，就重新拉起 agent。
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;

class SwitchService : ServiceBase
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll")]
    static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attrs, int impLevel, int tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool SetTokenInformation(IntPtr token, int infoClass, ref uint info, int len);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessAsUser(IntPtr token, string app, string cmdLine, IntPtr pAttr, IntPtr tAttr,
        bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    const uint TOKEN_ALL_ACCESS = 0xF01FF;
    const int SecurityImpersonation = 2, TokenPrimary = 1, TokenSessionId = 12;
    const uint CREATE_NO_WINDOW = 0x08000000;
    const uint NO_SESSION = 0xFFFFFFFF;

    Thread watchdog;
    volatile bool stopping;
    Process agent;
    uint agentSession = NO_SESSION;

    public SwitchService() { ServiceName = "ddcswitch"; }

    protected override void OnStart(string[] args)
    {
        Ddc.Log("[service] 启动");
        watchdog = new Thread(Watch) { IsBackground = true, Name = "watchdog" };
        watchdog.Start();
    }

    protected override void OnStop()
    {
        stopping = true;
        KillAgent();
        Ddc.Log("[service] 停止");
    }

    void Watch()
    {
        while (!stopping)
        {
            try
            {
                uint sid = WTSGetActiveConsoleSessionId();
                bool alive = agent != null && !agent.HasExited;
                if (sid != NO_SESSION && (!alive || sid != agentSession))
                {
                    KillAgent();
                    LaunchAgent(sid);
                }
            }
            catch (Exception e) { Ddc.Log("[service] 看门狗异常：" + e.Message); }
            Thread.Sleep(60000);
        }
    }

    void KillAgent()
    {
        if (agent == null) return;
        try { if (!agent.HasExited) agent.Kill(); } catch { }
        agent.Dispose();
        agent = null;
        agentSession = NO_SESSION;
    }

    // 复制服务自身的 SYSTEM 令牌，把会话号改成控制台会话，再在 winsta0\default 上启动 agent
    void LaunchAgent(uint sid)
    {
        IntPtr self, dup;
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ALL_ACCESS, out self))
        { Ddc.Log("[service] OpenProcessToken 失败 err=" + Marshal.GetLastWin32Error()); return; }
        try
        {
            if (!DuplicateTokenEx(self, TOKEN_ALL_ACCESS, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out dup))
            { Ddc.Log("[service] DuplicateTokenEx 失败 err=" + Marshal.GetLastWin32Error()); return; }
            try
            {
                if (!SetTokenInformation(dup, TokenSessionId, ref sid, 4))
                { Ddc.Log("[service] SetTokenInformation 失败 err=" + Marshal.GetLastWin32Error()); return; }
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), lpDesktop = @"winsta0\default" };
                PROCESS_INFORMATION pi;
                if (!CreateProcessAsUser(dup, exe, "\"" + exe + "\" agent", IntPtr.Zero, IntPtr.Zero, false,
                        CREATE_NO_WINDOW, IntPtr.Zero, AppDomain.CurrentDomain.BaseDirectory, ref si, out pi))
                { Ddc.Log("[service] CreateProcessAsUser 失败 err=" + Marshal.GetLastWin32Error()); return; }
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
                agent = Process.GetProcessById(pi.dwProcessId);
                agentSession = sid;
                Ddc.Log(string.Format("[service] 已在会话 {0} 启动 agent pid={1}", sid, pi.dwProcessId));
            }
            finally { CloseHandle(dup); }
        }
        finally { CloseHandle(self); }
    }
}
