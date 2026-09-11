using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DeskFolders
{
    /// <summary>Иконка-папка, лежащая на рабочем столе. Клик раскрывает сетку ярлыков.</summary>
    public class DockWindow : Window
    {
        private readonly DockConfig _dock;
        private readonly App _app;
        private Point _downPos;
        private Point _dragGrab;    // точка захвата внутри окна (для ручного перетаскивания)
        private bool _dragging;
        private bool _mouseDown;
        private FolderPopup _openPopup;
        private Border _bgBorder;
        private DispatcherTimer _openTimer;
        private DispatcherTimer _closeTimer;
        private bool _overDock;
        private bool _overPopup;
        private bool _peek;

        public DockConfig Dock => _dock;

        /// <summary>HWND окна-папки (для управления z-порядком в режиме «поверх окон»).</summary>
        public IntPtr Handle => new WindowInteropHelper(this).Handle;

        /// <summary>Диагностика: включён ли режим подсветки у этой папки.</summary>
        public bool PeekActiveForTest => _peek;

        private bool HoverMode => _app.Config != null && _app.Config.OpenOnHover;

        public DockWindow(DockConfig dock, App app)
        {
            _dock = dock;
            _app = app;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = false;
            ShowActivated = false;   // показ окна-папки не должен забирать фокус
            SizeToContent = SizeToContent.WidthAndHeight;
            Left = dock.X;
            Top = dock.Y;
            AllowDrop = true;

            Content = BuildTile();
            BuildContextMenu();

            SourceInitialized += OnSourceInitialized;
            PreviewMouseLeftButtonDown += OnDown;
            PreviewMouseMove += OnMove;
            PreviewMouseLeftButtonUp += OnUp;
            DragEnter += OnDragEnterOver;
            DragOver += OnDragEnterOver;
            DragLeave += (s, e) => Highlight(false);
            Drop += OnDrop;
            MouseEnter += OnDockMouseEnter;
            MouseLeave += OnDockMouseLeave;
        }

        // --- открытие по наведению ----------------------------------------
        private void OnDockMouseEnter(object sender, MouseEventArgs e)
        {
            _overDock = true;
            if (!HoverMode) return;
            CancelClose();
            if (_openPopup == null && _openTimer == null)
            {
                _openTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                _openTimer.Tick += (s, a) =>
                {
                    CancelOpen();
                    if (_overDock) OpenPopup(false);
                };
                _openTimer.Start();
            }
        }

        private void OnDockMouseLeave(object sender, MouseEventArgs e)
        {
            _overDock = false;
            if (!HoverMode) return;
            CancelOpen();
            ScheduleClose();
        }

        private void CancelOpen()
        {
            _openTimer?.Stop();
            _openTimer = null;
        }

        private void CancelClose()
        {
            _closeTimer?.Stop();
            _closeTimer = null;
        }

        private void ScheduleClose()
        {
            CancelClose();
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _closeTimer.Tick += (s, a) =>
            {
                CancelClose();
                if (!_overDock && !_overPopup)
                    ClosePopup();
            };
            _closeTimer.Start();
        }

        // --- перетаскивание ярлыков в папку -------------------------------
        private void OnDragEnterOver(object sender, DragEventArgs e)
        {
            bool ok = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            if (ok) Highlight(true);
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            Highlight(false);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null) return;
            foreach (var f in files)
            {
                _dock.Items.Add(new ShortcutItem
                {
                    // копируем ярлык в хранилище, чтобы он работал после удаления оригинала
                    Path = ShortcutStore.Import(f),
                    Name = System.IO.Path.GetFileNameWithoutExtension(f.TrimEnd('\\'))
                });
            }
            _app.SaveConfig();
            Rebuild();
            _app.NotifyManagerChanged();
        }

        private void Highlight(bool on)
        {
            if (_bgBorder == null) return;
            _bgBorder.Background = new SolidColorBrush(on
                ? Color.FromArgb(110, 120, 170, 255)
                : Color.FromArgb(48, 255, 255, 255));
            _bgBorder.BorderBrush = new SolidColorBrush(on
                ? Color.FromArgb(200, 150, 190, 255)
                : Color.FromArgb(60, 255, 255, 255));
        }

        // ------------------------------------------------------------------
        public void Rebuild()
        {
            Content = BuildTile();
        }

        /// <summary>Режим «показать поверх окон»: непрозрачная плитка, окно наверх/вниз.</summary>
        public void SetPeek(bool on)
        {
            _peek = on;
            Rebuild();
            UpdateLayout();      // принудительно перестраиваем плитку, чтобы карточка отрисовалась сразу
            var hwnd = Handle;
            if (on)
            {
                // Само окно делаем topmost; точный порядок (над затемняющим слоем)
                // задаёт App — так каждая папка гарантированно оказывается сверху.
                Topmost = true;
            }
            else
            {
                Topmost = false;
                if (hwnd != IntPtr.Zero) Native.SendToBottom(hwnd);
            }
        }

        public void CloseOpenPopup() => ClosePopup();

        private UIElement BuildTile()
        {
            var root = new StackPanel { Width = 96, Margin = new Thickness(0) };

            // фон папки со скруглением + 2x2 мини-иконки
            var bg = new Border
            {
                Width = 76,
                Height = 76,
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5,
                    Color = Colors.Black
                }
            };

            var grid = new UniformGrid { Rows = 2, Columns = 2, Margin = new Thickness(8) };
            int shown = 0;
            foreach (var it in _dock.Items)
            {
                if (shown >= 4) break;
                var img = new Image
                {
                    Source = IconHelper.GetIcon(it.Path),
                    Width = 24, Height = 24,
                    Margin = new Thickness(2)
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                grid.Children.Add(img);
                shown++;
            }
            bg.Child = grid;
            _bgBorder = bg;
            root.Children.Add(bg);

            // подпись
            var label = new TextBlock
            {
                Text = _dock.Name,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11.5,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 92,
                Margin = new Thickness(0, 4, 0, 0),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9, Color = Colors.Black
                }
            };
            root.Children.Add(label);

            // в режиме «поверх окон» — непрозрачная плитка-карточка
            if (_peek)
            {
                var card = new Border
                {
                    CornerRadius = new CornerRadius(16),
                    Background = new SolidColorBrush(Color.FromRgb(0x2b, 0x2b, 0x3a)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 8, 8, 6),
                    Child = root,
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 22, ShadowDepth = 4, Opacity = 0.7, Color = Colors.Black
                    }
                };
                return card;
            }
            return root;
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Открыть" };
            open.Click += (s, e) => TogglePopup();
            var edit = new MenuItem { Header = "Настроить папку…" };
            edit.Click += (s, e) => _app.OpenManager(_dock);
            var remove = new MenuItem { Header = "Убрать с рабочего стола" };
            remove.Click += (s, e) => _app.RemoveDock(_dock);
            menu.Items.Add(open);
            menu.Items.Add(edit);
            menu.Items.Add(new Separator());
            menu.Items.Add(remove);
            ContextMenu = menu;
        }

        // --- z-порядок: держим окно внизу, не активируем ------------------
        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindowNoActivate(hwnd);
            Native.SendToBottom(hwnd);
        }

        // --- перетаскивание vs клик ---------------------------------------
        // Ручное перетаскивание (без WinAPI-цикла SC_MOVE, который мог зависать
        // намертво): захватываем мышь и двигаем окно сами в MouseMove.
        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _downPos = e.GetPosition(this);
            _dragGrab = _downPos;
            _dragging = false;
            _mouseDown = true;
            try { CaptureMouse(); } catch { }
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_mouseDown || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (!_dragging)
            {
                if (Math.Abs(p.X - _downPos.X) <= 5 && Math.Abs(p.Y - _downPos.Y) <= 5) return;
                _dragging = true;   // порог пройден — начинаем тащить
            }
            // p измеряется относительно окна ДО его сдвига в этом обработчике,
            // поэтому (p - точка захвата) — это на сколько подвинуть окно.
            Left += p.X - _dragGrab.X;
            Top += p.Y - _dragGrab.Y;
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (_mouseDown)
            {
                _mouseDown = false;
                try { ReleaseMouseCapture(); } catch { }
            }

            if (_dragging)
            {
                if (_app.Config != null && _app.Config.SnapToGrid)
                    SnapPositionToGrid();          // «прилипаем» к сетке в конце перетаскивания
                _dock.X = Left;
                _dock.Y = Top;
                _app.SaveConfig();
            }
            else if (!HoverMode)                    // клик без перетаскивания
            {
                TogglePopup();
            }
            _dragging = false;
        }

        // --- размещение по сетке ------------------------------------------
        /// <summary>Прижимает окно к ближайшей ячейке сетки и сохраняет позицию в конфиг.</summary>
        public void ApplyGridSnap()
        {
            SnapPositionToGrid();
            _dock.X = Left;
            _dock.Y = Top;
        }

        private void SnapPositionToGrid()
        {
            var c = _app.Config;
            if (c == null) return;
            int cw = Math.Max(20, c.GridCellWidth);
            int ch = Math.Max(20, c.GridCellHeight);
            Left = SnapAxis(Left, cw, c.GridOriginX);
            Top = SnapAxis(Top, ch, c.GridOriginY);
        }

        private static double SnapAxis(double value, int cell, int origin)
        {
            double n = Math.Round((value - origin) / cell);
            double snapped = origin + n * cell;
            return snapped < 0 ? 0 : snapped;
        }

        // --- хуки для само-теста ------------------------------------------
        public void OpenPopupForTest() => OpenPopup(true);

        public void LaunchFirstForTest()
        {
            if (_dock.Items.Count == 0) return;
            _openPopup?.LaunchItemForTest(_dock.Items[0]);
        }

        // --- всплывающая сетка --------------------------------------------
        private void TogglePopup()
        {
            if (_openPopup != null) ClosePopup();
            else OpenPopup(true);
        }

        private void OpenPopup(bool activate)
        {
            if (_openPopup != null) return;
            var popup = new FolderPopup(_dock, _app);
            popup.HoverChanged = over =>
            {
                _overPopup = over;
                if (!HoverMode) return;
                if (over) CancelClose();
                else ScheduleClose();
            };
            popup.Closed += (s, e) =>
            {
                if (_openPopup == s) _openPopup = null;
                _overPopup = false;
            };
            _openPopup = popup;
            popup.ShowNear(this, activate);
        }

        private void ClosePopup()
        {
            if (_openPopup != null)
            {
                _openPopup.Close();
                _openPopup = null;
            }
        }
    }
}
