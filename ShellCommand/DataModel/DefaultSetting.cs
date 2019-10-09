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
    }
}
