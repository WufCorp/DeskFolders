using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DeskFolders
{
    /// <summary>Раскрытая папка: полупрозрачная панель с сеткой ярлыков.</summary>
    public class FolderPopup : Window
    {
        private readonly DockConfig _dock;
        private readonly App _app;
        private readonly bool _hover;
        private DockWindow _owner;
        private bool _contextOpen;
        private bool _reordering;
        private bool _closing;
        private Point _dragStart;
        private int _dragIndex;
        private bool _didDrag;
        private const string ReorderFormat = "DeskFolders.ItemIndex";
        private const int Columns = 4;

        /// <summary>Сообщает окну-папке, находится ли курсор над раскрытой панелью.</summary>
        public Action<bool> HoverChanged;

        public FolderPopup(DockConfig dock, App app)
        {
            _dock = dock;
            _app = app;
            _hover = app.Config != null && app.Config.OpenOnHover;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;

            Content = BuildPanel();
            Opacity = 0;
            Loaded += (s, e) => PlayIntro();

            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            if (_hover)
            {
                // в режиме наведения закрытием управляет отслеживание курсора
                MouseEnter += (s, e) => HoverChanged?.Invoke(true);
                MouseLeave += (s, e) => { if (!_reordering) HoverChanged?.Invoke(false); };
            }
            else
            {
                // в режиме клика закрываемся при потере фокуса,
                // но не тогда, когда открыто контекстное меню ярлыка
                Deactivated += (s, e) => { if (!_contextOpen && !_closing) Close(); };
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _closing = true;   // не даём обработчику Deactivated повторно вызвать Close()
            base.OnClosing(e);
        }

        private UIElement BuildPanel()
        {
            var panel = new Border
            {
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Color.FromArgb(235, 38, 38, 52)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 16, 18, 18),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 28, ShadowDepth = 6, Opacity = 0.55, Color = Colors.Black
                }
            };

            var outer = new StackPanel();

            outer.Children.Add(new TextBlock
            {
                Text = _dock.Name,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 15,
                Margin = new Thickness(4, 0, 0, 12)
            });

            if (_dock.Items.Count == 0)
            {
                outer.Children.Add(new TextBlock
                {
                    Text = "Папка пуста.\nПравый клик по папке → «Настроить папку…»",
                    Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 190)),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(20, 10, 20, 10)
                });
            }
            else
            {
                var grid = new UniformGrid { Columns = Columns };
                grid.Rows = (int)Math.Ceiling(_dock.Items.Count / (double)Columns);
                for (int i = 0; i < _dock.Items.Count; i++)
                    grid.Children.Add(BuildAppIcon(_dock.Items[i], i));
                outer.Children.Add(grid);
            }

            panel.Child = outer;
            return panel;
        }

        /// <summary>Анимация раскрытия (масштаб + прозрачность). Только при первом показе.</summary>
        private void PlayIntro()
        {
            if (!(Content is Border panel)) return;
            var scale = new ScaleTransform(0.82, 0.82);
            panel.RenderTransform = scale;
            panel.RenderTransformOrigin = new Point(0.5, 0.1);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var sa = new DoubleAnimation(0.82, 1.0, TimeSpan.FromMilliseconds(150))
                { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
            var oa = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(140));
            BeginAnimation(OpacityProperty, oa);
        }

        private UIElement BuildAppIcon(ShortcutItem it, int index)
        {
            string title = string.IsNullOrWhiteSpace(it.Name)
                ? System.IO.Path.GetFileNameWithoutExtension(it.Path)
                : it.Name;

            var cell = new Border
            {
                Width = 86, Height = 92,
                CornerRadius = new CornerRadius(14),
                Background = Brushes.Transparent,
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                AllowDrop = true
            };

            var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var appImg = new Image
            {
                Source = IconHelper.GetIcon(it.Path),
                Width = 44, Height = 44,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(appImg, BitmapScalingMode.HighQuality);
            sp.Children.Add(appImg);
            sp.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 78,
                Margin = new Thickness(0, 6, 0, 0)
            });
            cell.Child = sp;

            var hover = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            cell.MouseEnter += (s, e) => cell.Background = hover;
            cell.MouseLeave += (s, e) => cell.Background = Brushes.Transparent;

            // --- перетаскивание для сортировки ---
            cell.PreviewMouseLeftButtonDown += (s, e) =>
            {
                _dragStart = e.GetPosition(this);
                _dragIndex = index;
                _didDrag = false;
            };
            cell.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _didDrag) return;
                var p = e.GetPosition(this);
                if (Math.Abs(p.X - _dragStart.X) > 6 || Math.Abs(p.Y - _dragStart.Y) > 6)
                    StartReorder(cell);
            };
            cell.MouseLeftButtonUp += (s, e) =>
            {
                if (_didDrag) { _didDrag = false; return; }  // это было перетаскивание
                Native.Launch(it.Path);
                _app.OnLaunched();
                Close();
            };
            cell.DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(ReorderFormat)
                    ? DragDropEffects.Move : DragDropEffects.None;
                cell.Background = e.Data.GetDataPresent(ReorderFormat) ? hover : cell.Background;
                e.Handled = true;
            };
            cell.DragLeave += (s, e) => cell.Background = Brushes.Transparent;
            cell.Drop += (s, e) =>
            {
                cell.Background = Brushes.Transparent;
                if (!e.Data.GetDataPresent(ReorderFormat)) return;
                int from = (int)e.Data.GetData(ReorderFormat);
                ReorderItems(from, index);
                e.Handled = true;
            };

            // правый клик по ярлыку — контекстное меню
            var menu = new ContextMenu();
            var run = new MenuItem { Header = "Запустить" };
            run.Click += (s, e) => { Native.Launch(it.Path); _app.OnLaunched(); Close(); };
            var loc = new MenuItem { Header = "Открыть расположение файла" };
            loc.Click += (s, e) => { OpenLocation(it.Path); Close(); };
            var del = new MenuItem { Header = "Удалить из папки" };
            del.Click += (s, e) => RemoveFromFolder(it);
            menu.Items.Add(run);
            menu.Items.Add(loc);
            menu.Items.Add(new Separator());
            menu.Items.Add(del);
            menu.Opened += (s, e) => { _contextOpen = true; HoverChanged?.Invoke(true); };
            menu.Closed += (s, e) =>
            {
                _contextOpen = false;
                if (!IsMouseOver) HoverChanged?.Invoke(false);
            };
            cell.ContextMenu = menu;

            return cell;
        }

        private void OpenLocation(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void StartReorder(UIElement cell)
        {
            _didDrag = true;
            _reordering = true;
            _contextOpen = true;               // не закрываться в режиме клика
            HoverChanged?.Invoke(true);         // не закрываться в режиме наведения
            try
            {
                DragDrop.DoDragDrop(cell, new DataObject(ReorderFormat, _dragIndex),
                    DragDropEffects.Move);
            }
            catch { }
            finally
            {
                _reordering = false;
                _contextOpen = false;
                if (!IsMouseOver) HoverChanged?.Invoke(false);
            }
        }

        private void ReorderItems(int from, int to)
        {
            if (from < 0 || to < 0 || from >= _dock.Items.Count || to >= _dock.Items.Count
                || from == to) return;
            var item = _dock.Items[from];
            _dock.Items.RemoveAt(from);
            _dock.Items.Insert(to, item);
            _app.SaveConfig();
            _owner?.Rebuild();
            _app.NotifyManagerChanged();
            // перерисовку откладываем — нельзя менять дерево во время drag-drop
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Content = BuildPanel();
                Opacity = 1;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RemoveFromFolder(ShortcutItem it)
        {
            ShortcutStore.Cleanup(it.Path);
            _dock.Items.Remove(it);
            _app.SaveConfig();
            _owner?.Rebuild();            // обновить мини-иконки на плитке
            _app.NotifyManagerChanged();  // обновить менеджер, если открыт
            Content = BuildPanel();       // перерисовать раскрытую папку
            Opacity = 1;
        }

        public void LaunchItemForTest(ShortcutItem it)
        {
            Native.Launch(it.Path);
            Close();
        }

        /// <summary>Показывает панель рядом с иконкой-папкой, в пределах экрана.</summary>
        public void ShowNear(DockWindow dock, bool activate = true)
        {
            _owner = dock;
            // в режиме наведения окно не должно перехватывать фокус
            if (!activate) ShowActivated = false;
            Show();
            UpdateLayout();

            double screenW = SystemParameters.WorkArea.Width + SystemParameters.WorkArea.Left;
            double screenH = SystemParameters.WorkArea.Height + SystemParameters.WorkArea.Top;

            double x = dock.Left + dock.ActualWidth / 2 - ActualWidth / 2;
            double y = dock.Top + dock.ActualHeight + 8;

            if (x + ActualWidth > screenW) x = screenW - ActualWidth - 8;
            if (x < SystemParameters.WorkArea.Left) x = SystemParameters.WorkArea.Left + 8;
            if (y + ActualHeight > screenH) y = dock.Top - ActualHeight - 8; // над папкой
            if (y < 0) y = 8;

            Left = x;
            Top = y;
            if (activate)
            {
                Activate();
                Focus();
            }
        }
    }
}
