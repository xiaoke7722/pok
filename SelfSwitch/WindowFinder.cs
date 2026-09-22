using System;
using System.Collections.Generic;
using System.Text;

namespace SelfSwitch
{
    /// <summary>
    /// 依据配置的完整路径（可选标题关键字）查找已打开进程的顶层主窗口。
    /// 只查找，不启动任何进程。
    /// </summary>
    internal static class WindowFinder
    {
        /// <summary>
        /// 查找与 exePath 匹配的、Z 序最靠前的可见顶层窗口。
        /// EnumWindows 本身按 Z 序（前 → 后）回调，因此第一个命中即最前面的窗口，
        /// 也就等价于"最近被激活"的那个窗口。
        /// titleKeyword 非空时，窗口标题还必须包含该关键字（忽略大小写）。
        /// 找不到返回 IntPtr.Zero。
        /// </summary>
        public static IntPtr FindMainWindow(string exePath, string titleKeyword = null)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return IntPtr.Zero;

            string keyword = (titleKeyword ?? string.Empty).Trim();
            IntPtr found = IntPtr.Zero;
            var cache = new Dictionary<uint, string>();
            int pathHitButTitleMiss = 0;

            try
            {
                Native.EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
                {
                    try
                    {
                        // 1) 必须可见
                        if (!Native.IsWindowVisible(hWnd)) return true;

                        // 2) 必须有标题（Chrome 有大量无标题的隐藏窗口，必须过滤）
                        if (Native.GetWindowTextLength(hWnd) <= 0) return true;

                        // 3) 必须是无属主的顶层窗口（排除对话框 / 弹出窗口）
                        if (Native.GetWindow(hWnd, Native.GW_OWNER) != IntPtr.Zero) return true;

                        // 4) 排除工具窗口（WS_EX_TOOLWINDOW）
                        if (Win32.IsToolWindow(hWnd)) return true;

                        // 5) PID → 完整镜像路径 → 与配置路径规范化比较
                        uint pid;
                        Native.GetWindowThreadProcessId(hWnd, out pid);
                        if (pid == 0) return true;

                        string path;
                        if (!cache.TryGetValue(pid, out path))
                        {
                            path = Win32.GetProcessPathByPid(pid);
                            cache[pid] = path;
                        }
                        if (path == null) return true;

                        if (!PathHelper.Equals(path, exePath)) return true;

                        // 6) 可选：标题关键字过滤
                        if (keyword.Length > 0)
                        {
                            string title = Win32.GetWindowTitle(hWnd);
                            if (title == null ||
                                title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                pathHitButTitleMiss++;
                                return true; // 路径对了但标题不含关键字，继续找下一个窗口
                            }
                        }

                        found = hWnd;
                        return false; // 命中，停止枚举
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("枚举窗口回调异常", ex);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Logger.Error("EnumWindows 失败", ex);
            }

            if (found == IntPtr.Zero && pathHitButTitleMiss > 0)
            {
                Logger.Warn("路径匹配到 " + pathHitButTitleMiss + " 个窗口，但标题都不包含关键字 \"" +
                            keyword + "\"，视为未找到");
            }

            return found;
        }

        /// <summary>
        /// 诊断用（v1.6）：找"进程文件名相同、但完整路径与配置不一致"的可见窗口。
        /// 典型场景：配置写 D:\Doubao\Doubao.exe，实际在跑的是 D:\Doubao\app\Doubao.exe ——
        /// 这时直接把正确路径报出来，用户不用自己翻任务管理器。
        /// 返回形如 "D:\Doubao\app\Doubao.exe（窗口标题：豆包）" 的多行文本；没有则返回 null。
        /// </summary>
        public static string FindSimilarWindowHint(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return null;

            string fileName;
            try { fileName = System.IO.Path.GetFileName(exePath); }
            catch { return null; }
            if (string.IsNullOrEmpty(fileName)) return null;

            var list = new List<string>();
            var cache = new Dictionary<uint, string>();

            try
            {
                Native.EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
                {
                    try
                    {
                        if (!Native.IsWindowVisible(hWnd)) return true;
                        if (Native.GetWindowTextLength(hWnd) <= 0) return true;
                        if (Native.GetWindow(hWnd, Native.GW_OWNER) != IntPtr.Zero) return true;
                        if (Win32.IsToolWindow(hWnd)) return true;

                        uint pid;
                        Native.GetWindowThreadProcessId(hWnd, out pid);
                        if (pid == 0) return true;

                        string path;
                        if (!cache.TryGetValue(pid, out path))
                        {
                            path = Win32.GetProcessPathByPid(pid);
                            cache[pid] = path;
                        }
                        if (string.IsNullOrEmpty(path)) return true;
                        if (PathHelper.Equals(path, exePath)) return true;   // 完全一致的不算"疑似"

                        string name;
                        try { name = System.IO.Path.GetFileName(path); }
                        catch { return true; }
                        if (!string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return true;

                        string item = path + "（窗口标题：" + Win32.GetWindowTitle(hWnd) + "）";
                        if (!list.Contains(item) && list.Count < 3) list.Add(item);
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Logger.Error("查找同名进程窗口失败", ex);
            }

            if (list.Count == 0) return null;

            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append("\r\n");
                sb.Append(list[i]);
            }
            return sb.ToString();
        }

        /// <summary>用于日志：窗口标题 + 类名。</summary>
        public static string DescribeWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return "(未找到)";
            try
            {
                var sbClass = new StringBuilder(256);
                Native.GetClassName(hWnd, sbClass, sbClass.Capacity);
                return string.Format("hwnd=0x{0:X}, title=\"{1}\", class=\"{2}\"",
                    hWnd.ToInt64(), Win32.GetWindowTitle(hWnd), sbClass.ToString());
            }
            catch
            {
                return "hwnd=0x" + hWnd.ToInt64().ToString("X");
            }
        }
    }
}
