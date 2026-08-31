using System;
using System.IO;

namespace SwCheckinConflictButtonAddin
{
    internal static class AddinLog
    {
        private static readonly string FilePath =
            Path.Combine(Path.GetTempPath(), "SwCheckinConflictButtonAddin.log");

        public static void Info(string message)
        {
            try
            {
                File.AppendAllText(
                    FilePath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch
            {
                // 日志失败不影响插件
            }
        }
    }
}
