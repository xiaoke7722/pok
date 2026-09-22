using System;
using System.IO;
using System.Text;

namespace SelfSwitch
{
    /// <summary>
    /// 极简文件日志：写入 EXE 同目录 app.log，超过 2MB 自动轮转为 app.old.log。
    /// 任何日志异常都被吞掉，绝不因为写日志导致程序崩溃。
    /// </summary>
    public static class Logger
    {
        private static readonly object Sync = new object();
        private static string _path;
        private const long MaxBytes = 2 * 1024 * 1024;

        public static void Init(string path)
        {
            _path = path;
        }

        public static void Info(string message)
        {
            Write("INFO ", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN ", message, null);
        }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", message, ex);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                if (string.IsNullOrEmpty(_path)) return;
                lock (Sync)
                {
                    RollIfNeeded();
                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                      .Append(" [").Append(level).Append("] ")
                      .Append(message ?? string.Empty);
                    if (ex != null)
                    {
                        sb.AppendLine();
                        sb.Append("        ").Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
                        if (!string.IsNullOrEmpty(ex.StackTrace))
                        {
                            sb.AppendLine();
                            sb.Append(ex.StackTrace);
                        }
                    }
                    sb.AppendLine();
                    File.AppendAllText(_path, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch
            {
                // 日志失败静默
            }
        }

        private static void RollIfNeeded()
        {
            try
            {
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    string old = Path.Combine(fi.DirectoryName ?? ".", "app.old.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(_path, old);
                }
            }
            catch { }
        }
    }
}
