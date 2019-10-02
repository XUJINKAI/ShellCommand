using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShellCommand.DataModel
{
    static class DefaultSetting
    {
        public static DirectoryCommand[] GetDirectoryCommand()
        {
            return new DirectoryCommand[] {
                    new DirectoryCommand(){ Name = "Custom Command", Command = "cmd" },
                };
        }

        public static GlobalConfig GetGlobal()
        {
            return new GlobalConfig()
            {
                GlobalCommands = new List<DirectoryCommand>()
                {
                    new DirectoryCommand()
                    {
                        Command = "git pull",
                        Match = ".git",
                    },
                    new DirectoryCommand()
                    {
                        Command = "git submodule update --init --recursive",
                        Match = ".gitmodules",
                    },
                    new DirectoryCommand()
                    {
                        Name = "Open Command Window Here",
                        Command = "cmd",
                    },
                },
                Functions = new BuildinFunctions
                {
                    CopyPath = true,
                }
            };
        }
    }
}
