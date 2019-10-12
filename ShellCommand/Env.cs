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
        public const string MenuDisplay = AppName + "(&C)";
        public const string CommandFileName = ".shellcommand.yaml";
        public const string GlobalSettingFileName= "global.shellcommand.yaml";
        public const string GlobalTemplateSettingFileName = "global.template.shellcommand.yaml";

        public const string SrmExeName = "ServerRegistrationManager.exe";
        public const string LogFileName = "log.txt";

        public const string CreateFolderSpecificFileText = "Create .shellcommand.yaml";
        public const string OpenGlobalSettingFileText = "Edit Global Setting";
        public const string OpenAppText = "Open ShellCommand";

        public const string ConfigTitle = "# ShellCommand: https://github.com/XUJINKAI/ShellCommand \r\n\r\n";

        public const string VAR_DIR = "%DIR%";
        public const string VAR_SEPNAME = "---";
    }
}
