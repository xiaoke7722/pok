using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace SelfSwitch
{
    /// <summary>当前前台窗口属于哪一类</summary>
    public enum ForegroundKind
    {
        Other = 0,   // 既不是 Chrome 也不是目标 EXE
        Chrome = 1,
        Target = 2
    }

    /// <summary>
    /// 实时跟随前台窗口变化，给悬浮按钮提供"现在该显示什么文字"的依据。
    ///
    /// 两个机制叠加，既即时又不漏：
    ///   1. SetWinEventHook(EVENT_SYSTEM_FOREGROUND) 事件驱动 —— 切换窗口立刻响应；
    ///   2. 1 秒定时器兜底轮询 —— 防止极少数情况下事件丢失。
    ///
    /// 开销控制：只有"前台窗口句柄"发生变化时才去解析进程完整路径（一次 OpenProcess），
    /// 句柄没变就直接返回，所以轮询几乎不耗资源。
    /// </summary>
    internal static class ForegroundWatcher
    {
        /// <summary>前台分类发生变化时触发（只在"变化"时触发，不重复触发）。</summary>
        public static event Action<ForegroundKind> StateChanged;

        public static ForegroundKind Current { get; private set; }

        /// <summary>最近一次"非本工具"的前台窗口路径（配置界面「取当前窗口」按钮用它）。</summary>
        public static string LastForeignPath { get; private set; }

        /// <summary>最近一次"非本工具"的前台窗口标题。</summary>
        public static string LastForeignTitle { get; private set; }

        /// <summary>当前前台窗口的进程完整路径（未做任何过滤）。</summary>
        public static string CurrentForegroundPath { get; private set; }

        /// <summary>
        /// 系统"壳窗口"：通知横幅、任务栏、操作中心、托盘旋出窗。
        /// 它们抢前台不代表用户换了工作程序，不应该影响按钮状态与方向判断。
        /// </summary>
        private static readonly string[] ShellWindowClasses =
        {
            "Shell_TrayWnd",                        // 主任务栏
            "Shell_SecondaryTrayWnd",               // 副屏任务栏
            "NotifyIconOverflowWindow",             // 托盘溢出面板
            "Windows.UI.Core.CoreWindow",           // 通知横幅 / 操作中心
            "XamlExplorerHostIslandWindow",         // Win11 弹窗宿主
            "TaskListThumbnailWnd"                  // 任务栏缩略图浮窗
        };

        /// <summary>该窗口是不是系统壳窗口。</summary>
        public static bool IsShellNoise(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                string cls = Win32.GetWindowClassName(hwnd);
                if (string.IsNullOrEmpty(cls)) return false;
                for (int i = 0; i < ShellWindowClasses.Length; i++)
                {
                    if (string.Equals(cls, ShellWindowClasses[i], StringComparison.Ordinal)) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>这个进程路径是不是本工具自己（配置窗口 / 悬浮按钮）。</summary>
        public static bool IsSelfProcess(string path)
        {
            return !string.IsNullOrEmpty(path) && PathHelper.Equals(path, AppPaths.ExePath);
        }

        private static IntPtr _hook = IntPtr.Zero;
        private static Native.WinEventDelegate _hookProc;   // 必须保持强引用，否则被 GC 回收会崩溃
        private static DispatcherTimer _timer;
        private static IntPtr _lastHwnd = IntPtr.Zero;
        private static bool _started;

        public static void Start()
        {
            if (_started) return;
            _started = true;

            // 1) 前台窗口事件钩子（out-of-context，不注入任何进程）
            try
            {
                _hookProc = OnWinEvent;
                _hook = Native.SetWinEventHook(
                    Native.EVENT_SYSTEM_FOREGROUND,
                    Native.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _hookProc,
                    0, 0,
                    Native.WINEVENT_OUTOFCONTEXT);

                if (_hook == IntPtr.Zero)
                {
                    Logger.Warn("SetWinEventHook 失败(改用定时轮询), error=" + Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                Logger.Error("安装前台窗口事件钩子失败", ex);
            }

            // 2) 兜底轮询
            try
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background);
                _timer.Interval = TimeSpan.FromSeconds(1);
                _timer.Tick += delegate { Refresh(); };
                _timer.Start();
            }
            catch (Exception ex)
            {
                Logger.Error("启动前台窗口轮询失败", ex);
            }

            Refresh();
        }

        public static void Stop()
        {
            try
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer = null;
                }
            }
            catch { }

            try
            {
                if (_hook != IntPtr.Zero)
                {
                    Native.UnhookWinEvent(_hook);
                    _hook = IntPtr.Zero;
                }
                _hookProc = null;
            }
            catch { }

            _started = false;
        }

        /// <summary>配置改过（路径变了）时强制重新判定一次。</summary>
        public static void Invalidate()
        {
            _lastHwnd = IntPtr.Zero;
            Refresh();
        }

        /// <summary>重新检测当前前台窗口，分类变化时触发 StateChanged。</summary>
        public static void Refresh()
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                IntPtr root = fg == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(fg, Native.GA_ROOT);

                if (root == _lastHwnd) return;   // 句柄没变 → 不做任何解析，直接返回
                _lastHwnd = root;

                string path = root == IntPtr.Zero ? null : Win32.GetProcessPathByWindow(root);
                CurrentForegroundPath = path;

                // 记录最近一次"非本工具"的前台窗口，供配置界面「取当前窗口」按钮抓路径
                if (!string.IsNullOrEmpty(path) && !PathHelper.Equals(path, AppPaths.ExePath))
                {
                    LastForeignPath = path;
                    LastForeignTitle = Win32.GetWindowTitle(root);
                }

                // 本工具自己的窗口（配置窗口 / 悬浮按钮）成为前台时：
                // 不代表用户换了工作程序，而且**绝不能因此把按钮藏起来**——
                // 那会导致"点按钮的瞬间按钮消失、点击被吃掉"（现场日志实测过）。
                if (IsSelfProcess(path))
                {
                    Logger.Info("前台是本工具自身窗口，保持按钮状态不变 | " +
                                WindowFinder.DescribeWindow(root));
                    return;
                }

                // 系统壳窗口（通知横幅 / 任务栏 / 操作中心）同样不改变按钮状态
                AppConfig cfg0 = App.Config;
                if (cfg0 != null && cfg0.IgnoreShellWindows && IsShellNoise(root))
                {
                    Logger.Info("前台是系统壳窗口（通知/任务栏），保持按钮状态不变 | " +
                                WindowFinder.DescribeWindow(root));
                    return;
                }

                ForegroundKind kind = Classify(path);
                if (kind == Current) return;

                Current = kind;
                Logger.Info("前台窗口类别变化: " + kind + " | " + WindowFinder.DescribeWindow(root));

                Action<ForegroundKind> handler = StateChanged;
                if (handler != null) handler(kind);
            }
            catch (Exception ex)
            {
                Logger.Error("前台窗口检测失败", ex);
            }
        }

        private static ForegroundKind Classify(string path)
        {
            if (string.IsNullOrEmpty(path)) return ForegroundKind.Other;

            AppConfig cfg = App.Config;
            if (cfg == null) return ForegroundKind.Other;

            if (PathHelper.Equals(path, cfg.ChromePath)) return ForegroundKind.Chrome;
            if (PathHelper.Equals(path, cfg.TargetExePath)) return ForegroundKind.Target;
            return ForegroundKind.Other;
        }

        private static void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (eventType != Native.EVENT_SYSTEM_FOREGROUND) return;

                System.Windows.Application app = System.Windows.Application.Current;
                if (app == null)
                {
                    Refresh();
                    return;
                }
                // 回调在装钩子的线程（UI 线程）上进来，这里再排一次队，保证在 UI 线程执行
                app.Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background);
            }
            catch
            {
                // 钩子回调里绝不能抛异常
            }
        }
    }
}
