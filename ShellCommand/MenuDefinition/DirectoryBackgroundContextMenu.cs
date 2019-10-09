using SharpShell.Attributes;
using SharpShell.SharpContextMenu;
using ShellCommand.DataModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using XJK.SysX;
using XJK.SysX.CommandHelper;

namespace ShellCommand.MenuDefinition
{
    [ComVisible(true)]
    [COMServerAssociation(AssociationType.DirectoryBackground)]
    public class DirectoryBackgroundContextMenu : SharpContextMenu
    {
        public DirectoryBackgroundContextMenu() { }

        string CommandFilePath => Path.Combine(FolderPath, Env.CommandFileName);

        protected override bool CanShowMenu()
        {
            return true;
        }

        protected override ContextMenuStrip CreateMenu()
        {
            var menu = new ContextMenuStrip();
            var container = new ToolStripMenuItem(Env.AppName);

            var fileItems = CommandFileParser.ParseDirectoryCommand(CommandFilePath, FolderPath);
            if (fileItems.Count > 0)
            {
                container.DropDownItems.AddRange(fileItems.ToArray());
                container.DropDownItems.Add(new ToolStripSeparator());
            }

            var globalSettingPath = Path.Combine(Env.GetAppFolder(), Env.GlobalSettingFileName);
            if (File.Exists(globalSettingPath))
            {
                var globalItems = CommandFileParser.ParseGlobalCommand(globalSettingPath, FolderPath);
                if (globalItems.Count > 0)
                {
                    container.DropDownItems.AddRange(globalItems.ToArray());
                    container.DropDownItems.Add(new ToolStripSeparator());
                }
            }

            if (!File.Exists(CommandFilePath))
            {
                container.DropDownItems.Add(BuildinMenuItems.InitDirectoryFile(CommandFilePath));
            }

            container.DropDownItems.Add(BuildinMenuItems.OpenGlobalSetting());
            container.DropDownItems.Add(BuildinMenuItems.OpenApp());

            menu.Items.Add(container);
            return menu;
        }
    }
}
