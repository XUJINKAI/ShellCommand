using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShellCommand.Util
{
    static class Logger
    {
        public static void LogException(Exception ex)
        {
            var logPath = Reg.GetLogPath();
            File.AppendAllText(logPath, $"{Environment.NewLine}{ex.Message}{Environment.NewLine}{ex.StackTrace}");
        }
    }
}
