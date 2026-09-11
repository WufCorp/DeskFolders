using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskFolders
{
    /// <summary>Извлекает иконки файлов/ярлыков как WPF ImageSource (с кэшем).</summary>
    internal static class IconHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_SYSICONINDEX = 0x000004000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid,
            out IImageList ppv);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const int SHIL_JUMBO = 0x4;      // 256x256
        private const int SHIL_EXTRALARGE = 0x2; // 48x48
        private const int ILD_TRANSPARENT = 0x1;

        private static readonly Guid IID_IImageList =
            new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");

        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
            [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
            [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
            [PreserveSig] int Draw(IntPtr pimldp);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
            // остальные методы не нужны
        }

        private static readonly Dictionary<string, ImageSource> _cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Иконка большого размера (jumbo 256, с откатом на 32). Кэшируется.</summary>
        public static ImageSource GetIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_cache.TryGetValue(path, out var cached)) return cached;

            ImageSource result = TryGetJumbo(path) ?? TryGetLarge(path);
            if (result != null) _cache[path] = result;
            return result;
        }

        private static ImageSource TryGetJumbo(string path)
        {
            try
            {
                var shfi = new SHFILEINFO();
                IntPtr r = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(shfi),
                    SHGFI_SYSICONINDEX);
                if (r == IntPtr.Zero) return null;
                int idx = shfi.iIcon;

                Guid guid = IID_IImageList;
                if (SHGetImageList(SHIL_JUMBO, ref guid, out IImageList list) != 0 || list == null)
                    return null;

                IntPtr hicon = IntPtr.Zero;
                if (list.GetIcon(idx, ILD_TRANSPARENT, ref hicon) != 0 || hicon == IntPtr.Zero)
                    return null;
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(hicon,
                        Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally { DestroyIcon(hicon); }
            }
            catch { return null; }
        }

        private static ImageSource TryGetLarge(string path)
        {
            var shfi = new SHFILEINFO();
            try
            {
                IntPtr r = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(shfi),
                    SHGFI_ICON | SHGFI_LARGEICON);
                if (shfi.hIcon == IntPtr.Zero) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon,
                    Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally
            {
                if (shfi.hIcon != IntPtr.Zero) DestroyIcon(shfi.hIcon);
            }
        }
    }
}
