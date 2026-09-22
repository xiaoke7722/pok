using System;
using System.Threading;

namespace SelfSwitch
{
    /// <summary>
    /// 核心切换逻辑：在 Chrome 与目标 EXE 的已打开窗口之间来回切换。
    /// 不启动、不关闭任何进程。
    /// </summary>
    internal static class ToggleService
    {
        private static readonly object Sync = new object();

        /// <summary>两次切换之间的最小间隔（毫秒），用于丢弃连点。</summary>
        private const int MinIntervalMs = 400;

        /// <summary>上次切换完成时的 Environment.TickCount。</summary>
        private static int _lastDoneTick;

        /// <summary>
        /// 上一次"有意义"的前台程序路径（Chrome 或目标 EXE）。
        /// 用途：点击悬浮按钮时前台如果变成了小工具自己或其它程序，用这个值兜底判断切换方向。
        /// </summary>
        private static string _lastForegroundPath;

        public static void Toggle()
        {
            // 真正生效的防重入：时间节流。
            // 为什么不能只靠 Monitor：WPF 鼠标消息在 UI 线程忙碌时是**排队等待**而不是被丢弃的，
            // 而 Monitor 对同一线程本身可重入 → 连点会逐条排队执行（测试报告问题 D 实测证实）。
            // 这里直接按时间间隔丢弃，行为与文档描述一致。
            int now = Environment.TickCount;
            if (_lastDoneTick != 0)
            {
                int elapsed = unchecked(now - _lastDoneTick);
                if (elapsed >= 0 && elapsed < MinIntervalMs)
                {
                    Logger.Warn("距离上次切换完成仅 " + elapsed + "ms（节流 " + MinIntervalMs +
                                "ms），忽略本次点击");
                    return;
                }
            }

            if (!Monitor.TryEnter(Sync, 0))
            {
                Logger.Warn("上一次切换尚未结束，忽略本次点击");
                return;
            }
            bool switched = false;
            try
            {
                switched = DoToggle();
            }
            catch (Exception ex)
            {
                Logger.Error("切换失败", ex);
                TrayHelper.ShowError("切换失败", ex.Message);
            }
            finally
            {
                // 只有"真的切了"才计入节流窗口：
                // 找不到窗口 / 严格模式忽略 / 未配置路径 等失败场景不计，
                // 否则用户发现问题后重试会被节流无谓地拦掉（v1.5 的 400ms 节流曾经这样雪上加霜）。
                if (switched) _lastDoneTick = Environment.TickCount;
                Monitor.Exit(Sync);
            }
        }

