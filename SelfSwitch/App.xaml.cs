using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SelfSwitch
{
    public partial class App : Application
    {
        private Mutex _mutex;
        private static ConfigWindow _configWindow;

        /// <summary>当前生效的配置（全局单例，悬浮窗与切换逻辑都读它）。</summary>
        public static AppConfig Config { get; set; }

        public static string ExePath { get; private set; }
        public static string BaseDir { get; private set; }
        public static string ConfigPath { get; private set; }
        public static string LogPath { get; private set; }

        /// <summary>正在退出（用于放行悬浮窗的 Closing 拦截）。</summary>
        public static volatile bool IsShuttingDown;

        /// <summary>本程序版本号（写进日志与托盘提示，方便确认跑的是哪个版本）。</summary>
        public static string VersionText { get; private set; } = "?";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                Version v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (v != null) VersionText = v.Major + "." + v.Minor + "." + v.Build;
            }
            catch { }

            // ---------- 单实例 ----------
            bool createdNew;
            _mutex = new Mutex(true, "SelfSwitch_SingleInstance_Mutex_9F2A", out createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            // ---------- 路径解析：EXE 同目录优先，不可写则回落 %AppData%\SelfSwitch ----------
            AppPaths.Resolve();
            ExePath = AppPaths.ExePath;
            BaseDir = AppPaths.ExeDir;
            ConfigPath = AppPaths.ConfigFile;
            LogPath = AppPaths.LogFile;

            Logger.Init(LogPath);
            Logger.Info("=== SelfSwitch 启动 === 版本=" + VersionText + " exe=" + ExePath +
                        ", 数据目录=" + AppPaths.DataDir +
                        (AppPaths.UsedFallback ? "(EXE 目录不可写, 已回落 %AppData%)" : "") +
                        ", 管理员权限=" + Win32.IsSelfElevated());

            // ---------- 全局异常兜底：绝不崩溃 ----------
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // ---------- 配置 ----------
            Config = AppConfig.Load(ConfigPath);

            // ---------- 自启同步 ----------
            ApplyAutoStart(Config.AutoStart, true);

            // ---------- 托盘 ----------
            TrayHelper.Create(ToggleService.Toggle, ShowConfig, ExitApp);

            // ---------- 悬浮按钮 ----------
            var win = new MainWindow();
            win.Show();

            // ---------- 权限检测 ----------
            CheckElevation();

            // ---------- 数据目录回落提示 ----------
            if (AppPaths.UsedFallback)
            {
                TrayHelper.ShowInfo("配置位置已调整",
                    "EXE 所在目录不可写（例如放到了 Program Files），" +
                    "配置与日志已自动改存到：\r\n" + AppPaths.DataDir);
            }
        }

        // ==================== 权限 / 自启 ====================

        /// <summary>若 Chrome 或目标程序以管理员身份运行而本工具不是，给出提示。</summary>
        private void CheckElevation()
        {
            try
            {
                if (Win32.IsSelfElevated()) return;

                string who = null;
                if (Win32.IsProcessElevated(Config.ChromePath)) who = "Chrome";
                if (Win32.IsProcessElevated(Config.TargetExePath))
                {
                    who = who == null ? "目标程序" : who + " 和目标程序";
                }

                if (who != null)
                {
                    TrayHelper.ShowWarning("权限不足提示",
                        "检测到 " + who + " 以管理员身份运行，而本工具是普通权限，" +
                        "可能无法切换其窗口。\r\n请右键本程序 →【以管理员身份运行】。");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("权限检测失败", ex);
            }
        }

        /// <summary>写入/移除 HKCU 开机自启项。</summary>
        public static bool ApplyAutoStart(bool enable, bool silent = false)
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string valueName = "SelfSwitch";
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (key == null)
                    {
                        Logger.Warn("无法打开注册表 Run 键");
                        return false;
                    }

                    if (enable)
                    {
                        string value = "\"" + ExePath + "\"";
                        string current = key.GetValue(valueName) as string;
                        if (!string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
                        {
                            key.SetValue(valueName, value);
                            Logger.Info("已写入开机自启: " + value);
                        }
                    }
                    else
                    {
                        if (key.GetValue(valueName) != null)
                        {
                            key.DeleteValue(valueName, false);
                            Logger.Info("已移除开机自启项");
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("设置开机自启失败", ex);
                if (!silent) TrayHelper.ShowError("开机自启设置失败", ex.Message);
                return false;
            }
        }

        // ==================== 配置窗口 ====================

        public static void ShowConfig()
        {
            try
            {
                Dispatcher disp = Current.Dispatcher;
                if (!disp.CheckAccess())
                {
                    disp.Invoke(new Action(ShowConfig));
                    return;
                }

                if (_configWindow == null)
                {
                    _configWindow = new ConfigWindow();
                    _configWindow.Closed += delegate { _configWindow = null; };
                    _configWindow.Show();
                }
                else
                {
                    if (_configWindow.WindowState == WindowState.Minimized)
                    {
                        _configWindow.WindowState = WindowState.Normal;
                    }
                    _configWindow.Activate();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("打开配置窗口失败", ex);
            }
        }

        /// <summary>进入悬浮按钮位置调整模式（配置界面点「调整位置」触发）：隐藏配置窗口，交给悬浮按钮跟随鼠标。</summary>
        public static void BeginPlacement()
        {
            try
            {
                if (_configWindow != null) _configWindow.Hide();
                if (SelfSwitch.MainWindow.Instance != null) SelfSwitch.MainWindow.Instance.BeginPlacement();
            }
            catch (Exception ex)
            {
                Logger.Error("进入位置调整模式失败", ex);
            }
        }

        /// <summary>位置调整结束（确认或取消）→ 把配置窗口显示回来并刷新状态文字。</summary>
        public static void EndPlacementBackToConfig()
        {
            try
            {
                if (_configWindow == null) return;
                _configWindow.Show();
                _configWindow.Activate();
                _configWindow.RefreshPositionState();
            }
            catch (Exception ex)
            {
                Logger.Error("返回配置窗口失败", ex);
            }
        }

        public static void SaveConfig()
        {
            try
            {
                Config.Save(ConfigPath);
            }
            catch (Exception ex)
            {
                TrayHelper.ShowError("保存配置失败", ex.Message + "\r\n（详见 app.log）");
            }
        }

        // ==================== 退出 ====================

        public static void ExitApp()
        {
            try
            {
                IsShuttingDown = true;
                Logger.Info("=== 用户请求退出 ===");
                TrayHelper.Dispose();
                Current.Shutdown();
            }
            catch
            {
                Environment.Exit(0);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            IsShuttingDown = true;
            try { if (SelfSwitch.MainWindow.Instance != null) SelfSwitch.MainWindow.Instance.UnregisterHotkey(); } catch { }
            try { ForegroundWatcher.Stop(); } catch { }
            try { TrayHelper.Dispose(); } catch { }
            try { if (_mutex != null) _mutex.ReleaseMutex(); } catch { }
            try { if (_mutex != null) _mutex.Dispose(); } catch { }
            Logger.Info("=== SelfSwitch 已退出 ===");
            base.OnExit(e);
        }

        // ==================== 工具 ====================

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error("未处理的界面线程异常", e.Exception);
            e.Handled = true; // 吞掉异常，保证不闪退
            try
            {
                TrayHelper.ShowError("程序异常", e.Exception.Message + "\r\n详细信息见 app.log");
            }
            catch { }
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.Error("未处理的域异常: " + (e.ExceptionObject as Exception));
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error("未观察的任务异常", e.Exception);
            e.SetObserved();
        }
    }
}
