// agent：由服务以 SYSTEM 身份拉起到控制台会话，
// 分别在 Default（正常桌面）和 Winlogon（登录/锁屏/UAC 安全桌面）上挂低级键盘钩子，
// 检测到 Win+Ctrl+Shift+S 就把显示器切到 Mac。只判断组合键，不记录任何按键内容。
using System;
using System.Runtime.InteropServices;
using System.Threading;

static class Agent
{
    public static int Run()
    {
        bool created;
        using (var mutex = new Mutex(true, @"Local\ddcswitch-agent", out created))
        {
            if (!created) { Ddc.Log("[agent] 本会话已有 agent，退出"); return 0; }
            Ddc.Log("[agent] 启动");
            var threads = new[] { HookThread.Start("Default"), HookThread.Start("Winlogon") };
            foreach (var t in threads) t.Join();
            Ddc.Log("[agent] 所有钩子线程已退出");
            return 1;
        }
    }
}

class HookThread
{
    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenDesktop(string name, uint flags, bool inherit, uint access);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string name);

    const int WH_KEYBOARD_LL = 13;
    const uint GENERIC_ALL = 0x10000000;
    const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    const uint VK_S = 0x53, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    readonly string desktop;
    LowLevelKeyboardProc proc;   // 保持引用，防止委托被 GC
    IntPtr hook;
    bool win, ctrl, shift, swallowS;

    HookThread(string desktop) { this.desktop = desktop; }

    public static Thread Start(string desktop)
    {
        var ht = new HookThread(desktop);
        var t = new Thread(ht.Loop) { IsBackground = true, Name = "hook-" + desktop };
        t.Start();
        return t;
    }

    void Loop()
    {
        IntPtr hDesk = OpenDesktop(desktop, 0, false, GENERIC_ALL);
        if (hDesk == IntPtr.Zero || !SetThreadDesktop(hDesk))
        {
            Ddc.Log(string.Format("[agent] 附加到桌面 {0} 失败(err={1})", desktop, Marshal.GetLastWin32Error()));
            return;
        }
        proc = OnKey;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
        {
            Ddc.Log(string.Format("[agent] 桌面 {0} 挂钩子失败(err={1})", desktop, Marshal.GetLastWin32Error()));
            return;
        }
        Ddc.Log(string.Format("[agent] 桌面 {0} 已监听 Win+Ctrl+Shift+S", desktop));
        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
    }

    IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            int m = wParam.ToInt32();
            bool down = m == WM_KEYDOWN || m == WM_SYSKEYDOWN;
            bool up = m == WM_KEYUP || m == WM_SYSKEYUP;
            uint vk = k.vkCode;
            if (vk == VK_LWIN || vk == VK_RWIN) { if (down) win = true; else if (up) win = false; }
            else if (vk == VK_LCONTROL || vk == VK_RCONTROL) { if (down) ctrl = true; else if (up) ctrl = false; }
            else if (vk == VK_LSHIFT || vk == VK_RSHIFT) { if (down) shift = true; else if (up) shift = false; }
            else if (vk == VK_S)
            {
                if (down && win && ctrl && shift)
                {
                    // 切走后键盘会从本机断开，收不到修饰键的抬起，所以触发后直接清空状态
                    win = ctrl = shift = false;
                    swallowS = true;
                    Ddc.Log(string.Format("[agent] 桌面 {0} 触发，切到 Mac", desktop));
                    ThreadPool.QueueUserWorkItem(_ => Ddc.Run("set", Ddc.MAC_INPUT));
                    return (IntPtr)1;
                }
                if (up && swallowS) { swallowS = false; return (IntPtr)1; }
            }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }
}