        private static bool DoToggle()
        {
            AppConfig cfg = App.Config;

            if (string.IsNullOrWhiteSpace(cfg.ChromePath) && string.IsNullOrWhiteSpace(cfg.TargetExePath))
            {
                TrayHelper.ShowError("未配置路径", "请先在配置界面填写 Chrome 与目标 EXE 的完整路径。");
                return false;
            }

            // 1) 查两个窗口（只查已打开的，不启动）
            IntPtr chromeHwnd = WindowFinder.FindMainWindow(cfg.ChromePath);
            IntPtr targetHwnd = WindowFinder.FindMainWindow(cfg.TargetExePath, cfg.WindowTitleKeyword);

            Logger.Info("查找结果: chrome=" + WindowFinder.DescribeWindow(chromeHwnd) +
                        " | target=" + WindowFinder.DescribeWindow(targetHwnd));

            // 2) 取当前前台窗口（取根窗口，避免拿到子窗口）
            IntPtr fg = Native.GetForegroundWindow();
            IntPtr fgRoot = fg == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(fg, Native.GA_ROOT);
            string fgPath = fgRoot == IntPtr.Zero ? null : Win32.GetProcessPathByWindow(fgRoot);

            bool fgIsChrome = PathHelper.Equals(fgPath, cfg.ChromePath);
            bool fgIsTarget = PathHelper.Equals(fgPath, cfg.TargetExePath);

            // 前台是本工具自己（点了配置窗口/托盘菜单）或系统壳窗口（通知横幅 / 任务栏 / 操作中心）时，
            // 都不代表用户换了工作程序：改用 ForegroundWatcher 记录的"最近一次真实程序窗口"来判定方向。
            // 否则点完配置窗口再按快捷键 / 点按钮，会被严格模式误判成"其它程序"而静默忽略。
            bool fgIsSelf = PathHelper.Equals(fgPath, AppPaths.ExePath);
            if (!fgIsChrome && !fgIsTarget &&
                (fgIsSelf || (cfg.IgnoreShellWindows && ForegroundWatcher.IsShellNoise(fgRoot))))
            {
                string effective = ForegroundWatcher.LastForeignPath;
                Logger.Info("前台是" + (fgIsSelf ? "本工具自身" : "系统壳窗口") +
                            "，改用最近一次真实程序窗口判定: " + effective);
                if (PathHelper.Equals(effective, cfg.ChromePath)) fgIsChrome = true;
                else if (PathHelper.Equals(effective, cfg.TargetExePath)) fgIsTarget = true;
            }

            // 严格模式（默认开启）：只在前台是 Chrome 或目标 EXE 时才切换；
            // 其余情况静默忽略——不切换、不提示，只在 app.log 留一行记录。
            if (cfg.StrictForegroundCheck && !fgIsChrome && !fgIsTarget)
            {
                Logger.Info("严格模式：当前前台既不是 Chrome 也不是目标 EXE（当前=" + fgPath +
                            "），静默忽略本次点击");
                return false;
            }

            string reason;

            if (fgIsChrome)
            {
                reason = "前台是 Chrome";
            }
            else if (fgIsTarget)
            {
                reason = "前台是目标 EXE";
            }
            else
            {
                // 前台既不是 Chrome 也不是目标 EXE（例如用户点了任务栏、或某些情况下点到了悬浮按钮）
                // → 用内部记录的"上次状态"决定下一个目标，没有任何记录时默认切到 Chrome。
                if (!string.IsNullOrEmpty(_lastForegroundPath))
                {
                    if (PathHelper.Equals(_lastForegroundPath, cfg.ChromePath))
                    {
                        fgIsChrome = true;
                        reason = "前台是其它程序(" + fgPath + ")，按上次状态(Chrome)判断 → 切目标 EXE";
                    }
                    else if (PathHelper.Equals(_lastForegroundPath, cfg.TargetExePath))
                    {
                        fgIsTarget = true;
                        reason = "前台是其它程序(" + fgPath + ")，按上次状态(目标 EXE)判断 → 切 Chrome";
                    }
                    else
                    {
                        reason = "前台是其它程序(" + fgPath + ")，无有效记录，默认切 Chrome";
                    }
                }
                else
                {
                    reason = "前台是其它程序(" + fgPath + ")，无历史记录，默认切 Chrome";
                }
            }

            // 3) 决定切谁
            IntPtr toHwnd;
            bool toIsChrome;

            if (fgIsChrome)
            {
                toHwnd = targetHwnd;
                toIsChrome = false;
            }
            else
            {
                // 前台是目标 EXE 或 "其它" → 切 Chrome
                toHwnd = chromeHwnd;
                toIsChrome = true;
            }

            Logger.Info("切换方向: " + reason + " → 目标=" + (toIsChrome ? "Chrome" : "目标 EXE"));

            if (toHwnd == IntPtr.Zero)
            {
                // 自动诊断：找"同名但路径不同"的进程窗口，直接把正确路径报出来
                // （最常见的原因就是配置路径写错了一层目录，例如 D:\Doubao\Doubao.exe 实际在 D:\Doubao\app\）
                string wanted = toIsChrome ? cfg.ChromePath : cfg.TargetExePath;
                string similar = WindowFinder.FindSimilarWindowHint(wanted);
                string similarText = string.Empty;

                if (!string.IsNullOrEmpty(similar))
                {
                    Logger.Warn("疑似路径写错：配置=" + wanted +
                                " ，实际找到同名窗口在 " + similar.Replace("\r\n", " | "));
                    string firstLine = similar.Split('\n')[0].Trim();
                    similarText = "\r\n\r\n【疑似路径写错】检测到同名的程序窗口在：\r\n" + firstLine +
                                  "\r\n请按这个路径修正配置。";
                }

                if (toIsChrome)
                {
                    TrayHelper.ShowWarning("未找到 Chrome 窗口",
                        "没有找到已打开的 Chrome 主窗口。\r\n请先打开 Chrome（本工具不会代为启动）。\r\n路径：" +
                        cfg.ChromePath + similarText);
                }
                else
                {
                    string extra = string.IsNullOrEmpty(cfg.WindowTitleKeyword)
                        ? string.Empty
                        : "\r\n标题关键字：" + cfg.WindowTitleKeyword;
                    TrayHelper.ShowWarning("未找到目标 EXE 窗口",
                        "没有找到已打开的目标程序窗口。\r\n路径：" + cfg.TargetExePath + extra + similarText);
                }
                return false;
            }

            // 4) 处理"当前窗口"
            if (fgRoot != IntPtr.Zero && fgRoot != toHwnd && Native.IsWindow(fgRoot))
            {
                if (cfg.Previous == PreviousWindowAction.Minimize)
                {
                    Win32.ClearTopmost(fgRoot);
                    Native.ShowWindow(fgRoot, Native.SW_MINIMIZE);
                    Logger.Info("已最小化当前窗口: " + WindowFinder.DescribeWindow(fgRoot));
                }
                else
                {
                    Win32.ClearTopmost(fgRoot);
                    Logger.Info("当前窗口留在后台（已取消置顶，未最小化）: " +
                                WindowFinder.DescribeWindow(fgRoot));
                }
            }

            // 5) 激活目标窗口（含各自的全屏策略）
            Activate(toHwnd, toIsChrome, cfg);

            // 6) 记录本次前台程序（供下次兜底判断）
            _lastForegroundPath = toIsChrome ? cfg.ChromePath : cfg.TargetExePath;
            return true;
        }

