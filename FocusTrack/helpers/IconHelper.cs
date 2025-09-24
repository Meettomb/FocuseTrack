using System;
using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FocusTrack.Helpers
{
    public static class IconHelper
    {
        public static byte[] GetIconBytes(string exePath)
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(exePath))
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        icon.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static ImageSource BytesToImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;

            using (var ms = new MemoryStream(bytes))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad; // important to release stream
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze(); // make it cross-thread safe
                return image;
            }
        }
    }
}
