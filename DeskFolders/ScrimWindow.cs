using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeskFolders
{
    /// <summary>
    /// Полноэкранный полупрозрачный слой на время «показа папок поверх окон».
    /// Клик по нему (мимо папок) закрывает режим. Папки поднимаются над ним.
    /// </summary>
    public class ScrimWindow : Window
    {
        public Action Clicked;

        public ScrimWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            // ВАЖНО: scrim НЕ topmost. Папки делаем topmost, и ОС всегда держит
            // topmost-окна выше не-topmost — поэтому папки гарантированно над scrim,
            // без гонки, из-за которой раньше подсвечивались не все папки.
            Topmost = false;
            ShowActivated = false;
            // лёгкое затемнение, чтобы папки лучше читались поверх окон
            Background = new SolidColorBrush(Color.FromArgb(90, 10, 10, 16));
            // непустое содержимое нужно, чтобы гарантированно сработало ContentRendered
            Content = new Grid();

            // покрываем весь виртуальный экран (все мониторы)
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            MouseDown += (s, e) => Clicked?.Invoke();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindowNoActivate(hwnd); // без кнопки на панели задач/Alt-Tab
        }
    }
}
