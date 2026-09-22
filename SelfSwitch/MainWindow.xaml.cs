using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SelfSwitch
{
    /// <summary>
    /// 悬浮按钮窗口：主屏右下角、永远置顶、半透明、点击切换。
    /// 只响应鼠标点击，不注册全局热键。
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>全局单例引用（切换完成后要把自己重新压到最顶层）。</summary>
        public static MainWindow Instance { get; private set; }

        private DispatcherTimer _reassertTimer;
        private int _reassertCount;

        // ---- 鼠标交互（v1.11.0：按钮只负责"单击=切换"，位置调整移到了配置界面）----
        private bool _mouseDown;            // 鼠标左键按在按钮上（这期间禁止自动隐藏，否则会吃掉这次点击）

        // ---- 全局快捷键（v1.9）----
        private const int HotkeyId = 0x5A77;
        private HwndSource _hwndSource;
        private bool _hotkeyRegistered;
        private string _hotkeyApplied = string.Empty;   // 当前已注册的描述串

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            Closing += OnClosing;
        }

        // ==================== 生命周期 ====================

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            try
            {
                // WS_EX_NOACTIVATE：点击悬浮按钮时不抢前台焦点，
                // 这样 GetForegroundWindow() 仍然是被切换的目标程序，方向判断才准确。
                var handle = new WindowInteropHelper(this).Handle;
                EnsureNoActivate("SourceInitialized");

                // 挂消息钩子：接收全局快捷键的 WM_HOTKEY
                _hwndSource = HwndSource.FromHwnd(handle);
                if (_hwndSource != null) _hwndSource.AddHook(WndProc);
            }
            catch (Exception ex)
            {
                Logger.Error("设置无激活样式失败", ex);
            }
        }

        /// <summary>
        /// 设置并**校验** WS_EX_NOACTIVATE。
        /// WPF 在 Show / 改样式时可能覆盖扩展样式位，所以窗口加载后再复查一次并写日志——
        /// 这一位如果丢了，点按钮就会抢走前台焦点（直接破坏方向判断）。
        /// </summary>
        private void EnsureNoActivate(string stage)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                if (h == IntPtr.Zero) return;

                if (!Win32.IsNoActivate(h)) Win32.SetNoActivate(h, true);

                Logger.Info("悬浮按钮无激活样式(WS_EX_NOACTIVATE)=" +
                            (Win32.IsNoActivate(h) ? "已生效" : "设置失败(点击会抢焦点)") +
                            " [" + stage + "]");
            }
            catch (Exception ex)
            {
                Logger.Error("设置无激活样式失败 [" + stage + "]", ex);
            }
        }

        /// <summary>窗口消息钩子：目前只处理全局快捷键。</summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
                {
                    handled = true;
                    Logger.Info("全局快捷键触发切换（" + _hotkeyApplied + "）");

                    // 快捷键与点击按钮走同一套逻辑：一样受严格模式、防重入、节流约束
                    BringToFront();
                    ToggleService.Toggle();
                    ReassertTopmostDelayed();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("处理快捷键消息失败", ex);
            }
            return IntPtr.Zero;
        }

        /// <summary>按配置注册/注销全局快捷键。开关或组合键变了都会重新注册。</summary>
        public void ApplyHotkey(bool enabled, string text)
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;

                UnregisterHotkeyInternal(handle);

                if (!enabled)
                {
                    Logger.Info("快速切换快捷键：已关闭");
                    return;
                }

                uint mods, vk;
                string normalized;
                if (!HotkeySpec.TryParse(text, out mods, out vk, out normalized))
                {
                    Logger.Warn("快速切换快捷键格式不合法，未注册：\"" + text + "\"");
                    TrayHelper.ShowWarning("快捷键未生效",
                        "快捷键 \"" + text + "\" 格式不合法，已跳过注册。\r\n" +
                        "请到配置界面重新按一次组合键（例如 Ctrl+Alt+S）。");
                    return;
                }

                if (Native.RegisterHotKey(handle, HotkeyId, mods | Native.MOD_NOREPEAT, vk))
                {
                    _hotkeyRegistered = true;
                    _hotkeyApplied = normalized;
                    Logger.Info("已注册全局快捷键: " + normalized);
                }
                else
                {
                    int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    _hotkeyApplied = normalized;
                    Logger.Warn("全局快捷键注册失败: " + normalized + ", error=" + err);
                    TrayHelper.ShowWarning("快捷键注册失败",
                        "快捷键 " + normalized + " 没能注册（错误码 " + err + "）：\r\n" +
                        "可能已被其它程序占用。请到配置界面换一个组合键。");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("注册全局快捷键失败", ex);
            }
        }

        private void UnregisterHotkeyInternal(IntPtr handle)
        {
            try
            {
                if (_hotkeyRegistered)
                {
                    Native.UnregisterHotKey(handle, HotkeyId);
                    _hotkeyRegistered = false;
                    Logger.Info("已注销全局快捷键");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("注销全局快捷键失败", ex);
            }
        }

        /// <summary>退出前注销快捷键（避免残留占用）。</summary>
        public void UnregisterHotkey()
        {
            try
            {
                UnregisterHotkeyInternal(new WindowInteropHelper(this).Handle);
                if (_hwndSource != null)
                {
                    _hwndSource.RemoveHook(WndProc);
                    _hwndSource = null;
                }
            }
            catch { }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 关键顺序（修复测试报告问题 A）：先订阅 → 再启动检测 → 最后应用配置。
            // 若先执行 ApplyConfig()，它内部用的是 Current 的初始值 Other，会先把按钮判成“隐藏”；
            // 而随后 Invalidate() 算出的正确分类（Chrome/Target）因为"还没人订阅"被丢弃，
            // 结果就是启动时前台已经是 Chrome/目标程序的情况下，按钮永远不出现。
            try
            {
                ForegroundWatcher.StateChanged += OnForegroundStateChanged;
                ForegroundWatcher.Start();
            }
            catch (Exception ex)
            {
                Logger.Error("启动前台窗口跟随失败", ex);
            }

            ApplyConfig();   // 此时 Current 已是正确分类，文字 + 可见性一次同步到位

            EnsureNoActivate("Loaded");   // 再校验一次：WPF Show 之后这一位有可能被覆盖

            try
            {
                // 分辨率 / 缩放变化时重新定位（失败也不影响主流程）
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            }
            catch (Exception ex)
            {
                Logger.Warn("订阅显示设置变化失败: " + ex.Message);
            }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    PositionWindow();
                    BringToFront();
                }));
            }
            catch { }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            PositionWindow();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (!App.IsShuttingDown)
            {
                // 悬浮窗没有标题栏，正常无法被关闭；这里只是兜底，不退出程序。
                e.Cancel = true;
                Logger.Warn("悬浮按钮窗口收到关闭请求，已忽略");
            }
        }

        // ==================== 配置应用 / 定位 ====================

        /// <summary>把配置里的文字、透明度、字号、位置立即应用到悬浮按钮。</summary>
        public void ApplyConfig()
        {
            try
            {
                AppConfig cfg = App.Config;
                TxtButton.FontSize = cfg.ButtonFontSize;
                Opacity = cfg.ButtonOpacity;
                SyncButtonAppearance(ForegroundWatcher.Current);   // 文字 + 可见性统一在入口处同步
                PositionWindow();
                ApplyHotkey(cfg.HotkeyEnabled, cfg.HotkeyText);    // 快捷键：开关或组合键变了会重新注册
                ForegroundWatcher.Invalidate();  // 路径可能改了，强制重新判定一次（会再同步一次）
                Logger.Info("悬浮按钮配置已应用: 文字=\"" + TxtButton.Text + "\", 透明度=" + cfg.ButtonOpacity +
                            ", 锚点=" + cfg.Anchor + ", 偏移=(" + cfg.OffsetX + "," + cfg.OffsetY + ")" +
                            ", 其它程序时隐藏=" + cfg.HideButtonWhenOther);
            }
            catch (Exception ex)
            {
                Logger.Error("应用悬浮按钮配置失败", ex);
            }
        }

        /// <summary>
        /// 按钮外观的唯一同步入口：文字 + 可见性。
        /// 两处调用（ApplyConfig / 前台类别变化）都走这里，避免实现分散后不一致。
        /// </summary>
        private void SyncButtonAppearance(ForegroundKind kind)
        {
            try
            {
                string text = TextForState(kind);
                if (TxtButton.Text != text)
                {
                    TxtButton.Text = text;
                    Logger.Info("悬浮按钮文字已更新: \"" + text + "\"（前台=" + kind + "）");
                }
                ApplyVisibility(kind);
            }
            catch (Exception ex)
            {
                Logger.Error("同步悬浮按钮外观失败", ex);
            }
        }

        /// <summary>前台类别变化 → 同步文字与显隐，并抢回被第三方置顶浮窗压住的层级。</summary>
        private void OnForegroundStateChanged(ForegroundKind kind)
        {
            SyncButtonAppearance(kind);

            // 修复测试报告问题 C：前台应用一变，输入法状态条 / 系统通知横幅这类
            // "注入到前台进程里的 TOPMOST 浮窗"会被一起抬到最上层，把按钮压住导致点不动。
            // 这里在前台变化时补一次置顶（隐藏状态下 BringToFront 内部会自动跳过，
            // 不会被 SWP_SHOWWINDOW 把隐藏的按钮弹出来）。
            BringToFront();
        }

        /// <summary>
        /// 其它程序在前台（且配置允许）时隐藏悬浮按钮，回到 Chrome / 目标程序时再显示。
        /// 用 Visibility.Hidden 而不是 Collapsed：隐藏时仍保留布局，ActualWidth 有效，重新显示不用重算。
        ///
        /// 与严格模式的联动：严格模式关闭时，按钮在"其它程序"状态下是**有用**的
        /// （非严格模式下点它仍会按上次状态切换），此时忽略"自动隐藏"，避免出现"需要点但点不到"的死结。
        /// </summary>
        private void ApplyVisibility(ForegroundKind kind)
        {
            try
            {
                // 鼠标正在按钮上按下、或正处于"位置调整模式"时，绝不改变显隐：
                // 一旦此刻把窗口藏起来，鼠标松开事件就收不到了 → 这次点击/确认直接丢失。
                // （现场日志实测过：点按钮的瞬间窗口变前台 → 被判"其它程序" → 隐藏 → 点击被吃掉）
                if (_mouseDown || _placing)
                {
                    Logger.Info("鼠标交互/位置调整进行中，暂不改变悬浮按钮显隐（避免吃掉这次点击）");
                    return;
                }

                AppConfig cfg = App.Config;
                bool isOther = kind == ForegroundKind.Other;
                bool hideRequested = cfg != null && cfg.HideButtonWhenOther && isOther;
                bool suppressedByStrict = hideRequested && cfg != null && !cfg.StrictForegroundCheck;
                bool hide = hideRequested && !suppressedByStrict;

                if (suppressedByStrict && Visibility == Visibility.Hidden)
                {
                    // 严格模式刚被关掉：被抑制的隐藏要立刻撤回
                    hide = false;
                }

                if (hide)
                {
                    if (Visibility != Visibility.Hidden)
                    {
                        Visibility = Visibility.Hidden;
                        Logger.Info("前台是其它程序 → 悬浮按钮已自动隐藏");
                    }
                }
                else
                {
                    if (Visibility != Visibility.Visible)
                    {
                        Visibility = Visibility.Visible;
                        PositionWindow();
                        BringToFront();
                        Logger.Info("前台是 " + kind + " → 悬浮按钮已显示");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("切换悬浮按钮显示状态失败", ex);
            }
        }

        /// <summary>按前台类别取对应文字；取不到就用兜底文字，保证按钮不会变空。</summary>
        private static string TextForState(ForegroundKind kind)
        {
            try
            {
                AppConfig cfg = App.Config;
                if (cfg == null) return "切换";
                string text;
                switch (kind)
                {
                    case ForegroundKind.Chrome: text = cfg.ButtonTextWhenChrome; break;
                    case ForegroundKind.Target: text = cfg.ButtonTextWhenTarget; break;
                    default: text = cfg.ButtonTextOther; break;
                }
                if (string.IsNullOrEmpty(text)) text = "切换";
                return text;
            }
            catch
            {
                return "切换";
            }
        }

        /// <summary>
        /// 按配置的锚点 + X/Y 偏移定位。
        /// 参考系是主屏**工作区**（默认就不会压住任务栏）；偏移用负值可以主动压到任务栏上。
        /// 取整用 Math.Floor 并做越界夹取——宁可偏上偏左 1px，也不能因为取整把按钮压到任务栏上。
        /// </summary>
        private void PositionWindow()
        {
            try
            {
                AppConfig cfg = App.Config;
                var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea; // 物理像素，已排除任务栏
                RECT scr = Win32.GetPrimaryScreenRect();                         // 整屏（物理像素）

                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                double sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
                double sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

                double w = ActualWidth;
                double h = ActualHeight;
                if (double.IsNaN(w) || w <= 0) w = DesiredSize.Width;
                if (double.IsNaN(h) || h <= 0) h = DesiredSize.Height;

                double ox = cfg.OffsetX ?? cfg.EdgeMargin;
                double oy = cfg.OffsetY ?? cfg.EdgeMargin;

                // 自由位置（用户拖动过）：直接按记住的坐标放，夹在屏幕内
                if (cfg.FreePosition && cfg.FreeX != null && cfg.FreeY != null)
                {
                    double fl = Math.Floor(cfg.FreeX.Value / sx);
                    double ft = Math.Floor(cfg.FreeY.Value / sy);
                    Left = ClampTo(fl, scr.Left / sx, scr.Right / sx - w);
                    Top = ClampTo(ft, scr.Top / sy, scr.Bottom / sy - h);
                    return;
                }

                double leftDip;
                double topDip;
                switch (cfg.Anchor)
                {
                    case ButtonAnchor.BottomLeft:
                        leftDip = wa.Left / sx + ox;
                        topDip = wa.Bottom / sy - h - oy;
                        break;
                    case ButtonAnchor.TopRight:
                        leftDip = wa.Right / sx - w - ox;
                        topDip = wa.Top / sy + oy;
                        break;
                    case ButtonAnchor.TopLeft:
                        leftDip = wa.Left / sx + ox;
                        topDip = wa.Top / sy + oy;
                        break;
                    default: // BottomRight
                        leftDip = wa.Right / sx - w - ox;
                        topDip = wa.Bottom / sy - h - oy;
                        break;
                }

                // 常规情况夹在工作区内（多留 1 DIP 防取整外溢）；
                // 用户显式给了负偏移 = 想压在任务栏上，这时夹取范围放宽到整屏，别把他"保护"住调不动。
                bool userWantsOverTaskbar = ox < 0 || oy < 0;
                double minLeft, maxLeft, minTop, maxTop;
                if (userWantsOverTaskbar)
                {
                    minLeft = scr.Left / sx;
                    maxLeft = scr.Right / sx - w;
                    minTop = scr.Top / sy;
                    maxTop = scr.Bottom / sy - h;
                }
                else
                {
                    minLeft = wa.Left / sx;
                    maxLeft = wa.Right / sx - w - 1;
                    minTop = wa.Top / sy;
                    maxTop = wa.Bottom / sy - h - 1;
                }

                Left = ClampTo(Math.Floor(leftDip), minLeft, maxLeft);
                Top = ClampTo(Math.Floor(topDip), minTop, maxTop);
            }
            catch (Exception ex)
            {
                Logger.Error("悬浮按钮定位失败", ex);
            }
        }

        private static double ClampTo(double value, double min, double max)
        {
            if (max < min) max = min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>把自己重新压到最顶层（目标窗口全屏置顶后调用）。</summary>
        public void BringToFront()
        {
            try
            {
                // 隐藏状态下绝不能调 SetWindowPos(SWP_SHOWWINDOW)，否则会把按钮强行弹出来
                if (Visibility != Visibility.Visible) return;
                IntPtr h = new WindowInteropHelper(this).Handle;
                Win32.EnsureTopmost(h);
            }
            catch (Exception ex)
            {
                Logger.Error("重新置顶悬浮按钮失败", ex);
            }
        }

        /// <summary>切换完成后延时多次重新置顶，避免被全屏窗口盖住。</summary>
        private void ReassertTopmostDelayed()
        {
            if (_reassertTimer == null)
            {
                _reassertTimer = new DispatcherTimer();
                _reassertTimer.Interval = TimeSpan.FromMilliseconds(400);
                _reassertTimer.Tick += delegate
                {
                    _reassertCount++;
                    BringToFront();
                    if (_reassertCount >= 3)
                    {
                        _reassertTimer.Stop();
                        _reassertCount = 0;
                    }
                };
            }
            _reassertCount = 0;
            _reassertTimer.Stop();
            _reassertTimer.Start();
        }

        // ==================== 点击（切换） ====================

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true;
                _mouseDown = true;      // 鼠标按在按钮上期间，禁止"自动隐藏"把它藏起来
                Opacity = 1.0;
                Logger.Info("悬浮按钮：鼠标按下");
            }
            catch (Exception ex)
            {
                Logger.Error("处理鼠标按下失败", ex);
            }
        }

        private void RootBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _mouseDown = false;

            // 位置调整模式中：这一次点击是用来"确认新位置"的，不触发切换
            if (_placing)
            {
                Logger.Info("位置调整模式中：本次点击用于确认位置，不触发切换");
                return;
            }

            BringFrontAndToggle();
        }

        /// <summary>把按钮压到最顶层并执行切换（点按钮、按快捷键都走这里）。</summary>
        private void BringFrontAndToggle()
        {
            try
            {
                BringToFront();
                ToggleService.Toggle();
                ReassertTopmostDelayed();
            }
            catch (Exception ex)
            {
                Logger.Error("执行切换失败", ex);
            }
        }

        // ==================== 位置调整模式（v1.11.0） ====================
        // 位置调整从"在按钮上拖动"改成"在配置界面里点『调整位置』"：
        //   配置窗口隐藏 → 悬浮按钮跟随鼠标 → 移到想要的位置后单击一下确认（Esc 取消）。
        // 这样悬浮按钮只承担"单击 = 切换"一件事，永远不会把点击吃掉。

        private const int PlacePollMs = 30;      // 跟随轮询间隔
        private const int PlaceGuardMs = 500;    // 进入后先忽略 500ms，免得"点按钮进入模式"那一下立刻确认

        private DispatcherTimer _placeTimer;
        private bool _placing;
        private int _placeStartTick;
        private double _placeOriginalLeft;
        private double _placeOriginalTop;

        /// <summary>是否处于位置调整模式。</summary>
        public bool IsPlacing { get { return _placing; } }

        /// <summary>进入位置调整模式（由配置界面的「调整位置」按钮调用）。</summary>
        public void BeginPlacement()
        {
            try
            {
                if (_placing) return;

                _placeOriginalLeft = Left;
                _placeOriginalTop = Top;
                _placing = true;
                _placeStartTick = Environment.TickCount;

                FollowCursor();   // 先贴到鼠标上，用户立刻能看到"它在跟着我"

                if (_placeTimer == null)
                {
                    _placeTimer = new DispatcherTimer(DispatcherPriority.Input);
                    _placeTimer.Interval = TimeSpan.FromMilliseconds(PlacePollMs);
                    _placeTimer.Tick += OnPlaceTimerTick;
                }
                _placeTimer.Start();

                Logger.Info("进入位置调整模式：按钮跟随鼠标，单击确认，Esc 取消");
                TrayHelper.ShowInfo("调整悬浮按钮位置",
                    "移动鼠标把悬浮按钮放到想要的位置，然后单击一下确认；\r\n想放弃就按 Esc。");
            }
            catch (Exception ex)
            {
                Logger.Error("进入位置调整模式失败", ex);
                _placing = false;
            }
        }

        private void OnPlaceTimerTick(object sender, EventArgs e)
        {
            try
            {
                if (!_placing)
                {
                    _placeTimer.Stop();
                    return;
                }

                // Esc → 取消，位置回到调整前
                if ((Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0)
                {
                    CancelPlacement();
                    return;
                }

                FollowCursor();

                if (unchecked(Environment.TickCount - _placeStartTick) < PlaceGuardMs) return;

                // 左键按下 → 确认
                if ((Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0)
                {
                    ConfirmPlacement();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("位置调整轮询失败", ex);
            }
        }

        /// <summary>让按钮中心跟着鼠标走（允许压在任务栏上，仅限制不出屏）。</summary>
        private void FollowCursor()
        {
            try
            {
                POINT pt;
                if (!Native.GetCursorPos(out pt)) return;

                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                double sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
                double sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

                double w = ActualWidth;
                double h = ActualHeight;
                if (double.IsNaN(w) || w <= 0) w = DesiredSize.Width;
                if (double.IsNaN(h) || h <= 0) h = DesiredSize.Height;

                RECT scr = Win32.GetPrimaryScreenRect();
                double cx = pt.X / sx;
                double cy = pt.Y / sy;

                Left = ClampTo(Math.Floor(cx - w / 2), scr.Left / sx, scr.Right / sx - w);
                Top = ClampTo(Math.Floor(cy - h / 2), scr.Top / sy, scr.Bottom / sy - h);
            }
            catch { }
        }

        private void ConfirmPlacement()
        {
            try
            {
                StopPlacement();

                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                double sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
                double sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

                AppConfig cfg = App.Config;
                cfg.FreePosition = true;
                cfg.FreeX = Math.Round(Left * sx);   // 存物理像素
                cfg.FreeY = Math.Round(Top * sy);
                cfg.Normalize();
                App.SaveConfig();

                Logger.Info("位置调整完成，已保存: FreeX=" + cfg.FreeX + ", FreeY=" + cfg.FreeY);
                TrayHelper.ShowInfo("悬浮按钮位置已保存",
                    "X=" + (int)Math.Round(cfg.FreeX.Value) + ", Y=" + (int)Math.Round(cfg.FreeY.Value) +
                    "（已写入 config.json，下次启动还在原地）");
            }
            catch (Exception ex)
            {
                Logger.Error("保存悬浮按钮位置失败", ex);
            }
            finally
            {
                App.EndPlacementBackToConfig();
            }
        }

        private void CancelPlacement()
        {
            try
            {
                StopPlacement();
                Left = _placeOriginalLeft;
                Top = _placeOriginalTop;
                Logger.Info("已取消位置调整，按钮回到调整前的位置");
            }
            catch (Exception ex)
            {
                Logger.Error("取消位置调整失败", ex);
            }
            finally
            {
                App.EndPlacementBackToConfig();
            }
        }

        private void StopPlacement()
        {
            _placing = false;
            try { if (_placeTimer != null) _placeTimer.Stop(); } catch { }
        }

        private void RootBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            // 悬停变清晰：直接赋值，无动画
            Opacity = 1.0;
        }

        private void RootBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_placing) return;   // 位置调整中保持不透明
            Opacity = App.Config.ButtonOpacity;
        }

        // ==================== 右键菜单 ====================

        private void MenuConfig_Click(object sender, RoutedEventArgs e)
        {
            App.ShowConfig();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            App.ExitApp();
        }
    }
}
