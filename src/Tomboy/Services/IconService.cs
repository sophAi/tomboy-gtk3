using System;
using System.IO;
using System.Reflection;
using Gdk;

namespace Tomboy.Services
{
    public static class IconService
    {
        private static readonly Assembly CurrentAssembly = Assembly.GetExecutingAssembly();

        /// <summary>
        /// Loads an icon from GTK IconTheme or embedded resources / local assets matching Tomboy's GuiUtils.GetIcon.
        /// </summary>
        public static Pixbuf? GetIcon(string resourceName, int size = 22)
        {
            // 1. Try GTK Icon Theme
            try
            {
                var icon = Gtk.IconTheme.Default.LoadIcon(resourceName, size, 0);
                if (icon != null) return icon;
            }
            catch { }

            // 2. Try Embedded Resource
            try
            {
                string resPath = $"Tomboy.Resources.icons.{resourceName}.png";
                using Stream? stream = CurrentAssembly.GetManifestResourceStream(resPath);
                if (stream != null)
                {
                    var pb = new Pixbuf(stream);
                    if (pb.Width != size || pb.Height != size)
                    {
                        return pb.ScaleSimple(size, size, InterpType.Bilinear);
                    }
                    return pb;
                }
            }
            catch { }

            // 3. Try Local File System
            try
            {
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "icons", $"{resourceName}.png");
                if (File.Exists(localPath))
                {
                    var pb = new Pixbuf(localPath);
                    if (pb.Width != size || pb.Height != size)
                    {
                        return pb.ScaleSimple(size, size, InterpType.Bilinear);
                    }
                    return pb;
                }
            }
            catch { }

            return null;
        }
    }
}