        // ==================== 激活 / 显示方式策略（v1.7 统一） ====================

        private static void Activate(IntPtr hwnd, bool isChrome, AppConfig cfg)
        {
            // 最小化的窗口先恢复
            if (Native.IsIconic(hwnd))
            {
                Native.ShowWindow(hwnd, Native.SW_RESTORE);
                Thread.Sleep(60);
            }

            WindowDisplayMode mode = isChrome ? cfg.ChromeMode : cfg.TargetMode;
            string rectSpec = isChrome ? cfg.ChromeRect : cfg.TargetRect;
            string who = isChrome ? "Chrome" : "目标 EXE";

            // 非置顶类模式先取消置顶：
            // 否则上一次用"覆盖任务栏"模式留下的 TOPMOST 会一直压着别的窗口
            if (mode == WindowDisplayMode.Maximize || mode == WindowDisplayMode.Windowed ||
                mode == WindowDisplayMode.CustomRect || mode == WindowDisplayMode.KeepCurrent)
            {
                Win32.ClearTopmost(hwnd);
            }

            switch (mode)
            {
                case WindowDisplayMode.SendF11:
                    if (!AlreadyCovering(hwnd, who, "F11")) SendKeyAndRecheck(hwnd, who, "F11", Native.VK_F11, false);
                    break;

                case WindowDisplayMode.SendAltEnter:
                    if (!AlreadyCovering(hwnd, who, "Alt+Enter")) SendKeyAndRecheck(hwnd, who, "Alt+Enter", Native.VK_RETURN, true);
                    break;

                case WindowDisplayMode.CoverTaskbar:
                    Win32.MakeFullscreen(hwnd);
                    Win32.LogCoveringCompare(hwnd, "覆盖任务栏(" + who + ")");
                    break;

                case WindowDisplayMode.Maximize:
                    Native.ShowWindow(hwnd, Native.SW_SHOWMAXIMIZED); // SW_MAXIMIZE
                    Thread.Sleep(80);
                    Logger.Info(who + " 已最大化（普通最大化，不覆盖任务栏），最大化状态=" + Win32.IsMaximized(hwnd));
                    break;

                case WindowDisplayMode.MaximizeCoverTaskbar:
                    Native.ShowWindow(hwnd, Native.SW_SHOWMAXIMIZED);
                    Thread.Sleep(80);
                    Win32.MakeFullscreen(hwnd);   // 再强制铺满整屏并置顶 → 覆盖任务栏
                    Win32.LogCoveringCompare(hwnd, "最大化+覆盖任务栏(" + who + ")");
                    break;

                case WindowDisplayMode.Windowed:
                    Native.ShowWindow(hwnd, Native.SW_RESTORE);   // 最大化/最小化 → 恢复成普通窗口
                    Thread.Sleep(80);
                    Logger.Info(who + " 已窗口化（恢复为普通窗口，保留自身尺寸位置）");
                    break;

                case WindowDisplayMode.CustomRect:
                    ApplyCustomRect(hwnd, who, rectSpec);
                    break;

                case WindowDisplayMode.KeepCurrent:
                    Logger.Info(who + " 保持当前窗口状态（只切到前台，不改大小位置）");
                    break;
            }

            // 最后再强制一次前台（改窗口状态之后可能被系统抢走焦点）
            Win32.ForceForeground(hwnd);
            Logger.Info("已切换到: " + WindowFinder.DescribeWindow(hwnd) + " (显示方式=" + mode + ")");
        }

