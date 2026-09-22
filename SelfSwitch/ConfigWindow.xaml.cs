using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SelfSwitch
{
    /// <summary>配置界面：所有可配置项都在这里，保存后立即生效。</summary>
    public partial class ConfigWindow : Window
    {
        /// <summary>显示方式下拉项：索引 ↔ 枚举值一一对应（不依赖枚举的数值顺序）。</summary>
        private static readonly WindowDisplayMode[] ModeValues =
        {
            WindowDisplayMode.SendF11,
            WindowDisplayMode.CoverTaskbar,
            WindowDisplayMode.Maximize,
            WindowDisplayMode.MaximizeCoverTaskbar,
            WindowDisplayMode.Windowed,
            WindowDisplayMode.CustomRect,
            WindowDisplayMode.KeepCurrent,
            WindowDisplayMode.SendAltEnter
        };

        private static readonly string[] ModeNames =
        {
            "F11（浏览器原生全屏）",
            "覆盖任务栏（铺满 + 置顶）",
            "最大化（不覆盖任务栏）",
            "最大化 + 覆盖任务栏",
            "窗口化（普通窗口）",
            "自定义区域（分屏 / 固定尺寸）",
            "保持当前状态（只切前台）",
            "Alt+Enter（部分程序适用）"
        };

        public ConfigWindow()
        {
            InitializeComponent();
            CmbChromeMode.ItemsSource = ModeNames;
            CmbTargetMode.ItemsSource = ModeNames;
            LoadFromConfig();
        }

        private static int IndexOfMode(WindowDisplayMode mode)
        {
            int i = Array.IndexOf(ModeValues, mode);
            return i < 0 ? 0 : i;
        }

        /// <summary>系统进程 / 资源管理器 / 系统目录下的程序：不该被当成"目标程序"绑进配置。</summary>
        private static bool IsSystemProcess(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;

                string name = Path.GetFileName(path).ToLowerInvariant();
                if (name == "explorer.exe" || name == "shellexperiencehost.exe" ||
                    name == "startmenuexperiencehost.exe" || name == "searchhost.exe" ||
                    name == "textinputhost.exe" || name == "sihost.exe" || name == "ctfmon.exe")
                {
                    return true;
                }

                string lower = path.ToLowerInvariant().Replace('/', '\\');
                if (lower.StartsWith(@"c:\windows\")) return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        // ==================== 载入 / 保存 ====================

        private void LoadFromConfig()
        {
            try
            {
                AppConfig cfg = App.Config;
                TxtChrome.Text = cfg.ChromePath;
                TxtTarget.Text = cfg.TargetExePath;
                TxtTitleKeyword.Text = cfg.WindowTitleKeyword;
                TxtTextWhenChrome.Text = cfg.ButtonTextWhenChrome;
                TxtTextWhenTarget.Text = cfg.ButtonTextWhenTarget;
                TxtTextOther.Text = cfg.ButtonTextOther;
                SldOpacity.Value = cfg.ButtonOpacity;
                TxtOpacityValue.Text = cfg.ButtonOpacity.ToString("0.00", CultureInfo.InvariantCulture);
                CmbAnchor.SelectedIndex = (int)cfg.Anchor;
                TxtOffsetX.Text = ((int)Math.Round(cfg.OffsetX ?? cfg.EdgeMargin)).ToString(CultureInfo.InvariantCulture);
                TxtOffsetY.Text = ((int)Math.Round(cfg.OffsetY ?? cfg.EdgeMargin)).ToString(CultureInfo.InvariantCulture);
                ChkAutoStart.IsChecked = cfg.AutoStart;
                ChkStrict.IsChecked = cfg.StrictForegroundCheck;
                ChkHideWhenOther.IsChecked = cfg.HideButtonWhenOther;
                ChkIgnoreShell.IsChecked = cfg.IgnoreShellWindows;
                ChkHotkey.IsChecked = cfg.HotkeyEnabled;
                TxtHotkey.Text = cfg.HotkeyText;
                CmbChromeMode.SelectedIndex = IndexOfMode(cfg.ChromeMode);
                CmbTargetMode.SelectedIndex = IndexOfMode(cfg.TargetMode);
                TxtChromeRect.Text = cfg.ChromeRect;
                TxtTargetRect.Text = cfg.TargetRect;
                UpdatePositionStateText();
                CmbPrevious.SelectedIndex = (int)cfg.Previous;
            }
            catch (Exception ex)
            {
                Logger.Error("载入配置到界面失败", ex);
            }
        }

        private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (TxtOpacityValue != null)
                {
                    TxtOpacityValue.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
                }
            }
            catch { }
        }

        // ==================== 快捷键录入（v1.9） ====================

        private void TxtHotkey_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            try { TxtHotkey.SelectAll(); } catch { }
        }

        /// <summary>在框里直接按组合键，自动转成 "Ctrl+Alt+S" 这样的文本。</summary>
        private void TxtHotkey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;   // 不让按键传给其它控件（也避免 Tab 跳走）

            try
            {
                Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;

                if (key == Key.Back || key == Key.Delete)
                {
                    TxtHotkey.Text = string.Empty;
                    return;
                }

                // 只按修饰键：继续等主键，什么都不做
                if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                    key == Key.LeftAlt || key == Key.RightAlt ||
                    key == Key.LeftShift || key == Key.RightShift ||
                    key == Key.LWin || key == Key.RWin || key == Key.System || key == Key.None)
                {
                    return;
                }

                string text = HotkeySpec.Format(Keyboard.Modifiers, key);
                if (string.IsNullOrEmpty(text))
                {
                    MessageBox.Show(this,
                        "这个组合不能用。\r\n\r\n规则：必须带 Ctrl / Alt / Shift / Win 中的至少一个，" +
                        "或者单独用一个 F1~F12。\r\n例如：Ctrl+Alt+S、Ctrl+Shift+F9。",
                        "不支持该组合", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                TxtHotkey.Text = text;
                Logger.Info("配置界面：录入快捷键 = " + text);
            }
            catch (Exception ex)
            {
                Logger.Error("录入快捷键失败", ex);
            }
        }

        // ==================== 悬浮按钮位置：拖动记忆 / 复位（v1.8） ====================

        /// <summary>刷新"当前位置"提示文字（位置调整后会回来重新显示）。</summary>
        public void RefreshPositionState()
        {
            UpdatePositionStateText();
        }

        private void UpdatePositionStateText()
        {
            try
            {
                AppConfig cfg = App.Config;
                if (cfg.FreePosition && cfg.FreeX != null && cfg.FreeY != null)
                {
                    TxtPositionState.Text = "当前位置：调整后的位置 (X=" +
                        (int)Math.Round(cfg.FreeX.Value) + ", Y=" + (int)Math.Round(cfg.FreeY.Value) +
                        ")，上面的锚点/偏移暂时不生效。";
                }
                else
                {
                    TxtPositionState.Text = "点「调整位置」→ 配置窗口隐藏、按钮跟着鼠标走 → 单击一下确认（Esc 取消）。";
                }
            }
            catch { }
        }

        /// <summary>进入位置调整模式：隐藏配置窗口，悬浮按钮跟随鼠标。</summary>
        private void BtnPlaceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("配置界面：进入悬浮按钮位置调整模式");
                App.BeginPlacement();
            }
            catch (Exception ex)
            {
                Logger.Error("进入位置调整模式失败", ex);
                MessageBox.Show(this, ex.Message, "启动调整失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>清除"拖动记忆的位置"，回到锚点 + 偏移计算的位置。</summary>
        private void BtnResetPosition_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppConfig cfg = App.Config;
                cfg.FreePosition = false;
                cfg.FreeX = null;
                cfg.FreeY = null;
                cfg.Normalize();
                App.SaveConfig();

                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.ApplyConfig();
                    MainWindow.Instance.BringToFront();
                }

                UpdatePositionStateText();
                Logger.Info("配置界面：已复位悬浮按钮位置到锚点");
            }
            catch (Exception ex)
            {
                Logger.Error("复位悬浮按钮位置失败", ex);
                MessageBox.Show(this, ex.Message, "复位失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnBrowseChrome_Click(object sender, RoutedEventArgs e)
        {
            BrowseTo(TxtChrome, "选择 Chrome 主程序 (chrome.exe)");
        }

        private void BtnBrowseTarget_Click(object sender, RoutedEventArgs e)
        {
            BrowseTo(TxtTarget, "选择目标程序 EXE");
        }

        // ==================== 一键抓取当前窗口路径（v1.6） ====================

        private void BtnPickChrome_Click(object sender, RoutedEventArgs e)
        {
            PickPathFor(TxtChrome, "Chrome");
        }

        private void BtnPickTarget_Click(object sender, RoutedEventArgs e)
        {
            PickPathFor(TxtTarget, "目标程序");
        }

        /// <summary>
        /// 填入"最近一次出现在前台的窗口（本工具自己不算）"的进程完整路径。
        /// 为什么不用实时 GetForegroundWindow：点这个按钮本身就会让配置窗口变成前台窗口，
        /// 实时取到的会是小工具自己。所以用 ForegroundWatcher 记录下来的"最近一次真实程序"。
        /// </summary>
        private void PickPathFor(TextBox box, string what)
        {
            try
            {
                string path = ForegroundWatcher.LastForeignPath;

                if (string.IsNullOrEmpty(path) || PathHelper.Equals(path, AppPaths.ExePath))
                {
                    MessageBox.Show(this,
                        "还没记录到可用的前台窗口。\r\n\r\n用法：先把 " + what +
                        " 的窗口点到最前面（让它成为当前窗口），再回来点这个按钮。",
                        "读取失败", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 现场实测过：如果前台恰好是资源管理器/系统进程，会把 explorer.exe 填进来
                if (IsSystemProcess(path))
                {
                    MessageBox.Show(this,
                        "取到的是 Windows 系统进程（不是你要绑定的程序）：\r\n" + path +
                        "\r\n\r\n请先把 " + what + " 的窗口点到最前面（能看到它），再回来点这个按钮。",
                        "取到的不是目标程序", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                box.Text = path;
                Logger.Info("配置界面：按「取当前窗口」填入 " + what + " 路径 = " + path);

                string other = (ReferenceEquals(box, TxtChrome) ? TxtTarget.Text : TxtChrome.Text) ?? string.Empty;
                if (PathHelper.Equals(other, path))
                {
                    MessageBox.Show(this,
                        "注意：Chrome 路径和目标程序路径填成了同一个程序。\r\n" +
                        "请把另一个路径也改成对应程序的（先点它的窗口，再点对应的「取当前窗口」）。",
                        "两个路径重复", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("读取当前窗口路径失败", ex);
                MessageBox.Show(this, ex.Message, "读取失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BrowseTo(TextBox box, string title)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Title = title;
                dlg.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = true;

                string current = (box.Text ?? string.Empty).Trim().Trim('"');
                try
                {
                    if (current.Length > 0)
                    {
                        string dir = Path.GetDirectoryName(current);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                    }
                }
                catch { }

                bool? ok = dlg.ShowDialog(this);
                if (ok == true && !string.IsNullOrEmpty(dlg.FileName))
                {
                    box.Text = dlg.FileName;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("浏览文件失败", ex);
                MessageBox.Show(this, ex.Message, "选择文件失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string chrome = (TxtChrome.Text ?? string.Empty).Trim().Trim('"');
                string target = (TxtTarget.Text ?? string.Empty).Trim().Trim('"');
                string keyword = (TxtTitleKeyword.Text ?? string.Empty).Trim();

                if (chrome.Length == 0 && target.Length == 0)
                {
                    MessageBox.Show(this, "Chrome 路径和目标 EXE 路径至少需要填写一个。", "配置不完整",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!ConfirmPath(chrome, "Chrome")) return;
                if (!ConfirmPath(target, "目标 EXE")) return;

                double offsetX;
                double offsetY;
                if (!double.TryParse((TxtOffsetX.Text ?? string.Empty).Trim(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out offsetX) ||
                    !double.TryParse((TxtOffsetY.Text ?? string.Empty).Trim(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out offsetY))
                {
                    MessageBox.Show(this,
                        "X / Y 偏移必须是数字（例如 12；负值表示向任务栏方向压过去，可用于避开输入法状态条等浮窗）。",
                        "输入无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                AppConfig cfg = App.Config.Clone();
                cfg.ChromePath = chrome;
                cfg.TargetExePath = target;
                cfg.WindowTitleKeyword = keyword;
                cfg.ButtonTextWhenChrome = TxtTextWhenChrome.Text;
                cfg.ButtonTextWhenTarget = TxtTextWhenTarget.Text;
                cfg.ButtonTextOther = TxtTextOther.Text;
                cfg.ButtonOpacity = SldOpacity.Value;
                cfg.Anchor = (ButtonAnchor)Math.Max(0, CmbAnchor.SelectedIndex);
                cfg.OffsetX = offsetX;
                cfg.OffsetY = offsetY;
                cfg.AutoStart = ChkAutoStart.IsChecked == true;
                cfg.StrictForegroundCheck = ChkStrict.IsChecked == true;
                cfg.HideButtonWhenOther = ChkHideWhenOther.IsChecked == true;
                cfg.IgnoreShellWindows = ChkIgnoreShell.IsChecked == true;

                // 快捷键（v1.9）
                cfg.HotkeyEnabled = ChkHotkey.IsChecked == true;
                cfg.HotkeyText = (TxtHotkey.Text ?? string.Empty).Trim();
                if (cfg.HotkeyEnabled && !HotkeySpec.IsValid(cfg.HotkeyText))
                {
                    MessageBox.Show(this,
                        "快速切换快捷键填得不合法：" + (cfg.HotkeyText.Length == 0 ? "(空)" : cfg.HotkeyText) +
                        "\r\n\r\n点进那个框，直接按你想要的组合键即可。\r\n" +
                        "规则：必须带 Ctrl / Alt / Shift / Win 中至少一个，或单独一个 F1~F12。",
                        "快捷键不合法", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                cfg.ChromeMode = ModeValues[Math.Max(0, Math.Min(CmbChromeMode.SelectedIndex, ModeValues.Length - 1))];
                cfg.TargetMode = ModeValues[Math.Max(0, Math.Min(CmbTargetMode.SelectedIndex, ModeValues.Length - 1))];
                cfg.ChromeRect = (TxtChromeRect.Text ?? string.Empty).Trim();
                cfg.TargetRect = (TxtTargetRect.Text ?? string.Empty).Trim();

                // 选了「自定义区域」就必须给出合法的 x,y,w,h
                if (cfg.ChromeMode == WindowDisplayMode.CustomRect && !RectSpec.IsValid(cfg.ChromeRect))
                {
                    MessageBox.Show(this,
                        "Chrome 选了「自定义区域」，但区域填得不合法：\"" + cfg.ChromeRect + "\"\r\n\r\n" +
                        "格式：x,y,w,h（4 个值，可用像素或百分比）\r\n例：0,0,50%,100%（左半屏）、0,0,1280,720（固定尺寸）",
                        "区域格式不对", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (cfg.TargetMode == WindowDisplayMode.CustomRect && !RectSpec.IsValid(cfg.TargetRect))
                {
                    MessageBox.Show(this,
                        "目标 EXE 选了「自定义区域」，但区域填得不合法：\"" + cfg.TargetRect + "\"\r\n\r\n" +
                        "格式：x,y,w,h（4 个值，可用像素或百分比）\r\n例：50%,0,50%,100%（右半屏）",
                        "区域格式不对", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                cfg.Previous = (PreviousWindowAction)Math.Max(0, CmbPrevious.SelectedIndex);
                cfg.Normalize();

                App.Config = cfg;
                App.SaveConfig();
                App.ApplyAutoStart(cfg.AutoStart);

                // 立即应用悬浮按钮的文字 / 透明度 / 字号 / 位置
                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.ApplyConfig();
                    MainWindow.Instance.BringToFront();
                }

                TrayHelper.ShowInfo("配置已保存",
                    "已保存到：" + AppPaths.ConfigFile + "\r\n设置已立即生效。");
                Close();
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置出错", ex);
                MessageBox.Show(this, "保存失败：" + ex.Message + "\r\n详细信息见 app.log", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>路径不存在 / 不是 EXE 时给出确认提示（允许强行保存）。</summary>
        private bool ConfirmPath(string path, string what)
        {
            if (path.Length == 0) return true;

            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var r = MessageBox.Show(this,
                    what + " 路径不是 .exe 文件：\r\n" + path + "\r\n\r\n仍要保存吗？",
                    "路径可能无效", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                return r == MessageBoxResult.Yes;
            }

            if (!File.Exists(path))
            {
                var r = MessageBox.Show(this,
                    what + " 路径不存在：\r\n" + path +
                    "\r\n\r\n请确认是「完整启动路径」（绝对路径 + 文件名，如 C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe）。\r\n仍要保存吗？",
                    "路径可能无效", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                return r == MessageBoxResult.Yes;
            }

            return true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
