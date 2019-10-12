using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ShellCommand.Util
{
    class NativeLoader
    {
        public static Image LocaImage(string iconPath)
        {
            try
            {
                if (iconPath.Contains("?"))
                {
                    var splits = iconPath.Split('?');
                    if (splits.Length != 2) throw new Exception("Icon Path can only have ONE '?' to select index.");
                    var path = splits[0];
                    var index = int.Parse(splits[1]);
                    return ExtractDllIcon(path, index, false).ToBitmap();
                }
                var bitmap = Icon.ExtractAssociatedIcon(iconPath).ToBitmap();
                bitmap = new Bitmap(bitmap, new Size(24, 24));
                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                return null;
            }
        }

        public static Image GetShell32Icon(int number)
        {
            var shell32 = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\Shell32.dll");
            return ExtractDllIcon(shell32, number, false).ToBitmap();
        }

        public static Icon ExtractDllIcon(string file, int number, bool largeIcon)
        {
            ExtractIconEx(file, number, out IntPtr large, out IntPtr small, 1);
            try
            {
                return Icon.FromHandle(largeIcon ? large : small);
            }
            catch
            {
                return null;
            }

        }

        [DllImport("Shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern int ExtractIconEx(string sFile, int iIndex, out IntPtr piLargeVersion, out IntPtr piSmallVersion, int amountIcons);
    }
}
