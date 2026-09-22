using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SelfSwitch
{
    #region Win32 结构体与常量

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }

        public override string ToString()
        {
            return string.Format("({0},{1})-({2},{3})", Left, Top, Right, Bottom);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>Win32 P/Invoke 声明集中处。</summary>
    internal static class Native
    {
        // ShowWindow 命令
        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int SW_SHOW = 5;
        public const int SW_MINIMIZE = 6;
        public const int SW_RESTORE = 9;

        // GetWindow
        public const uint GW_OWNER = 4;

        // GetAncestor
        public const uint GA_ROOT = 2;

        // GetWindowLong
        public const int GWL_EXSTYLE = -20;
        public const int GWL_STYLE = -16;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TRANSPARENT = 0x00000020;

        // 进程访问权限
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        // 显示器
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        // SetWindowPos
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOREDRAW = 0x0008;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_HIDEWINDOW = 0x0080;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint SWP_NOSENDCHANGING = 0x0400;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public static readonly IntPtr HWND_TOP = new IntPtr(0);
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        // GetSystemMetrics
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        // 键盘
        public const int VK_MENU = 0x12;   // Alt
        public const int VK_RETURN = 0x0D;
        public const int VK_F11 = 0x7A;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        public const int ERROR_ACCESS_DENIED = 5;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags,
            StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // ---- SetWinEventHook（前台窗口变化事件）----
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        // ---- 全局快捷键 ----
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // ---- 鼠标位置与按键状态（位置调整模式用）----
        public const int VK_LBUTTON = 0x01;
        public const int VK_ESCAPE = 0x1B;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8) return GetWindowLongPtr64(hWnd, nIndex);
            return new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8) return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }
    }

    #endregion

    /// <summary>
    /// "x,y,w,h" 形式的窗口区域描述解析（v1.7）。
    /// 每个字段可以是像素整数，也可以是百分比（例：50%）——百分比相对窗口所在显示器矩形计算。
    /// 例："0,0,50%,100%" = 左半屏；"50%,0,50%,100%" = 右半屏；"0,0,1280,720" = 固定 1280×720。
    /// </summary>
    public static class RectSpec
    {
        public const string DefaultChrome = "0,0,50%,100%";
        public const string DefaultTarget = "50%,0,50%,100%";

        /// <summary>解析区域描述；monitor 传窗口所在显示器的完整矩形。</summary>
        public static bool TryParse(string spec, RECT monitor, out RECT result)
        {
            result = new RECT();
            if (string.IsNullOrWhiteSpace(spec)) return false;

            string[] parts = spec.Split(new[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return false;

            int monW = monitor.Width;
            int monH = monitor.Height;
            if (monW <= 0) monW = 1920;
            if (monH <= 0) monH = 1080;

            int x = 0, y = 0, w = 0, h = 0;
            if (!ParseComponent(parts[0], monitor.Left, monW, out x)) return false;   // x
            if (!ParseComponent(parts[1], monitor.Top, monH, out y)) return false;    // y
            if (!ParseComponent(parts[2], 0, monW, out w)) return false;              // 宽
            if (!ParseComponent(parts[3], 0, monH, out h)) return false;              // 高

            if (w < 1) w = 1;
            if (h < 1) h = 1;

            result.Left = x;
            result.Top = y;
            result.Right = x + w;
            result.Bottom = y + h;
            return true;
        }

        /// <summary>校验（用一个假想的 1920×1080 显示器试解析）。</summary>
        public static bool IsValid(string spec)
        {
            RECT dummy = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            RECT r;
            return TryParse(spec, dummy, out r);
        }

        /// <summary>单个分量：带 % 视为百分比（相对 origin+总量），否则按像素。</summary>
        private static bool ParseComponent(string text, int origin, int total, out int value)
        {
            value = 0;
            string p = (text ?? string.Empty).Trim();
            if (p.Length == 0) return false;

            if (p.EndsWith("%"))
            {
                double pct;
                if (!double.TryParse(p.Substring(0, p.Length - 1).Trim(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out pct)) return false;
                if (pct < -500 || pct > 500) return false;
                value = origin + (int)Math.Round(total * pct / 100.0);
                return true;
            }

            int px;
            if (!int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out px)) return false;
            if (px < -32000 || px > 32000) return false;
            value = px;
            return true;
        }
    }

    /// <summary>路径规范化比较工具。</summary>
    public static class PathHelper
    {
        /// <summary>统一斜杠、去掉引号与结尾反斜杠、转小写。</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string p = path.Trim().Trim('"').Replace('/', '\\');
            try
            {
                p = Path.GetFullPath(p);
            }
            catch
            {
                // 路径非法时保留原样继续比较
            }
            p = p.TrimEnd('\\');
            return p.ToLowerInvariant();
        }

        /// <summary>忽略大小写、统一斜杠后的路径相等判断。</summary>
        public static bool Equals(string a, string b)
        {
            string na = Normalize(a);
            string nb = Normalize(b);
            if (na.Length == 0 || nb.Length == 0) return false;
            return string.Equals(na, nb, StringComparison.Ordinal);
        }
    }

    /// <summary>Win32 常用操作封装。</summary>
    internal static class Win32
    {
        // 同一 PID 的取路径失败，1 分钟内只写一条 WARN：
        // ForegroundWatcher 会在每次前台句柄变化时取一次进程路径，
        // 若前台是受保护进程（部分安全软件 / 反作弊），否则可能高频刷 WARN。
        private static readonly Dictionary<uint, DateTime> WarnHistory = new Dictionary<uint, DateTime>();
        private static readonly object WarnLock = new object();

        private static void WarnOnce(uint pid, string message)
        {
            try
            {
                lock (WarnLock)
                {
                    DateTime last;
                    if (WarnHistory.TryGetValue(pid, out last) &&
                        (DateTime.Now - last).TotalSeconds < 60)
                    {
                        return;
                    }
                    WarnHistory[pid] = DateTime.Now;
                    if (WarnHistory.Count > 256) WarnHistory.Clear();
                }
            }
            catch { }
            Logger.Warn(message);
        }

        // ---------------- 进程路径 ----------------

        /// <summary>用 QueryFullProcessImageName 取进程完整路径，失败返回 null。</summary>
        public static string GetProcessPathByPid(uint pid)
        {
            if (pid == 0) return null;
            IntPtr h = IntPtr.Zero;
            try
            {
                h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero)
                {
                    WarnOnce(pid, "OpenProcess 失败, pid=" + pid + ", error=" +
                                  Marshal.GetLastWin32Error());
                    return null;
                }
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (Native.QueryFullProcessImageName(h, 0, sb, ref size))
                {
                    return sb.ToString(0, size);
                }
                WarnOnce(pid, "QueryFullProcessImageName 失败, pid=" + pid + ", error=" +
                               Marshal.GetLastWin32Error());
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error("取进程路径异常, pid=" + pid, ex);
                return null;
            }
            finally
            {
                if (h != IntPtr.Zero) Native.CloseHandle(h);
            }
        }

        /// <summary>取窗口所属进程的完整路径，失败返回 null。</summary>
        public static string GetProcessPathByWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return null;
            uint pid;
            Native.GetWindowThreadProcessId(hWnd, out pid);
            return GetProcessPathByPid(pid);
        }

        /// <summary>该进程是否以更高权限（管理员）运行且当前无法访问。</summary>
        public static bool IsProcessElevated(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return false;
            string name;
            try { name = Path.GetFileNameWithoutExtension(exePath); }
            catch { return false; }
            if (string.IsNullOrEmpty(name)) return false;

            Process[] list;
            try { list = Process.GetProcessesByName(name); }
            catch { return false; }

            bool elevated = false;
            foreach (Process p in list)
            {
                IntPtr h = IntPtr.Zero;
                try
                {
                    h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)p.Id);
                    if (h == IntPtr.Zero)
                    {
                        if (Marshal.GetLastWin32Error() == Native.ERROR_ACCESS_DENIED) elevated = true;
                    }
                    else
                    {
                        // 能打开：再核对一次路径，避免同名进程误判
                        var sb = new StringBuilder(1024);
                        int size = sb.Capacity;
                        if (Native.QueryFullProcessImageName(h, 0, sb, ref size))
                        {
                            if (!PathHelper.Equals(sb.ToString(0, size), exePath))
                            {
                                Native.CloseHandle(h);
                                h = IntPtr.Zero;
                                continue;
                            }
                        }
                    }
                }
                catch { }
                finally
                {
                    if (h != IntPtr.Zero) Native.CloseHandle(h);
                    p.Dispose();
                }
            }
            return elevated;
        }

        /// <summary>当前进程是否以管理员权限运行。</summary>
        public static bool IsSelfElevated()
        {
            try
            {
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var pr = new System.Security.Principal.WindowsPrincipal(id);
                    return pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("检测自身权限失败", ex);
                return false;
            }
        }

        // ---------------- 全屏 / 前台 ----------------

        /// <summary>窗口所在显示器的完整矩形（单屏环境即主屏 0,0 - W,H）。</summary>
        public static RECT GetMonitorRect(IntPtr hWnd)
        {
            var mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            IntPtr mon = Native.MonitorFromWindow(hWnd, Native.MONITOR_DEFAULTTONEAREST);
            if (mon != IntPtr.Zero && Native.GetMonitorInfo(mon, ref mi))
            {
                return mi.rcMonitor;
            }
            return new RECT
            {
                Left = 0,
                Top = 0,
                Right = Native.GetSystemMetrics(Native.SM_CXSCREEN),
                Bottom = Native.GetSystemMetrics(Native.SM_CYSCREEN)
            };
        }

        /// <summary>主屏尺寸（像素）。</summary>
        public static RECT GetPrimaryScreenRect()
        {
            return new RECT
            {
                Left = 0,
                Top = 0,
                Right = Native.GetSystemMetrics(Native.SM_CXSCREEN),
                Bottom = Native.GetSystemMetrics(Native.SM_CYSCREEN)
            };
        }

        /// <summary>窗口矩形是否已经覆盖它所在显示器的整个屏幕（容忍 4px 误差）。</summary>
        public static bool IsCoveringScreen(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return false;
            RECT r;
            if (!Native.GetWindowRect(hWnd, out r)) return false;
            RECT m = GetMonitorRect(hWnd);
            const int tol = 4;
            return Math.Abs(r.Left - m.Left) <= tol
                && Math.Abs(r.Top - m.Top) <= tol
                && Math.Abs(r.Right - m.Right) <= tol
                && Math.Abs(r.Bottom - m.Bottom) <= tol;
        }

        /// <summary>
        /// 把"窗口矩形 vs 显示器矩形"的实测值写进 app.log。
        /// F11 判断是否准确靠这一行日志验证（要求实测时看这个）。
        /// </summary>
        public static void LogCoveringCompare(IntPtr hWnd, string tag)
        {
            try
            {
                RECT r;
                if (!Native.GetWindowRect(hWnd, out r))
                {
                    Logger.Warn(tag + ": GetWindowRect 失败, error=" + Marshal.GetLastWin32Error());
                    return;
                }
                RECT m = GetMonitorRect(hWnd);
                Logger.Info(string.Format(
                    "{0}: 窗口矩形={1} 显示器矩形={2} 覆盖={3} 最大化={4}",
                    tag, r, m, IsCoveringScreen(hWnd), Native.IsZoomed(hWnd)));
            }
            catch (Exception ex)
            {
                Logger.Error(tag + ": 记录全屏比对失败", ex);
            }
        }

        /// <summary>读窗口标题。</summary>
        public static string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                int len = Native.GetWindowTextLength(hWnd);
                if (len <= 0) return string.Empty;
                var sb = new StringBuilder(len + 2);
                int n = Native.GetWindowText(hWnd, sb, sb.Capacity);
                return sb.ToString(0, n);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>窗口是否处于最大化状态。</summary>
        public static bool IsMaximized(IntPtr hWnd)
        {
            return hWnd != IntPtr.Zero && Native.IsZoomed(hWnd);
        }

        /// <summary>读窗口类名。</summary>
        public static string GetWindowClassName(IntPtr hWnd)
        {
            try
            {
                if (hWnd == IntPtr.Zero) return string.Empty;
                var sb = new StringBuilder(256);
                int n = Native.GetClassName(hWnd, sb, sb.Capacity);
                return n > 0 ? sb.ToString(0, n) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Win32 方式全屏：SetWindowPos 铺满显示器并置于最顶层（覆盖任务栏）。
        /// </summary>
        public static bool MakeFullscreen(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return false;
            if (Native.IsIconic(hWnd)) Native.ShowWindow(hWnd, Native.SW_RESTORE);

            RECT m = GetMonitorRect(hWnd);
            bool ok = Native.SetWindowPos(
                hWnd,
                Native.HWND_TOPMOST,
                m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top,
                Native.SWP_SHOWWINDOW | Native.SWP_NOOWNERZORDER);

            if (!ok)
            {
                Logger.Warn("SetWindowPos 失败, hwnd=" + hWnd + ", error=" + Marshal.GetLastWin32Error());
            }
            return ok;
        }

        /// <summary>取消置顶（留在后台时用）。</summary>
        public static void ClearTopmost(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return;
            Native.SetWindowPos(hWnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        /// <summary>把自己（悬浮按钮）重新压到最顶层，且不抢焦点。</summary>
        public static void EnsureTopmost(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return;
            Native.SetWindowPos(hWnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE |
                Native.SWP_SHOWWINDOW | Native.SWP_NOOWNERZORDER);
        }

        /// <summary>
        /// 强制把窗口切到前台。SetForegroundWindow 会被系统前台锁定策略拦截，
        /// 这里依次用 AttachThreadInput 和 Alt 键技巧兜底。
        /// </summary>
        public static bool ForceForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return false;
            if (Native.GetForegroundWindow() == hWnd) return true;

            uint ignored;
            IntPtr fg = Native.GetForegroundWindow();
            uint fgThread = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, out ignored);
            uint curThread = Native.GetCurrentThreadId();
            bool attached = false;

            try
            {
                if (fgThread != 0 && fgThread != curThread)
                {
                    attached = Native.AttachThreadInput(curThread, fgThread, true);
                }

                Native.BringWindowToTop(hWnd);
                bool ok = Native.SetForegroundWindow(hWnd);

                if (!ok || Native.GetForegroundWindow() != hWnd)
                {
                    // Alt 键按下/抬起，绕过前台锁定
                    Native.keybd_event((byte)Native.VK_MENU, 0, 0, UIntPtr.Zero);
                    Native.keybd_event((byte)Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    ok = Native.SetForegroundWindow(hWnd);
                }

                bool result = ok || Native.GetForegroundWindow() == hWnd;
                if (!result) Logger.Warn("SetForegroundWindow 失败, hwnd=" + hWnd);
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error("强制前台异常, hwnd=" + hWnd, ex);
                return false;
            }
            finally
            {
                if (attached) Native.AttachThreadInput(curThread, fgThread, false);
            }
        }

        /// <summary>发送按键（真实输入，要求目标窗口已是前台）。</summary>
        public static void SendKey(int vk, bool withAlt)
        {
            try
            {
                if (withAlt) Native.keybd_event((byte)Native.VK_MENU, 0, 0, UIntPtr.Zero);
                Native.keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
                System.Threading.Thread.Sleep(30);
                Native.keybd_event((byte)vk, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                if (withAlt) Native.keybd_event((byte)Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Logger.Info("已发送按键 vk=0x" + vk.ToString("X2") + (withAlt ? " (Alt组合)" : ""));
            }
            catch (Exception ex)
            {
                Logger.Error("发送按键失败 vk=0x" + vk.ToString("X2"), ex);
            }
        }

        /// <summary>给窗口附加 / 移除 WS_EX_NOACTIVATE 扩展样式。</summary>
        public static void SetNoActivate(IntPtr hWnd, bool enable)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                IntPtr ex = Native.GetWindowLongPtr(hWnd, Native.GWL_EXSTYLE);
                long v = ex.ToInt64();
                if (enable) v |= Native.WS_EX_NOACTIVATE;
                else v &= ~(long)Native.WS_EX_NOACTIVATE;
                Native.SetWindowLongPtr(hWnd, Native.GWL_EXSTYLE, new IntPtr(v));
            }
            catch (Exception ex2)
            {
                Logger.Error("设置 WS_EX_NOACTIVATE 失败", ex2);
            }
        }

        /// <summary>窗口是否为工具窗口（跳过这类窗口）。</summary>
        public static bool IsToolWindow(IntPtr hWnd)
        {
            long ex = Native.GetWindowLongPtr(hWnd, Native.GWL_EXSTYLE).ToInt64();
            return (ex & Native.WS_EX_TOOLWINDOW) != 0;
        }

        /// <summary>当前窗口是否带 WS_EX_NOACTIVATE（点击不抢前台焦点）。</summary>
        public static bool IsNoActivate(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            long ex = Native.GetWindowLongPtr(hWnd, Native.GWL_EXSTYLE).ToInt64();
            return (ex & Native.WS_EX_NOACTIVATE) != 0;
        }
    }
}