        /// <summary>发键式全屏前的判断：已经铺满就别再发键，否则会把全屏"切出去"。</summary>
        private static bool AlreadyCovering(IntPtr hwnd, string who, string keyName)
        {
            Win32.LogCoveringCompare(hwnd, keyName + " 判断(" + who + ")");
            if (Win32.IsCoveringScreen(hwnd))
            {
                Logger.Info(who + " 已铺满屏幕，跳过" + keyName + "（避免把全屏切掉）");
                return true;
            }
            return false;
        }

        /// <summary>发键 → 回检 → 程序不支持就回退 Win32 覆盖任务栏（保证"点了有反应"）。</summary>
        private static void SendKeyAndRecheck(IntPtr hwnd, string who, string keyName, int vk, bool withAlt)
        {
            Win32.ForceForeground(hwnd);
            Thread.Sleep(120);
            Win32.SendKey(vk, withAlt);
            Thread.Sleep(320);
            Win32.LogCoveringCompare(hwnd, keyName + " 发送后(" + who + ")");

            if (!Win32.IsCoveringScreen(hwnd))
            {
                Logger.Warn(who + " 按" + keyName + "后仍未铺满（该程序可能不支持此快捷键），回退 Win32 覆盖任务栏");
                Win32.MakeFullscreen(hwnd);
            }
        }

        /// <summary>自定义区域：按 "x,y,w,h"（支持百分比）摆放窗口。</summary>
        private static void ApplyCustomRect(IntPtr hwnd, string who, string spec)
        {
            RECT mon = Win32.GetMonitorRect(hwnd);
            RECT target;

            if (!RectSpec.TryParse(spec, mon, out target))
            {
                Logger.Warn(who + " 的自定义区域不合法：\"" + spec + "\"，本次回退为最大化");
                Native.ShowWindow(hwnd, Native.SW_SHOWMAXIMIZED);
                return;
            }

            // 先恢复成普通窗口，否则最大化窗口的 SetWindowPos 会被系统"弹"回去
            Native.ShowWindow(hwnd, Native.SW_RESTORE);
            Thread.Sleep(60);

            bool ok = Native.SetWindowPos(hwnd, Native.HWND_NOTOPMOST,
                target.Left, target.Top, target.Width, target.Height,
                Native.SWP_SHOWWINDOW | Native.SWP_NOOWNERZORDER);

            Logger.Info(string.Format("{0} 已摆放到自定义区域 {1}（显示器 {2}）成功={3}",
                who, target, mon, ok));
        }
    }
}
