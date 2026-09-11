using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace DeskFolders
{
    /// <summary>Выбор установленных программ (из меню «Пуск») для добавления в папку.</summary>
    public class AppPickerWindow : Window
    {
        private readonly App _app;
        private readonly DockConfig _target;
        private List<AppEntry> _all = new List<AppEntry>();
        private ListBox _list;
        private TextBox _search;
        private TextBlock _status;

        public AppPickerWindow(App app, DockConfig target)
        {
            _app = app;
            _target = target;

            Title = "Добавить установленные приложения";
            Width = 460;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x30));
            Content = BuildUi();

            Loaded += (s, e) => LoadApps();
        }

        private UIElement BuildUi()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _search = new TextBox
            {
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };
            _search.TextChanged += (s, e) => ApplyFilter();
            Grid.SetRow(_search, 0);
            grid.Children.Add(_search);

            _list = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2c, 0x2c, 0x3a)),
                BorderThickness = new Thickness(0),
                ItemTemplate = BuildItemTemplate()
            };
            ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
            Grid.SetRow(_list, 1);
            grid.Children.Add(_list);

            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(bottom, 2);

            var add = new Button
            {
                Content = "Добавить выбранные",
                Padding = new Thickness(14, 6, 14, 6),
                IsDefault = true
            };
            add.Click += (s, e) => AddSelected();
            DockPanel.SetDock(add, Dock.Right);
            bottom.Children.Add(add);

            var cancel = new Button
            {
                Content = "Отмена",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancel.Click += (s, e) => Close();
            DockPanel.SetDock(cancel, Dock.Right);
            bottom.Children.Add(cancel);

            _status = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 170)),
                VerticalAlignment = VerticalAlignment.Center
            };
            bottom.Children.Add(_status);

            grid.Children.Add(bottom);
            return grid;
        }

        private DataTemplate BuildItemTemplate()
        {
            const string xaml =
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<StackPanel Orientation=\"Horizontal\" Margin=\"2,3,2,3\">" +
                "<CheckBox IsChecked=\"{Binding IsChecked, Mode=TwoWay}\" VerticalAlignment=\"Center\" Margin=\"2,0,10,0\"/>" +
                "<Image Source=\"{Binding Icon}\" Width=\"26\" Height=\"26\" Margin=\"0,0,10,0\"/>" +
                "<TextBlock Text=\"{Binding Name}\" VerticalAlignment=\"Center\" Foreground=\"White\" FontFamily=\"Segoe UI\" FontSize=\"13\"/>" +
                "</StackPanel></DataTemplate>";
            return (DataTemplate)XamlReader.Parse(xaml);
        }

        // ---- сканирование меню «Пуск» ------------------------------------
        private void LoadApps()
        {
            _status.Text = "Поиск приложений…";
            var dirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<AppEntry>();

            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };

            foreach (var dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                foreach (var pattern in new[] { "*.lnk", "*.url" })
                {
                    IEnumerable<string> files;
                    try { files = Directory.EnumerateFiles(dir, pattern, opts); }
                    catch { continue; }

                    foreach (var f in files)
                    {
                        string name;
                        try { name = Path.GetFileNameWithoutExtension(f); }
                        catch { continue; }
                        if (IsNoise(name)) continue;
                        if (!seen.Add(name)) continue;
                        result.Add(new AppEntry { Name = name, Path = f });
                    }
                }
            }

            _all = result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            ApplyFilter();
        }

        private static bool IsNoise(string name)
        {
            string n = name.ToLowerInvariant().TrimStart();
            return n.Contains("uninstall")
                || n.Contains("деинсталл")
                || n.StartsWith("удалить ")
                || n.StartsWith("remove ");
        }

        private void ApplyFilter()
        {
            string q = _search.Text?.Trim() ?? "";
            IEnumerable<AppEntry> view = _all;
            if (q.Length > 0)
                view = _all.Where(a => a.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
            var shown = view.ToList();
            _list.ItemsSource = shown;
            _status.Text = $"Найдено: {_all.Count}" + (q.Length > 0 ? $" (показано {shown.Count})" : "");
        }

        // ---- добавление --------------------------------------------------
        private void AddSelected()
        {
            var chosen = _all.Where(a => a.IsChecked).ToList();
            if (chosen.Count == 0) { Close(); return; }

            foreach (var a in chosen)
            {
                _target.Items.Add(new ShortcutItem
                {
                    Path = ShortcutStore.Import(a.Path),
                    Name = a.Name
                });
            }
            _app.SaveConfig();
            _app.RefreshDocks();
            _app.NotifyManagerChanged();
            Close();
        }

        private class AppEntry
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public bool IsChecked { get; set; }

            private ImageSource _icon;
            private bool _loaded;
            public ImageSource Icon
            {
                get
                {
                    if (!_loaded) { _loaded = true; _icon = IconHelper.GetIcon(Path); }
                    return _icon;
                }
            }
        }
    }
}
