using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ShellCommand.Util
{
    static class Reg
    {
        const string ExePath = "ExePath";
        const string SUBKEY = @"SOFTWARE\SharpShell";

        public static void DeleteSubKey()
        {
            Registry.LocalMachine.DeleteSubKey(SUBKEY, false);
        }

        public static void SetLogPath()
        {
            using (var key = Registry.LocalMachine.CreateSubKey(SUBKEY))
            {
                key.SetValue("LogPath", Env.GetLogPath(), RegistryValueKind.String);
            }
        }

        public static void SetExePath(string path)
        {
            using (var key = Registry.LocalMachine.CreateSubKey(SUBKEY))
            {
                key.SetValue(ExePath, path, RegistryValueKind.String);
            }
        }

        public static string GetExePath()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(SUBKEY))
            {
                return key.GetValue(ExePath) as string;
            }
        }

        public static string GetAppFolderPath()
        {
            return Path.GetDirectoryName(GetExePath());
        }
    }
}
