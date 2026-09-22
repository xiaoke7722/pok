using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace SelfSwitch
{
    /// <summary>
    /// 全局快捷键的"字符串 ↔ 修饰键+VK 码"转换（v1.9）。
    ///
    /// 配置里存成可读字符串，例如 "Ctrl+Alt+S"、"Ctrl+Shift+F9"、"F9"。
    /// 规则：必须至少含一个修饰键（Ctrl/Alt/Shift/Win），或者主体是 F1~F24。
    /// </summary>
    public static class HotkeySpec
    {
        public const string Default = "Ctrl+Alt+S";

        /// <summary>键名 ↔ VK 码（用于反查显示名）。</summary>
        private static readonly KeyValuePair<uint, string>[] NamedKeys =
        {
            new KeyValuePair<uint, string>(0x20, "Space"),
            new KeyValuePair<uint, string>(0x0D, "Enter"),
            new KeyValuePair<uint, string>(0x09, "Tab"),
            new KeyValuePair<uint, string>(0x1B, "Esc"),
            new KeyValuePair<uint, string>(0x08, "Backspace"),
            new KeyValuePair<uint, string>(0x2D, "Insert"),
            new KeyValuePair<uint, string>(0x2E, "Delete"),
            new KeyValuePair<uint, string>(0x24, "Home"),
            new KeyValuePair<uint, string>(0x23, "End"),
            new KeyValuePair<uint, string>(0x21, "PageUp"),
            new KeyValuePair<uint, string>(0x22, "PageDown"),
            new KeyValuePair<uint, string>(0x26, "Up"),
            new KeyValuePair<uint, string>(0x28, "Down"),
            new KeyValuePair<uint, string>(0x25, "Left"),
            new KeyValuePair<uint, string>(0x27, "Right"),
            new KeyValuePair<uint, string>(0xBD, "-"),
            new KeyValuePair<uint, string>(0xBB, "="),
            new KeyValuePair<uint, string>(0xDB, "["),
            new KeyValuePair<uint, string>(0xDD, "]"),
            new KeyValuePair<uint, string>(0xDC, "\\"),
            new KeyValuePair<uint, string>(0xBA, ";"),
            new KeyValuePair<uint, string>(0xDE, "'"),
            new KeyValuePair<uint, string>(0xBC, ","),
            new KeyValuePair<uint, string>(0xBE, "."),
            new KeyValuePair<uint, string>(0xBF, "/"),
            new KeyValuePair<uint, string>(0xC0, "`")
        };

        /// <summary>解析配置里的快捷键字符串。</summary>
        public static bool TryParse(string text, out uint modifiers, out uint vk, out string normalized)
        {
            modifiers = 0;
            vk = 0;
            normalized = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Split(new[] { '+', '＋' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            uint mods = 0;
            uint key = 0;
            bool hasKey = false;

            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;

                switch (p.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                    case "ctl":
                        mods |= Native.MOD_CONTROL;
                        continue;
                    case "alt":
                    case "menu":
                        mods |= Native.MOD_ALT;
                        continue;
                    case "shift":
                        mods |= Native.MOD_SHIFT;
                        continue;
                    case "win":
                    case "windows":
                    case "cmd":
                    case "super":
                        mods |= Native.MOD_WIN;
                        continue;
                }

                if (hasKey) return false;      // 出现两个主键 → 非法

                uint v;
                if (!TryKeyToVk(p, out v)) return false;
                key = v;
                hasKey = true;
            }

            if (!hasKey) return false;
            if (mods == 0 && !IsFunctionKey(key)) return false;   // 裸键只允许 F1~F24

            modifiers = mods;
            vk = key;
            normalized = Format(mods, key);
            return true;
        }

        public static bool IsValid(string text)
        {
            uint m, v;
            string n;
            return TryParse(text, out m, out v, out n);
        }

        /// <summary>把 VK + 修饰键格式化成规范字符串：Ctrl+Alt+Shift+Win+Key。</summary>
        public static string Format(uint modifiers, uint vk)
        {
            var sb = new System.Text.StringBuilder();
            if ((modifiers & Native.MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((modifiers & Native.MOD_ALT) != 0) sb.Append("Alt+");
            if ((modifiers & Native.MOD_SHIFT) != 0) sb.Append("Shift+");
            if ((modifiers & Native.MOD_WIN) != 0) sb.Append("Win+");
            sb.Append(VkToName(vk));
            return sb.ToString();
        }

        /// <summary>配置界面捕获按键时用：WPF 的 ModifierKeys + Key → 规范字符串。</summary>
        public static string Format(ModifierKeys mods, Key key)
        {
            uint m = 0;
            if ((mods & ModifierKeys.Control) != 0) m |= Native.MOD_CONTROL;
            if ((mods & ModifierKeys.Alt) != 0) m |= Native.MOD_ALT;
            if ((mods & ModifierKeys.Shift) != 0) m |= Native.MOD_SHIFT;
            if ((mods & ModifierKeys.Windows) != 0) m |= Native.MOD_WIN;

            uint vk;
            if (!KeyToVk(key, out vk)) return null;
            if (m == 0 && !IsFunctionKey(vk)) return null;   // 单按一个普通键不接受

            return Format(m, vk);
        }

        // ---------------- 内部映射 ----------------

        private static bool IsFunctionKey(uint vk)
        {
            return vk >= 0x70 && vk <= 0x87;   // F1 ~ F24
        }

        private static string VkToName(uint vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();          // A-Z
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();          // 0-9
            if (vk >= 0x60 && vk <= 0x69) return "NumPad" + (vk - 0x60);         // 小键盘
            if (IsFunctionKey(vk)) return "F" + (vk - 0x70 + 1);
            for (int i = 0; i < NamedKeys.Length; i++)
            {
                if (NamedKeys[i].Key == vk) return NamedKeys[i].Value;
            }
            return "0x" + vk.ToString("X2");
        }

        private static bool TryKeyToVk(string token, out uint vk)
        {
            vk = 0;
            if (string.IsNullOrEmpty(token)) return false;

            // 单字符：字母 / 数字 / 常见符号
            if (token.Length == 1)
            {
                char c = token[0];
                if (c >= 'a' && c <= 'z') { vk = (uint)(c - 32); return true; }
                if (c >= 'A' && c <= 'Z') { vk = (uint)c; return true; }
                if (c >= '0' && c <= '9') { vk = (uint)c; return true; }
                for (int i = 0; i < NamedKeys.Length; i++)
                {
                    if (NamedKeys[i].Value == token) { vk = NamedKeys[i].Key; return true; }
                }
                return false;
            }

            // 名字（不区分大小写）
            string low = token.ToLowerInvariant();
            if (low.StartsWith("f") && low.Length >= 2)
            {
                int n;
                if (int.TryParse(low.Substring(1), out n) && n >= 1 && n <= 24)
                {
                    vk = (uint)(0x70 + n - 1);
                    return true;
                }
            }
            if (low.StartsWith("numpad") && low.Length > 6)
            {
                int n;
                if (int.TryParse(low.Substring(6), out n) && n >= 0 && n <= 9)
                {
                    vk = (uint)(0x60 + n);
                    return true;
                }
            }
            for (int i = 0; i < NamedKeys.Length; i++)
            {
                if (string.Equals(NamedKeys[i].Value, token, StringComparison.OrdinalIgnoreCase))
                {
                    vk = NamedKeys[i].Key;
                    return true;
                }
            }
            if (low == "return") { vk = 0x0D; return true; }
            if (low == "escape") { vk = 0x1B; return true; }
            if (low == "del") { vk = 0x2E; return true; }
            if (low == "ins") { vk = 0x2D; return true; }
            return false;
        }

        private static bool KeyToVk(Key key, out uint vk)
        {
            vk = 0;
            int k = (int)key;   // 枚举不支持比较运算符，转 int 再比
            if (k >= (int)Key.A && k <= (int)Key.Z) { vk = (uint)('A' + (k - (int)Key.A)); return true; }
            if (k >= (int)Key.D0 && k <= (int)Key.D9) { vk = (uint)('0' + (k - (int)Key.D0)); return true; }
            if (k >= (int)Key.NumPad0 && k <= (int)Key.NumPad9) { vk = (uint)(0x60 + (k - (int)Key.NumPad0)); return true; }
            if (k >= (int)Key.F1 && k <= (int)Key.F24) { vk = (uint)(0x70 + (k - (int)Key.F1)); return true; }

            switch (key)
            {
                case Key.Space: vk = 0x20; return true;
                case Key.Enter: vk = 0x0D; return true;
                case Key.Tab: vk = 0x09; return true;
                case Key.Escape: vk = 0x1B; return true;
                case Key.Back: vk = 0x08; return true;
                case Key.Insert: vk = 0x2D; return true;
                case Key.Delete: vk = 0x2E; return true;
                case Key.Home: vk = 0x24; return true;
                case Key.End: vk = 0x23; return true;
                case Key.PageUp: vk = 0x21; return true;
                case Key.PageDown: vk = 0x22; return true;
                case Key.Up: vk = 0x26; return true;
                case Key.Down: vk = 0x28; return true;
                case Key.Left: vk = 0x25; return true;
                case Key.Right: vk = 0x27; return true;
                case Key.OemMinus: vk = 0xBD; return true;
                case Key.OemPlus: vk = 0xBB; return true;
                case Key.OemOpenBrackets: vk = 0xDB; return true;
                case Key.OemCloseBrackets: vk = 0xDD; return true;
                case Key.OemPipe: vk = 0xDC; return true;
                case Key.OemSemicolon: vk = 0xBA; return true;
                case Key.OemQuotes: vk = 0xDE; return true;
                case Key.OemComma: vk = 0xBC; return true;
                case Key.OemPeriod: vk = 0xBE; return true;
                case Key.OemQuestion: vk = 0xBF; return true;
                case Key.OemTilde: vk = 0xC0; return true;
            }
            return false;
        }
    }
}
