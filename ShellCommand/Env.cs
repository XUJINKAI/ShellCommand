using ShellCommand.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ShellCommand
{
    public static class Env
    {
        public const string AppName = "ShellCommand";
        public const string CommandFileName = ".shellcommand.yaml";
        public const string GlobalSettingFileName= "global.shellcommand.yaml";
        public const string GlobalTemplateSettingFileName = "global.template.shellcommand.yaml";

        public const string CreateFolderSpecificFileText = "Create .shellcommand.yaml";
        public const string OpenGlobalSettingFileText = "Edit Global Setting";
        public const string OpenAppText = "Setting";

        public const string ConfigTitle = "# ShellCommand: https://github.com/XUJINKAI/ShellCommand \r\n\r\n";

        public const string VAR_DIR = "%DIR%";

        public static string GetAppFolder()
        {
            var path = Reg.GetExePath();
            return Path.GetDirectoryName(path);
        }

        public static string GetSrmPath()
        {
            return Path.Combine(GetAppFolder(), "ServerRegistrationManager.exe");
        }

        public static string GetLogPath()
        {
            var appfolder = GetAppFolder();
            return Path.Combine(appfolder, "log.txt");
        }
    }
}
