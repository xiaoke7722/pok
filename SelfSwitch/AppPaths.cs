using System;
using System.IO;

namespace SelfSwitch
{
    /// <summary>
    /// 配置与日志的落盘位置。
    /// 规则：优先和 EXE 同目录（便携）；若该目录不可写（例如放在 Program Files），
    /// 自动回落到 %AppData%\SelfSwitch\，保证程序永远能保存配置。
    /// </summary>
    public static class AppPaths
    {
        public static string ExePath { get; private set; }
        public static string ExeDir { get; private set; }

        /// <summary>实际使用的数据目录（config.json / app.log 都在这里）。</summary>
        public static string DataDir { get; private set; }

        /// <summary>是否因为 EXE 目录不可写而回落到 %AppData%。</summary>
        public static bool UsedFallback { get; private set; }

        public static string ConfigFile { get { return Path.Combine(DataDir, "config.json"); } }
        public static string LogFile { get { return Path.Combine(DataDir, "app.log"); } }

        public static void Resolve()
        {
            ExePath = ResolveExePath();
            try
            {
                ExeDir = Path.GetDirectoryName(ExePath);
            }
            catch { ExeDir = null; }
            if (string.IsNullOrEmpty(ExeDir)) ExeDir = AppContext.BaseDirectory;

            ExeDir = ExeDir.TrimEnd('\\');

            // 1) EXE 同目录可写 → 直接用（便携、符合"与 EXE 同目录"的原始要求）
            if (IsWritable(ExeDir))
            {
                DataDir = ExeDir;
                UsedFallback = false;
                return;
            }

            // 2) 回落到 %AppData%\SelfSwitch\
            UsedFallback = true;
            string appData = null;
            try
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            catch { }

            if (string.IsNullOrEmpty(appData))
            {
                DataDir = ExeDir; // 极端情况：还是没有，只能硬着头皮用 EXE 目录
                return;
            }

            string fallback = Path.Combine(appData, "SelfSwitch");
            try
            {
                Directory.CreateDirectory(fallback);
                if (IsWritable(fallback))
                {
                    DataDir = fallback;

                    // 若 EXE 目录里已经有一份旧的 config.json，搬到新目录，避免"配置突然变默认"
                    try
                    {
                        string oldCfg = Path.Combine(ExeDir, "config.json");
                        string newCfg = Path.Combine(fallback, "config.json");
                        if (File.Exists(oldCfg) && !File.Exists(newCfg))
                        {
                            File.Copy(oldCfg, newCfg);
                        }
                    }
                    catch { }
                    return;
                }
            }
            catch { }

            DataDir = ExeDir;
        }

        /// <summary>目录是否可写（真写一个临时文件再删掉，比查 ACL 可靠）。</summary>
        public static bool IsWritable(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            string probe = Path.Combine(dir, ".selfswitch_write_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                try { if (File.Exists(probe)) File.Delete(probe); } catch { }
                return false;
            }
        }

        private static string ResolveExePath()
        {
            try
            {
                string p = Environment.ProcessPath; // 单文件发布下即 EXE 自身
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch { }
            return Path.Combine(AppContext.BaseDirectory, "SelfSwitch.exe");
        }
    }
}
