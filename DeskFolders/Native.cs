using System;
using System.Runtime.InteropServices;

namespace DeskFolders
{
    /// <summary>Тонкая обёртка над WinAPI: позиционирование окон, запуск файлов.</summary>
    internal static class Native
    {
        // --- позиционирование окон ---------------------------------------
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOP = new IntPtr(0);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        /// <summary>Делает окно неактивируемым и убирает его из Alt-Tab/панели задач.</summary>
        public static void MakeToolWindowNoActivate(IntPtr hwnd)
        {
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }

        /// <summary>Опускает окно в самый низ z-порядка (за остальные окна).</summary>
        public static void SendToBottom(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>Поднимает окно на самый верх (внутри своей topmost-группы).</summary>
        public static void RaiseToTop(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>Делает окно topmost и поднимает его на самый верх topmost-группы.</summary>
        public static void RaiseTopmost(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// Ставит окно <paramref name="hwnd"/> НЕПОСРЕДСТВЕННО ЗА (ниже) окном
        /// <paramref name="reference"/> в z-порядке (reference оказывается выше).
        /// </summary>
        public static void PlaceBehind(IntPtr hwnd, IntPtr reference)
        {
            SetWindowPos(hwnd, reference, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // --- запуск файлов/ярлыков ---------------------------------------
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ShellExecuteW(IntPtr hwnd, string lpOperation,
            string lpFile, string lpParameters, string lpDirectory, int nShowCmd);

        public static void Launch(string path)
        {
            try
            {
                ShellExecuteW(IntPtr.Zero, "open", path, null, null, 1 /*SW_SHOWNORMAL*/);
            }
            catch { }
        }
    }
}
