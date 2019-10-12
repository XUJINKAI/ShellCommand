using SharpShell.Attributes;
using SharpShell.SharpContextMenu;
using ShellCommand.DataModel;
using ShellCommand.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShellCommand.MenuDefinition
{
    [ComVisible(true)]
    [COMServerAssociation(AssociationType.DirectoryBackground)]
    public class DirectoryBackgroundContextMenu : SharpContextMenu
    {
        public DirectoryBackgroundContextMenu() { }

        protected override bool CanShowMenu()
        {
            return true;
        }

        protected override ContextMenuStrip CreateMenu()
        {
            var directoryFilePath = Path.Combine(FolderPath, Env.CommandFileName);
            var globalSettingPath = Path.Combine(Reg.GetAppFolder(), Env.GlobalSettingFileName);
            return MenuCreator.Create(FolderPath, directoryFilePath, globalSettingPath);
        }
    }
}
