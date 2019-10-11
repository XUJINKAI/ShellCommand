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
            return CreateMenu(FolderPath, CommandFilePath);
        }

        internal static ContextMenuStrip CreateMenu(string workingDir, string CommandFilePath)
        {
            var menu = new ContextMenuStrip();
            var container = new ToolStripMenuItem(Env.MenuDisplay);

            var list = new List<ToolStripItem>();

            MenuItemsBuilder.ParseDirectoryCommandInto(list, CommandFilePath, workingDir);
            MenuItemsBuilder.AddSeparator(list);

            var globalSettingPath = Path.Combine(Env.GetAppFolder(), Env.GlobalSettingFileName);
            if (File.Exists(globalSettingPath))
            {
                MenuItemsBuilder.ParseGlobalCommandInto(list, globalSettingPath, workingDir);
            }

            list.Add(BuildinMenuItems.OpenApp());
            if (!File.Exists(CommandFilePath))
            {
                MenuItemsBuilder.AddSeparator(list);
                list.Add(BuildinMenuItems.InitDirectoryFile(CommandFilePath));
            }

            container.DropDownItems.AddRange(list.ToArray());
            menu.Items.Add(container);
            return menu;
        }
    }
}
