using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SelfSwitch
{
    /// <summary>
    /// 托盘图标 + 气泡提示（同一错误 5 秒内不重复提示）。
    /// </summary>
    internal static class TrayHelper
    {
        private static NotifyIcon _icon;
        private static Icon _generatedIcon;
        private static readonly Dictionary<string, DateTime> LastTip =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly TimeSpan DedupeWindow = TimeSpan.FromSeconds(5);

        public static NotifyIcon Tray { get { return _icon; } }

        public static void Create(Action onToggle, Action onConfig, Action onExit)
        {
            try
            {
                _icon = new NotifyIcon();
                _icon.Icon = BuildIcon();
                _icon.Text = "SelfSwitch 窗口切换工具 v" + App.VersionText + "（双击=切换）";
                _icon.Visible = true;

                var menu = new ContextMenuStrip();
                menu.Items.Add("切换窗口", null, delegate { SafeInvoke(onToggle); });
                menu.Items.Add("打开配置", null, delegate { SafeInvoke(onConfig); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("退出程序", null, delegate { SafeInvoke(onExit); });
                _icon.ContextMenuStrip = menu;

                _icon.DoubleClick += delegate { SafeInvoke(onToggle); };
                _icon.BalloonTipClicked += delegate { SafeInvoke(onConfig); };
            }
            catch (Exception ex)
            {
                Logger.Error("创建托盘图标失败", ex);
            }
        }

        private static void SafeInvoke(Action a)
        {
            try
            {
                if (a != null) a();
            }
            catch (Exception ex)
            {
                Logger.Error("托盘菜单动作异常", ex);
            }
        }

        /// <summary>运行时画一个简单的托盘图标（两个竖条＝切换）。</summary>
        private static Icon BuildIcon()
        {
            try
            {
                using (var bmp = new Bitmap(32, 32))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        using (var brush = new SolidBrush(Color.FromArgb(255, 32, 118, 210)))
                        {
                            g.FillEllipse(brush, 1, 1, 30, 30);
                        }
                        using (var pen = new Pen(Color.White, 4.5f))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            g.DrawLine(pen, 12f, 10f, 12f, 22f);
                            g.DrawLine(pen, 20f, 10f, 20f, 22f);
                        }
                    }

                    IntPtr h = bmp.GetHicon();
                    try
                    {
                        using (var tmp = System.Drawing.Icon.FromHandle(h))
                        {
                            _generatedIcon = (System.Drawing.Icon)tmp.Clone();
                        }
                    }
                    finally
                    {
                        Native.DestroyIcon(h);
                    }
                    return _generatedIcon;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("生成托盘图标失败，使用系统默认图标", ex);
                return SystemIcons.Application;
            }
        }

        public static void ShowInfo(string title, string text)
        {
            Tip(title, text, ToolTipIcon.Info, false);
        }

        public static void ShowWarning(string title, string text)
        {
            Tip(title, text, ToolTipIcon.Warning, true);
        }

        public static void ShowError(string title, string text)
        {
            Tip(title, text, ToolTipIcon.Error, true);
        }

        private static void Tip(string title, string text, ToolTipIcon kind, bool dedupe)
        {
            try
            {
                if (_icon == null) return;

                if (dedupe)
                {
                    string key = title + "|" + text;
                    DateTime last;
                    if (LastTip.TryGetValue(key, out last) &&
                        DateTime.Now - last < DedupeWindow)
                    {
                        return; // 5 秒内同一错误不重复提示
                    }
                    LastTip[key] = DateTime.Now;
                }

                Logger.Info("提示: " + title + " - " + text);
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = text;
                _icon.BalloonTipIcon = kind;
                _icon.ShowBalloonTip(4000);
            }
            catch (Exception ex)
            {
                Logger.Error("显示气泡提示失败", ex);
            }
        }

        public static void Dispose()
        {
            try
            {
                if (_icon != null)
                {
                    _icon.Visible = false;
                    _icon.Dispose();
                    _icon = null;
                }
                if (_generatedIcon != null)
                {
                    _generatedIcon.Dispose();
                    _generatedIcon = null;
                }
            }
            catch { }
        }
    }
}
