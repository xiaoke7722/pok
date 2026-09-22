using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SelfSwitch
{
    /// <summary>
    /// 窗口"显示方式"：Chrome 与目标 EXE 共用同一套（v1.7）。
    /// JSON 用名称序列化，历史取值 SendF11 / CoverTaskbar / Maximize / MaximizeCoverTaskbar / SendAltEnter
    /// 全部保留 —— 旧 config.json 直接兼容，顺序变化不影响。
    /// </summary>
    public enum WindowDisplayMode
    {
        SendF11 = 0,                // 发送 F11（Chrome 原生全屏，浏览器推荐）
        CoverTaskbar = 1,           // 铺满整屏并置顶（覆盖任务栏）
        Maximize = 2,               // 最大化（不覆盖任务栏）
        MaximizeCoverTaskbar = 3,   // 最大化后再铺满整屏（覆盖任务栏）
        Windowed = 4,               // 窗口化：恢复成普通窗口（保留原有尺寸位置）
        CustomRect = 5,             // 自定义区域：按 Rect 摆放（支持百分比，可做左右分屏）
        KeepCurrent = 6,            // 只切到前台，不改窗口大小与位置
        SendAltEnter = 7            // 发送 Alt+Enter（部分程序/游戏适用）
    }

    /// <summary>切换后对“当前窗口”的处理方式</summary>
    public enum PreviousWindowAction
    {
        Minimize = 0,        // 最小化（默认）
        StayInBackground = 1 // 留在后台（取消置顶并让出焦点）
    }

    /// <summary>悬浮按钮吸附到主屏工作区的哪个角</summary>
    public enum ButtonAnchor
    {
        BottomRight = 0,    // 右下角（默认）
        BottomLeft = 1,     // 左下角
        TopRight = 2,       // 右上角
        TopLeft = 3         // 左上角
    }

    /// <summary>本次配置加载过程中的修正记录（用于决定是否回写磁盘）。</summary>
    internal static class ConfigLoadReport
    {
        public static int EnumFallbacks;

        public static void Reset()
        {
            EnumFallbacks = 0;
        }
    }

    /// <summary>
    /// 宽松枚举转换器：遇到无法识别 / 越界的取值时，**只把该字段回落到默认值**，
    /// 其余字段照常加载，不再抛 JsonException 让整份配置作废。
    /// </summary>
    public sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string s = reader.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    try
                    {
                        object parsed = Enum.Parse(typeof(T), s.Trim(), true);
                        if (parsed != null && Enum.IsDefined(typeof(T), parsed))
                        {
                            return (T)parsed;
                        }
                    }
                    catch (ArgumentException) { }
                    catch (OverflowException) { }
                }
                ConfigLoadReport.EnumFallbacks++;
                Logger.Warn("配置项 " + typeof(T).Name + " 取值无法识别：\"" + s +
                            "\"，已回落默认值 " + default(T));
                return default(T);
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                try
                {
                    int n = reader.GetInt32();
                    if (Enum.IsDefined(typeof(T), n)) return (T)Enum.ToObject(typeof(T), n);
                }
                catch { }
                ConfigLoadReport.EnumFallbacks++;
                Logger.Warn("配置项 " + typeof(T).Name + " 数值越界，已回落默认值 " + default(T));
                return default(T);
            }

            // null / 数组 / 对象 等非法类型
            ConfigLoadReport.EnumFallbacks++;
            Logger.Warn("配置项 " + typeof(T).Name + " 类型不合法，已回落默认值 " + default(T));
            try { reader.Skip(); } catch { }
            return default(T);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    /// <summary>全部可配置项，保存为 config.json。</summary>
    public sealed class AppConfig
    {
        // ---------- 窗口绑定 ----------
        /// <summary>Chrome 完整启动路径</summary>
        public string ChromePath { get; set; } = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

        /// <summary>目标 EXE 完整启动路径</summary>
        public string TargetExePath { get; set; } = @"C:\Program Files\FleetView\FleetView.exe";

        /// <summary>
        /// 目标窗口标题关键字（可选）。留空 = 只用路径识别；
        /// 填写后要求窗口标题包含该关键字，用于区分同一程序的多个窗口。
        /// </summary>
        public string WindowTitleKeyword { get; set; } = "";

        // ---------- 悬浮按钮 ----------
        /// <summary>旧字段（v1.2 及以前）：单一按钮文字。仅为兼容旧 config.json 保留，不再使用。</summary>
        public string ButtonText { get; set; } = "切换";

        /// <summary>当前前台是 Chrome 时按钮显示的文字（点下去会切到目标程序）</summary>
        public string ButtonTextWhenChrome { get; set; } = "切到目标程序";

        /// <summary>当前前台是目标 EXE 时按钮显示的文字（点下去会切到 Chrome）</summary>
        public string ButtonTextWhenTarget { get; set; } = "切到浏览器";

        /// <summary>当前前台既不是 Chrome 也不是目标 EXE 时按钮显示的文字</summary>
        public string ButtonTextOther { get; set; } = "—";

        /// <summary>
        /// 其它程序在前台时自动隐藏悬浮按钮（默认开启）。
        /// 注意：严格模式关闭时该隐藏会被自动忽略——那种情况下按钮在"其它程序"状态下是有用的，
        /// 藏起来用户就点不到了。
        /// </summary>
        public bool HideButtonWhenOther { get; set; } = true;

        public double ButtonOpacity { get; set; } = 0.75;   // 0.3 ~ 1.0
        public double ButtonFontSize { get; set; } = 15;

        /// <summary>按钮吸附的角（默认右下角）</summary>
        public ButtonAnchor Anchor { get; set; } = ButtonAnchor.BottomRight;

        /// <summary>水平偏移（像素）：正值向屏幕内侧，负值可压到任务栏上。null → 用旧字段 EdgeMargin</summary>
        public double? OffsetX { get; set; } = null;

        /// <summary>垂直偏移（像素）：正值向屏幕内侧，负值可压到任务栏上。null → 用旧字段 EdgeMargin</summary>
        public double? OffsetY { get; set; } = null;

        /// <summary>旧字段（v1.4 及以前）：距右下角的单一边距。仅在 OffsetX/OffsetY 缺省时代入。</summary>
        public double EdgeMargin { get; set; } = 12;

        /// <summary>
        /// 是否使用"自由位置"（v1.8）：用户用鼠标把悬浮按钮拖到任意位置后置 true，
        /// 之后按钮就固定放在 FreeX/FreeY 处，不再按锚点+偏移计算。
        /// </summary>
        public bool FreePosition { get; set; } = false;

        /// <summary>自由位置的左上角 X（物理像素，屏幕坐标系）。null = 还没拖过。</summary>
        public double? FreeX { get; set; } = null;

        /// <summary>自由位置的左上角 Y（物理像素，屏幕坐标系）。null = 还没拖过。</summary>
        public double? FreeY { get; set; } = null;

        // ---------- 全屏方式 ----------
        /// <summary>Chrome 显示方式（默认发 F11，浏览器原生全屏）</summary>
        public WindowDisplayMode ChromeMode { get; set; } = WindowDisplayMode.SendF11;

        /// <summary>目标 EXE 显示方式（默认最大化，不覆盖任务栏）</summary>
        public WindowDisplayMode TargetMode { get; set; } = WindowDisplayMode.Maximize;

        /// <summary>
        /// Chrome 自定义区域（仅在 ChromeMode = CustomRect 时使用）。
        /// 格式 "x,y,w,h"，每项可为像素或百分比，例："0,0,50%,100%" = 左半屏。
        /// </summary>
        public string ChromeRect { get; set; } = RectSpec.DefaultChrome;

        /// <summary>
        /// 目标 EXE 自定义区域（仅在 TargetMode = CustomRect 时使用）。
        /// 默认 "50%,0,50%,100%" = 右半屏，与 Chrome 的左半屏组成左右分屏。
        /// </summary>
        public string TargetRect { get; set; } = RectSpec.DefaultTarget;

        /// <summary>
        /// 严格模式（默认开启）：只在前台窗口是 Chrome 或目标 EXE 时才执行切换。
        /// 当前台是其它程序时，点悬浮按钮**不切换也不提示**，只在 app.log 留一行记录。
        /// </summary>
        public bool StrictForegroundCheck { get; set; } = true;

        /// <summary>
        /// 是否启用"快速切换"全局快捷键（v1.9）。
        /// 快捷键与点击悬浮按钮执行**同一套逻辑**（一样受严格模式、防重入、节流约束）。
        /// </summary>
        public bool HotkeyEnabled { get; set; } = true;

        /// <summary>快捷键组合，例如 "Ctrl+Alt+S"。必须含 Ctrl/Alt/Shift/Win，或单独一个 F1~F24。</summary>
        public string HotkeyText { get; set; } = HotkeySpec.Default;

        /// <summary>
        /// 忽略系统"壳窗口"（通知横幅、任务栏、操作中心、托盘旋出窗）对状态的影响（默认开启）。
        /// 这些窗口抢前台时，不改变按钮文字/显隐，也不影响切换方向判断。
        /// 否则一条系统通知就会把按钮藏起来、让严格模式误判为"其它程序"而静默忽略点击。
        /// </summary>
        public bool IgnoreShellWindows { get; set; } = true;

        // ---------- 行为 ----------
        /// <summary>切换时对当前窗口的处理</summary>
        public PreviousWindowAction Previous { get; set; } = PreviousWindowAction.Minimize;

        /// <summary>是否开机自启（写入 HKCU\...\Run）</summary>
        public bool AutoStart { get; set; } = true;

        // ---------------- 序列化 ----------------

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文不转义
            Converters =
            {
                // 用宽松转换器替代 JsonStringEnumConverter：未知值只回落单项，不丢整份配置
                new LenientEnumConverter<WindowDisplayMode>(),
                new LenientEnumConverter<PreviousWindowAction>(),
                new LenientEnumConverter<ButtonAnchor>()
            }
        };

        public AppConfig Clone()
        {
            return (AppConfig)MemberwiseClone();
        }

        /// <summary>修正越界值，保证配置永远合法。</summary>
        public void Normalize()
        {
            ChromePath = (ChromePath ?? string.Empty).Trim().Trim('"');
            TargetExePath = (TargetExePath ?? string.Empty).Trim().Trim('"');
            WindowTitleKeyword = (WindowTitleKeyword ?? string.Empty).Trim();

            ButtonText = (ButtonText ?? string.Empty).Trim();
            ButtonTextWhenChrome = (ButtonTextWhenChrome ?? string.Empty).Trim();
            ButtonTextWhenTarget = (ButtonTextWhenTarget ?? string.Empty).Trim();
            ButtonTextOther = (ButtonTextOther ?? string.Empty).Trim();
            if (ButtonTextWhenChrome.Length == 0) ButtonTextWhenChrome = "切到目标程序";
            if (ButtonTextWhenTarget.Length == 0) ButtonTextWhenTarget = "切到浏览器";
            if (ButtonTextOther.Length == 0) ButtonTextOther = "—";
            if (ButtonText.Length == 0) ButtonText = "切换";

            if (ButtonOpacity < 0.3) ButtonOpacity = 0.3;
            if (ButtonOpacity > 1.0) ButtonOpacity = 1.0;
            if (ButtonFontSize < 8) ButtonFontSize = 8;
            if (ButtonFontSize > 48) ButtonFontSize = 48;

            if (EdgeMargin < 0) EdgeMargin = 0;
            if (EdgeMargin > 500) EdgeMargin = 500;

            // 旧配置迁移：没有 OffsetX/OffsetY 时用 EdgeMargin 代入
            if (OffsetX == null) OffsetX = EdgeMargin;
            if (OffsetY == null) OffsetY = EdgeMargin;
            if (OffsetX < -1000) OffsetX = -1000;
            if (OffsetX > 2000) OffsetX = 2000;
            if (OffsetY < -1000) OffsetY = -1000;
            if (OffsetY > 2000) OffsetY = 2000;

            // 自由位置：只有在 X/Y 都有效时才认为可用（否则回头走锚点逻辑）
            if (FreeX == null || FreeY == null)
            {
                FreePosition = false;
            }
            else
            {
                if (FreeX < -10000) FreeX = -10000;
                if (FreeX > 20000) FreeX = 20000;
                if (FreeY < -10000) FreeY = -10000;
                if (FreeY > 20000) FreeY = 20000;
            }

            if (!Enum.IsDefined(typeof(WindowDisplayMode), ChromeMode)) ChromeMode = WindowDisplayMode.SendF11;
            if (!Enum.IsDefined(typeof(WindowDisplayMode), TargetMode)) TargetMode = WindowDisplayMode.Maximize;

            ChromeRect = (ChromeRect ?? string.Empty).Trim();
            TargetRect = (TargetRect ?? string.Empty).Trim();
            if (!RectSpec.IsValid(ChromeRect))
            {
                Logger.Warn("ChromeRect 取值不合法：\"" + ChromeRect + "\"，已回落默认 " + RectSpec.DefaultChrome);
                ChromeRect = RectSpec.DefaultChrome;
            }
            if (!RectSpec.IsValid(TargetRect))
            {
                Logger.Warn("TargetRect 取值不合法：\"" + TargetRect + "\"，已回落默认 " + RectSpec.DefaultTarget);
                TargetRect = RectSpec.DefaultTarget;
            }
            if (!Enum.IsDefined(typeof(PreviousWindowAction), Previous)) Previous = PreviousWindowAction.Minimize;
            if (!Enum.IsDefined(typeof(ButtonAnchor), Anchor)) Anchor = ButtonAnchor.BottomRight;

            // 快捷键：格式不合法就回落默认
            HotkeyText = (HotkeyText ?? string.Empty).Trim();
            if (!HotkeySpec.IsValid(HotkeyText))
            {
                Logger.Warn("HotkeyText 取值不合法：\"" + HotkeyText + "\"，已回落默认 " + HotkeySpec.Default);
                HotkeyText = HotkeySpec.Default;
            }
        }

        public static AppConfig Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    var def = new AppConfig();
                    def.Normalize();
                    def.Save(path);
                    Logger.Info("配置文件不存在，已生成默认配置（首次运行）: " + path);
                    return def;
                }

                ConfigLoadReport.Reset();
                string json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (cfg == null)
                {
                    cfg = new AppConfig();
                    ConfigLoadReport.EnumFallbacks++;
                    Logger.Warn("配置内容为空，已按默认值处理: " + path);
                }

                string before = JsonSerializer.Serialize(cfg, JsonOptions);
                cfg.Normalize();
                string after = JsonSerializer.Serialize(cfg, JsonOptions);

                // 解析时有字段回落、或归一化改动了取值 → 把修正后的合法配置回写磁盘，
                // 避免"每次启动都报同一个错"，也保证磁盘内容与内存一致。
                if (ConfigLoadReport.EnumFallbacks > 0 || !string.Equals(before, after, StringComparison.Ordinal))
                {
                    try
                    {
                        File.WriteAllText(path, after, new System.Text.UTF8Encoding(false));
                        Logger.Info("配置中的非法/越界取值已修正并回写磁盘: " + path);
                    }
                    catch (Exception ex2)
                    {
                        Logger.Warn("回写修正后的配置失败（不影响本次运行）: " + ex2.Message);
                    }
                }

                Logger.Info("已载入配置: " + path);
                return cfg;
            }
            catch (Exception ex)
            {
                Logger.Error("读取配置失败（文件已损坏），已备份并回退默认配置: " + path, ex);
                try
                {
                    string bad = path + ".bad";
                    if (File.Exists(path) && !File.Exists(bad)) File.Move(path, bad);
                }
                catch { /* 备份失败不影响启动 */ }

                var def = new AppConfig();
                def.Normalize();
                try
                {
                    // 立刻落盘默认值：否则下次启动又会走"文件不存在"分支，日志语义混乱
                    def.Save(path);
                    Logger.Warn("已备份损坏配置为 .bad 并生成默认配置，请在配置界面重新填写程序路径");
                }
                catch { }
                return def;
            }
        }

        public void Save(string path)
        {
            try
            {
                Normalize();
                string json = JsonSerializer.Serialize(this, JsonOptions);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
                Logger.Info("配置已保存: " + path);
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置失败: " + path, ex);
                throw;
            }
        }
    }
}
