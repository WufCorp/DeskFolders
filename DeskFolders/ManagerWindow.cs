using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace DeskFolders
{
    /// <summary>Окно управления: папки, их ярлыки и настройки приложения.</summary>
    public class ManagerWindow : Window
    {
        private readonly App _app;

        private ListBox _dockList;
        private TextBox _nameBox;
        private ListBox _itemList;
        private DockConfig _current;
        private bool _loading;

        // настройки
        private CheckBox _autoStart;
        private bool _syncingAuto;
        private Action<bool> _autoStartHandler;
        private Button _hotkeyBtn;
        private TextBlock _hotkeyHint;
        private bool _capturingHotkey;

        // навигация
        private ContentControl _host;
        private UIElement _foldersView;
        private UIElement _settingsView;
        private Button _navFolders;
        private Button _navSettings;

        // ---- палитра ------------------------------------------------------
        private static readonly FontFamily UiFont = new FontFamily("Segoe UI");
        private static SolidColorBrush B(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
        private readonly Brush _bg = B(0x1B, 0x1B, 0x24);
        private readonly Brush _panel = B(0x26, 0x26, 0x33);
        private readonly Brush _panelBorder = B(0x39, 0x39, 0x49);
        private readonly Brush _accent = B(0x6C, 0x8C, 0xFF);
        private readonly Brush _text = Brushes.White;
        private readonly Brush _sub = B(0xA6, 0xA6, 0xB8);
        private readonly Brush _danger = B(0xE0, 0x6C, 0x75);
        private readonly Brush _heart = B(0xFF, 0x6B, 0x8A);

        // общий шаблон кнопки: скругление + мягкое подсвечивание при наведении
        private static readonly ControlTemplate BtnTemplate = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate TargetType='Button'
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Border x:Name='bd' CornerRadius='8' Background='{TemplateBinding Background}'
          BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'
          Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.87'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.7'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='bd' Property='Opacity' Value='0.4'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>");

        public ManagerWindow(App app)
        {
            _app = app;
            Title = "DeskFolders";
            Width = 780;
            Height = 580;
            MinWidth = 660;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = _bg;
            FontFamily = UiFont;
            Content = BuildUi();
            PreviewKeyDown += OnKeyDownCapture;
        }

        // ================= общий каркас ===================================
        private UIElement BuildUi()
        {
            var root = new DockPanel();

            // ---- шапка с навигацией ----
            var header = new Grid { Margin = new Thickness(16, 14, 16, 6) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            DockPanel.SetDock(header, Dock.Top);

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(new TextBlock
            {
                Text = "DeskFolders",
                Foreground = _text,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            titleRow.Children.Add(new TextBlock
            {
                Text = "v" + (ver != null ? ver.ToString(3) : "?"),
                Foreground = _sub,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 0, 4)
            });
            Grid.SetColumn(titleRow, 0);
            header.Children.Add(titleRow);

            var nav = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _navFolders = NavButton("Папки", () => ShowView(true));
            _navSettings = NavButton("Настройки", () => ShowView(false));
            nav.Children.Add(_navFolders);
            nav.Children.Add(_navSettings);
            Grid.SetColumn(nav, 1);
            header.Children.Add(nav);

            var donate = Btn("❤  Поддержать", _panel, _heart, (s, e) => _app.OpenDonate());
            donate.Margin = new Thickness(0);
            donate.BorderThickness = new Thickness(1);
            donate.BorderBrush = _panelBorder;
            Grid.SetColumn(donate, 2);
            header.Children.Add(donate);

            root.Children.Add(header);

            // ---- хост содержимого ----
            _host = new ContentControl { Margin = new Thickness(16, 6, 16, 16) };
            _foldersView = BuildFoldersView();
            _settingsView = BuildSettingsView();
            root.Children.Add(_host);

            ShowView(true);
            return root;
        }

        private Button NavButton(string text, Action onClick)
        {
            var b = new Button
            {
                Content = text,
                Template = BtnTemplate,
                Background = Brushes.Transparent,
                Foreground = _sub,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(3, 0, 3, 0),
                FontSize = 14,
                Cursor = Cursors.Hand
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private void ShowView(bool folders)
        {
            _host.Content = folders ? _foldersView : _settingsView;
            _navFolders.Background = folders ? _accent : Brushes.Transparent;
            _navFolders.Foreground = folders ? _text : _sub;
            _navSettings.Background = folders ? Brushes.Transparent : _accent;
            _navSettings.Foreground = folders ? _sub : _text;
        }

        // ================= вкладка «Папки» ================================
        private UIElement BuildFoldersView()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // --- левая карточка: список папок ---
            var leftDock = new DockPanel();
            var leftBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(leftBtns, Dock.Bottom);
            leftBtns.Children.Add(Btn("+ Папка", _accent, _text, (s, e) => _app.AddDock()));
            leftBtns.Children.Add(Btn("Удалить", _panel, _danger, (s, e) => { if (_current != null) _app.RemoveDock(_current); }));
            leftDock.Children.Add(leftBtns);

            leftDock.Children.Add(SectionTitle("Папки на столе"));
            _dockList = StyledList();
            _dockList.SelectionChanged += DockSelected;
            var leftScrollHost = new DockPanel();
            leftScrollHost.Children.Add(_dockList);
            leftDock.Children.Add(leftScrollHost);

            var leftCard = Card(leftDock);
            Grid.SetColumn(leftCard, 0);
            grid.Children.Add(leftCard);

            // --- правая карточка: редактор папки ---
            var rightDock = new DockPanel();

            rightDock.Children.Add(SectionTitle("Ярлыки в папке"));

            var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(nameRow, Dock.Top);
            var nameLbl = new TextBlock
            {
                Text = "Название", Foreground = _sub, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            DockPanel.SetDock(nameLbl, Dock.Left);
            nameRow.Children.Add(nameLbl);
            _nameBox = new TextBox
            {
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = _bg, Foreground = _text, CaretBrush = _text,
                BorderBrush = _panelBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6), FontSize = 13
            };
            _nameBox.TextChanged += (s, e) =>
            {
                if (_loading || _current == null) return;
                _current.Name = _nameBox.Text;
                _app.SaveConfig();
                _app.UpdateDockVisual(_current);
            };
            nameRow.Children.Add(_nameBox);
            // порядок: сначала title (Top уже добавлен), затем nameRow сверху
            DockPanel.SetDock(nameRow, Dock.Top);
            rightDock.Children.Add(nameRow);

            var itemBtns = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(itemBtns, Dock.Bottom);
            itemBtns.Children.Add(Btn("+ Установленное", _accent, _text, (s, e) => AddInstalled()));
            itemBtns.Children.Add(Btn("+ Файл", _panel, _text, (s, e) => AddFile()));
            itemBtns.Children.Add(Btn("+ Папку", _panel, _text, (s, e) => AddFolder()));
            itemBtns.Children.Add(Btn("↑", _panel, _text, (s, e) => Move(-1)));
            itemBtns.Children.Add(Btn("↓", _panel, _text, (s, e) => Move(1)));
            itemBtns.Children.Add(Btn("Убрать", _panel, _danger, (s, e) => RemoveItem()));
            rightDock.Children.Add(itemBtns);

            _itemList = StyledList();
            rightDock.Children.Add(_itemList);

            var rightCard = Card(rightDock);
            Grid.SetColumn(rightCard, 2);
            grid.Children.Add(rightCard);

            return grid;
        }

        private Border Card(UIElement child) => new Border
        {
            Background = _panel,
            BorderBrush = _panelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = child
        };

        private TextBlock SectionTitle(string t)
        {
            var tb = new TextBlock
            {
                Text = t, Foreground = _text, FontSize = 15, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(tb, Dock.Top);
            return tb;
        }

        private ListBox StyledList()
        {
            var lb = new ListBox
            {
                Background = _bg,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Foreground = _text,
                Padding = new Thickness(2)
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(lb, ScrollBarVisibility.Disabled);
            return lb;
        }

        private Button Btn(string text, Brush bg, Brush fg, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = text,
                Background = bg,
                Foreground = fg,
                Template = BtnTemplate,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 13,
                Cursor = Cursors.Hand
            };
            b.Click += onClick;
            return b;
        }

        // ================= вкладка «Настройки» ============================
        private UIElement BuildSettingsView()
        {
            var stack = new StackPanel();

            // автозапуск
            _autoStart = MakeCheck("Запускать при старте Windows", _app.IsAutoStartOn());
            _autoStart.Checked += (s, e) => { if (!_syncingAuto) _app.SetAutoStart(true); };
            _autoStart.Unchecked += (s, e) => { if (!_syncingAuto) _app.SetAutoStart(false); };
            _autoStartHandler = on => { _syncingAuto = true; _autoStart.IsChecked = on; _syncingAuto = false; };
            _app.AutoStartChanged += _autoStartHandler;
            Closed += (s, e) => { if (_autoStartHandler != null) _app.AutoStartChanged -= _autoStartHandler; };
            stack.Children.Add(SettingCard("Автозапуск",
                "Приложение будет запускаться автоматически при входе в Windows.", _autoStart));

            // открытие по наведению
            var hover = MakeCheck("Открывать папки по наведению мыши", _app.Config.OpenOnHover);
            hover.Checked += (s, e) => { _app.Config.OpenOnHover = true; _app.SaveConfig(); };
            hover.Unchecked += (s, e) => { _app.Config.OpenOnHover = false; _app.SaveConfig(); };
            stack.Children.Add(SettingCard("Открытие папок",
                "Папка раскрывается сама, когда наводишь на неё курсор (без клика).", hover));

            // размещение
            var place = new StackPanel { Orientation = Orientation.Horizontal };
            var free = MakeRadio("Свободное", "placement", !_app.Config.SnapToGrid);
            var snap = MakeRadio("По сетке", "placement", _app.Config.SnapToGrid);
            free.Margin = new Thickness(0, 0, 18, 0);
            free.Checked += (s, e) => _app.SetSnapToGrid(false);
            snap.Checked += (s, e) => _app.SetSnapToGrid(true);
            place.Children.Add(free);
            place.Children.Add(snap);
            stack.Children.Add(SettingCard("Размещение на рабочем столе",
                "Свободное — тащите папки куда угодно. По сетке — папки выравниваются как обычные ярлыки.", place));

            // горячая клавиша
            var hk = new StackPanel();
            _hotkeyBtn = Btn(FormatHotkey(_app.Config.HotkeyModifiers, _app.Config.HotkeyVk), _bg, _text,
                (s, e) => BeginCaptureHotkey());
            _hotkeyBtn.BorderThickness = new Thickness(1);
            _hotkeyBtn.BorderBrush = _panelBorder;
            _hotkeyBtn.HorizontalAlignment = HorizontalAlignment.Left;
            _hotkeyBtn.MinWidth = 180;
            _hotkeyBtn.Margin = new Thickness(0);
            _hotkeyHint = new TextBlock
            {
                Text = "Нажмите кнопку и задайте свою комбинацию (например, Ctrl + Shift + D).",
                Foreground = _sub, FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap
            };
            hk.Children.Add(_hotkeyBtn);
            hk.Children.Add(_hotkeyHint);
            stack.Children.Add(SettingCard("Горячая клавиша «показать папки поверх окон»",
                "Комбинация, которая мгновенно показывает все папки поверх открытых окон.", hk));

            // поддержка автора
            var donateStack = new StackPanel();
            var donateBtn = Btn("❤  Поддержать автора", _heart, Brushes.White, (s, e) => _app.OpenDonate());
            donateBtn.HorizontalAlignment = HorizontalAlignment.Left;
            donateBtn.Padding = new Thickness(16, 9, 16, 9);
            donateBtn.FontSize = 14;
            donateBtn.Margin = new Thickness(0);
            var donateLink = new TextBlock
            {
                Text = App.DonateUrl, Foreground = _sub, FontSize = 12, Margin = new Thickness(0, 8, 0, 0)
            };
            donateStack.Children.Add(donateBtn);
            donateStack.Children.Add(donateLink);
            stack.Children.Add(SettingCard("Поддержать автора",
                "DeskFolders бесплатен. Если он вам полезен — можно поблагодарить автора любой суммой (от 10 ₽). Спасибо!",
                donateStack));

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = stack
            };
        }

        private Border SettingCard(string title, string desc, UIElement control)
        {
            var s = new StackPanel();
            s.Children.Add(new TextBlock
            {
                Text = title, Foreground = _text, FontSize = 15, FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrEmpty(desc))
                s.Children.Add(new TextBlock
                {
                    Text = desc, Foreground = _sub, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 10)
                });
            else
                s.Children.Add(new Border { Height = 10 });
            s.Children.Add(control);

            return new Border
            {
                Background = _panel,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12),
                Child = s
            };
        }

        private CheckBox MakeCheck(string text, bool @checked) => new CheckBox
        {
            Content = text, Foreground = _text, FontSize = 13.5, IsChecked = @checked,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        private RadioButton MakeRadio(string text, string group, bool @checked) => new RadioButton
        {
            Content = text, Foreground = _text, FontSize = 13.5, GroupName = group, IsChecked = @checked,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        // ================= захват горячей клавиши =========================
        private void BeginCaptureHotkey()
        {
            _capturingHotkey = true;
            _hotkeyBtn.Content = "Нажмите клавиши…";
            _hotkeyHint.Text = "Ожидание комбинации. Esc — отмена.";
            _hotkeyHint.Foreground = _accent;
            Focus();
        }

        private void OnKeyDownCapture(object sender, KeyEventArgs e)
        {
            if (!_capturingHotkey) return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                EndCapture("Отменено. Комбинация не изменена.", false);
                e.Handled = true;
                return;
            }

            // одни модификаторы не считаем — ждём основную клавишу
            if (IsModifierKey(key)) { e.Handled = true; return; }

            uint mods = 0;
            var m = Keyboard.Modifiers;
            if ((m & ModifierKeys.Alt) != 0) mods |= 1;      // MOD_ALT
            if ((m & ModifierKeys.Control) != 0) mods |= 2;  // MOD_CONTROL
            if ((m & ModifierKeys.Shift) != 0) mods |= 4;    // MOD_SHIFT
            if ((m & ModifierKeys.Windows) != 0) mods |= 8;  // MOD_WIN

            if (mods == 0)
            {
                _hotkeyHint.Text = "Нужен хотя бы один модификатор (Ctrl, Shift или Alt). Попробуйте ещё раз.";
                _hotkeyHint.Foreground = _danger;
                e.Handled = true;
                return;
            }

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            bool ok = _app.UpdateHotkey(mods, vk);
            _hotkeyBtn.Content = FormatHotkey(mods, vk);
            if (ok) EndCapture("Готово. Новая комбинация сохранена.", true);
            else EndCapture("Не удалось назначить — возможно, комбинация уже занята системой. Выберите другую.", false);
            e.Handled = true;
        }

        private void EndCapture(string message, bool success)
        {
            _capturingHotkey = false;
            _hotkeyHint.Text = message;
            _hotkeyHint.Foreground = success ? _accent : _danger;
        }

        private static bool IsModifierKey(Key k) =>
            k == Key.LeftCtrl || k == Key.RightCtrl ||
            k == Key.LeftShift || k == Key.RightShift ||
            k == Key.LeftAlt || k == Key.RightAlt ||
            k == Key.LWin || k == Key.RWin || k == Key.System;

        private static string FormatHotkey(uint mods, uint vk)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((mods & 2) != 0) parts.Add("Ctrl");
            if ((mods & 4) != 0) parts.Add("Shift");
            if ((mods & 1) != 0) parts.Add("Alt");
            if ((mods & 8) != 0) parts.Add("Win");
            Key key = KeyInterop.KeyFromVirtualKey((int)vk);
            parts.Add(key == Key.None ? ("VK " + vk) : key.ToString());
            return string.Join(" + ", parts);
        }

        // ================= список папок ===================================
        public void ReloadList()
        {
            _loading = true;
            _dockList.Items.Clear();
            foreach (var d in _app.Config.Docks)
                _dockList.Items.Add(MakeDockRow(d));
            _loading = false;
            if (_dockList.Items.Count > 0 && _current == null)
                _dockList.SelectedIndex = 0;
            else
                SelectDock(_current);
        }

        public void ReloadCurrent() => ReloadItems();

        public void SelectDock(DockConfig dock)
        {
            foreach (var obj in _dockList.Items)
                if (obj is ListBoxItem li && li.Tag == dock)
                {
                    _dockList.SelectedItem = obj;
                    return;
                }
        }

        private ListBoxItem MakeDockRow(DockConfig d)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = "📁", FontSize = 16, Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = d.Name, Foreground = _text, FontSize = 13.5 });
            texts.Children.Add(new TextBlock
            {
                Text = Plural(d.Items.Count), Foreground = _sub, FontSize = 11
            });
            row.Children.Add(texts);
            return new ListBoxItem { Content = row, Tag = d, Padding = new Thickness(6, 5, 6, 5) };
        }

        private static string Plural(int n)
        {
            int m10 = n % 10, m100 = n % 100;
            string word = (m10 == 1 && m100 != 11) ? "ярлык"
                        : (m10 >= 2 && m10 <= 4 && (m100 < 10 || m100 >= 20)) ? "ярлыка" : "ярлыков";
            return n + " " + word;
        }

        private void DockSelected(object sender, SelectionChangedEventArgs e)
        {
            _current = (_dockList.SelectedItem as ListBoxItem)?.Tag as DockConfig;
            _loading = true;
            _nameBox.Text = _current?.Name ?? "";
            _loading = false;
            ReloadItems();
        }

        // ================= ярлыки внутри папки ============================
        private void ReloadItems()
        {
            _itemList.Items.Clear();
            if (_current == null) return;
            foreach (var it in _current.Items)
                _itemList.Items.Add(MakeItemRow(it));
        }

        private ListBoxItem MakeItemRow(ShortcutItem it)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            try
            {
                row.Children.Add(new Image
                {
                    Source = IconHelper.GetIcon(it.Path),
                    Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            catch { }
            string name = string.IsNullOrWhiteSpace(it.Name)
                ? Path.GetFileNameWithoutExtension(it.Path) : it.Name;
            row.Children.Add(new TextBlock
            {
                Text = name, Foreground = _text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center
            });
            return new ListBoxItem { Content = row, Tag = it, Padding = new Thickness(6, 4, 6, 4) };
        }

        private void AddInstalled()
        {
            if (_current == null) return;
            var picker = new AppPickerWindow(_app, _current) { Owner = this };
            picker.ShowDialog();
        }

        private void AddFile()
        {
            if (_current == null) return;
            var dlg = new OpenFileDialog
            {
                Title = "Выберите программу или ярлык",
                Filter = "Программы и ярлыки|*.exe;*.lnk;*.url;*.bat;*.cmd|Все файлы|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames)
                    _current.Items.Add(new ShortcutItem
                    {
                        Path = ShortcutStore.Import(f),
                        Name = Path.GetFileNameWithoutExtension(f)
                    });
                Persist();
            }
        }

        private void AddFolder()
        {
            if (_current == null) return;
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Выберите папку, которую нужно открывать"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _current.Items.Add(new ShortcutItem
                {
                    Path = dlg.SelectedPath,
                    Name = new DirectoryInfo(dlg.SelectedPath).Name
                });
                Persist();
            }
        }

        private void RemoveItem()
        {
            if (_current == null) return;
            int i = _itemList.SelectedIndex;
            if (i < 0) return;
            ShortcutStore.Cleanup(_current.Items[i].Path);
            _current.Items.RemoveAt(i);
            Persist();
        }

        private void Move(int delta)
        {
            if (_current == null) return;
            int i = _itemList.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _current.Items.Count) return;
            var tmp = _current.Items[i];
            _current.Items[i] = _current.Items[j];
            _current.Items[j] = tmp;
            Persist();
            _itemList.SelectedIndex = j;
        }

        private void Persist()
        {
            _app.SaveConfig();
            _app.RefreshDocks();
            ReloadItems();
            // обновим счётчик ярлыков в левом списке
            int sel = _dockList.SelectedIndex;
            ReloadList();
            if (sel >= 0 && sel < _dockList.Items.Count) _dockList.SelectedIndex = sel;
        }
    }
}
