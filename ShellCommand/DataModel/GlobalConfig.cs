using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShellCommand.DataModel
{
    public class GlobalConfig
    {
        public List<DirectoryCommand> GlobalCommands { get; set; }

        public BuildinFunctions Functions { get; set; }
    }

    public class BuildinFunctions
    {
        public bool CopyPath { get; set; }
    }
}
