using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Drawing;
using Microsoft.Win32;

namespace DeskFolders
{
    public partial class App : System.Windows.Application
    {
        private const string RunKeyName = "DeskFolders";
        private const string MutexName = "DeskFolders_SingleInstance_v1";
        private const string ShowEventName = "DeskFolders_ShowManager_v1";
        private static Mutex _mutex;
        private static EventWaitHandle _showEvent;

        private AppConfig _config;
        private readonly List<DockWindow> _dockWindows = new List<DockWindow>();
        private NotifyIcon _tray;
        private ToolStripMenuItem _autoStartItem;
        private ManagerWindow _manager;
        private bool _syncingAutoStart;

        /// <summary>Срабатывает, когда автозапуск включили/выключили (для синхронизации галочек в разных окнах).</summary>
        public event Action<bool> AutoStartChanged;

        // горячая клавиша + режим «поверх окон»
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0xB001;
        private const uint MOD_NOREPEAT = 0x4000;
        private HwndSource _msgSource;
        private ScrimWindow _scrim;
        private bool _peekActive;
        private System.Windows.Threading.DispatcherTimer _peekRaiseTimer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // приложение живёт в трее — не закрываться при закрытии окон
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // ничего не должно ронять процесс молча
            DispatcherUnhandledException += (s, ex) =>
            {
                LogError("Dispatcher", ex.Exception);
                ex.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
                LogError("AppDomain", ex.ExceptionObject as Exception);

            if (e.Args != null && e.Args.Contains("--pickertest"))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _config = AppConfig.Load();
                var target = _config.Docks.Count > 0 ? _config.Docks[0] : new DockConfig();
                var picker = new AppPickerWindow(this, target);
                picker.Show();
                return;
            }

            if (e.Args != null && e.Args.Contains("--importtest"))
            {
                var p = ShortcutStore.Import(@"C:\Windows\System32\notepad.exe");
                LogError("importtest", new Exception(
                    $"result={p} exists={System.IO.File.Exists(p)} isLnk={p?.EndsWith(".lnk")}"));
                Shutdown();
                return;
            }

            // ---- защита от двойного запуска ----
            _mutex = new Mutex(true, MutexName, out bool isNew);
            if (!isNew)
            {
                // уже запущено: разбудить работающий экземпляр (откроет менеджер)
                // и молча закрыться, не создавая второй значок в трее.
                try
                {
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev))
                    {
                        ev.Set();
                        ev.Dispose();
                    }
                }
                catch { }
                Shutdown();
                return;
            }

            // слушаем сигнал от последующих попыток запуска
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var listener = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        _showEvent.WaitOne();
                        Dispatcher.BeginInvoke(new Action(() => OpenManager(null)));
                    }
                    catch { break; }
                }
            })
            { IsBackground = true, Name = "DeskFolders-InstanceListener" };
            listener.Start();

            _config = AppConfig.Load();
            SyncAutoStart();

            BuildTray();
            CreateDockWindows();
            RegisterHotkey();

            if (_config.Docks.Count == 0)
                OpenManager(null);

            if (e.Args != null && e.Args.Contains("--selftest"))
                RunSelfTest();

            if (e.Args != null && e.Args.Contains("--peektest"))
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(700);
                    EnterPeek();
                }));

            if (e.Args != null && e.Args.Contains("--peekdiag"))
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(800);
                    for (int cycle = 1; cycle <= 6; cycle++)
                    {
                        EnterPeek();
                        await System.Threading.Tasks.Task.Delay(700);
                        foreach (var d in _dockWindows)
                        {
                            int ex = d.Handle != IntPtr.Zero ? Native.GetWindowLong(d.Handle, Native.GWL_EXSTYLE) : 0;
                            bool topmost = (ex & 0x00000008) != 0;   // WS_EX_TOPMOST
                            LogError("peekdiag", new Exception(
                                $"cycle={cycle} dock='{d.Dock.Name}' vis={d.IsVisible} w={d.ActualWidth:F0} " +
                                $"peek?={d.PeekActiveForTest} wpfTopmost={d.Topmost} winTopmost={topmost} handle={(d.Handle != IntPtr.Zero)}"));
                        }
                        await System.Threading.Tasks.Task.Delay(200);
                        ExitPeek();
                        await System.Threading.Tasks.Task.Delay(600);
                    }
                    LogError("peekdiag", new Exception("=== DONE ==="));
                }));
        }

        // ================= горячая клавиша «поверх окон» ==================
        private void RegisterHotkey()
        {
            try
            {
                var p = new HwndSourceParameters("DeskFoldersMsg")
                {
                    Width = 1,
                    Height = 1,
                    ParentWindow = new IntPtr(-3) // HWND_MESSAGE — окно только для сообщений
                };
                _msgSource = new HwndSource(p);
                _msgSource.AddHook(WndProc);
                uint mods = _config.HotkeyModifiers | MOD_NOREPEAT;
                RegisterHotKey(_msgSource.Handle, HOTKEY_ID, mods, _config.HotkeyVk);
            }
            catch (Exception ex) { LogError("hotkey", ex); }
        }

        /// <summary>Меняет горячую клавишу «показать папки поверх окон» и перерегистрирует её.</summary>
        public bool UpdateHotkey(uint modifiers, uint vk)
        {
            _config.HotkeyModifiers = modifiers;
            _config.HotkeyVk = vk;
            SaveConfig();
            try
            {
                if (_msgSource == null) return false;
                UnregisterHotKey(_msgSource.Handle, HOTKEY_ID);
                return RegisterHotKey(_msgSource.Handle, HOTKEY_ID, modifiers | MOD_NOREPEAT, vk);
            }
            catch (Exception ex) { LogError("hotkey-update", ex); return false; }
        }

        /// <summary>Ссылка для поддержки автора (открывается в браузере).</summary>
        public const string DonateUrl = "https://yoomoney.ru/to/4100119273215272";

        public void OpenDonate() => Native.Launch(DonateUrl);

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                TogglePeek();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void TogglePeek()
        {
            if (_peekActive) ExitPeek();
            else EnterPeek();
        }

        private void EnterPeek()
        {
            if (_peekActive) return;
            if (_dockWindows.Count == 0) { OpenManager(null); return; }
            _peekActive = true;

            _scrim = new ScrimWindow();
            _scrim.Clicked += ExitPeek;
            // WPF повторно применяет Topmost к scrim уже после показа, поэтому
            // поднимаем папки над ним и сразу, и после первой отрисовки, и в
            // отложенном вызове — чтобы гарантированно оказались сверху ВСЕ папки.
            _scrim.ContentRendered += (s, e) => RaiseDocksAboveScrim();
            _scrim.Show();

            // Подсветку каждой папки включаем НЕЗАВИСИМО: если у одной случится сбой,
            // остальные всё равно подсветятся (раньше исключение обрывало цикл и
            // «часть папок не подсвечивалась»).
            foreach (var d in _dockWindows)
            {
                try { d.SetPeek(true); }
                catch (Exception ex) { LogError("peek-setpeek", ex); }
            }

            RaiseDocksAboveScrim();
            Dispatcher.BeginInvoke(new Action(RaiseDocksAboveScrim),
                System.Windows.Threading.DispatcherPriority.Loaded);

            // Несколько повторных подъёмов за первые ~360 мс перекрывают любые поздние
            // перестроения z-порядка (например, только что запущенным приложением),
            // чтобы ВСЕ папки оказались и остались поверх затемняющего слоя.
            int ticks = 0;
            _peekRaiseTimer?.Stop();
            _peekRaiseTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(60) };
            _peekRaiseTimer.Tick += (s, e) =>
            {
                if (!_peekActive || ++ticks > 6) { _peekRaiseTimer.Stop(); return; }
                RaiseDocksAboveScrim();
            };
            _peekRaiseTimer.Start();
        }

        /// <summary>Поднимает scrim над обычными окнами, а все папки — в topmost (над scrim).</summary>
        private void RaiseDocksAboveScrim()
        {
            if (!_peekActive || _scrim == null) return;

            // scrim не-topmost: поднимаем его на верх ОБЫЧНОГО слоя, чтобы затемнить окна
            IntPtr scrimHwnd = new WindowInteropHelper(_scrim).Handle;
            if (scrimHwnd != IntPtr.Zero) Native.RaiseToTop(scrimHwnd);

            // папки делаем topmost — ОС всегда рисует их выше не-topmost scrim
            foreach (var d in _dockWindows)
            {
                try { if (d.Handle != IntPtr.Zero) Native.RaiseTopmost(d.Handle); }
                catch (Exception ex) { LogError("peek-raise", ex); }
            }
        }

        private void ExitPeek()
        {
            if (!_peekActive) return;
            _peekActive = false;
            _peekRaiseTimer?.Stop();

            foreach (var d in _dockWindows)
            {
                try { d.CloseOpenPopup(); d.SetPeek(false); }
                catch (Exception ex) { LogError("peek-exit", ex); }
            }
            if (_scrim != null) { try { _scrim.Close(); } catch { } _scrim = null; }
        }

        /// <summary>Вызывается после запуска ярлыка — если активен режим «поверх окон», выйти из него.</summary>
        public void OnLaunched()
        {
            if (_peekActive) ExitPeek();
        }

        internal static void LogError(string where, Exception ex)
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppConfig.Dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppConfig.Dir, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}: {ex}\r\n\r\n");
            }
            catch { }
        }

        // само-тест: открыть папку и запустить первый ярлык (для отладки закрытия)
        private void RunSelfTest()
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(600);
                    if (_dockWindows.Count == 0) { LogError("selftest", new Exception("no docks")); return; }
                    var d = _dockWindows[0];
                    d.OpenPopupForTest();
                    await System.Threading.Tasks.Task.Delay(400);
                    d.LaunchFirstForTest();
                    await System.Threading.Tasks.Task.Delay(2500);
                    LogError("selftest", new Exception("STILL ALIVE after launch"));
                }
                catch (Exception exx) { LogError("selftest-catch", exx); }
            }));
        }

        // ================= окна-папки =====================================
        private void CreateDockWindows()
        {
            foreach (var w in _dockWindows) w.Close();
            _dockWindows.Clear();
            foreach (var dock in _config.Docks)
            {
                var w = new DockWindow(dock, this);
                w.Show();
                _dockWindows.Add(w);
            }
        }

        public void RefreshDocks()
        {
            CreateDockWindows();
        }

        /// <summary>Обновляет только вид одной папки (без пересоздания окон и кражи фокуса).</summary>
        public void UpdateDockVisual(DockConfig dock)
        {
            foreach (var w in _dockWindows)
                if (w.Dock == dock) { w.Rebuild(); return; }
        }

        public void SaveConfig() => _config?.Save();

        public AppConfig Config => _config;

        /// <summary>Переключает стиль размещения папок: свободный или по сетке.</summary>
        public void SetSnapToGrid(bool on)
        {
            if (_config == null) return;
            _config.SnapToGrid = on;
            if (on)
                foreach (var w in _dockWindows) w.ApplyGridSnap();   // сразу расставляем по сетке
            SaveConfig();
        }

        public void AddDock()
        {
            var dock = new DockConfig
            {
                Name = "Новая папка",
                X = 300 + _config.Docks.Count * 30,
                Y = 300 + _config.Docks.Count * 30
            };
            _config.Docks.Add(dock);
            SaveConfig();
            RefreshDocks();
            OpenManager(dock);
        }

        public void RemoveDock(DockConfig dock)
        {
            _config.Docks.Remove(dock);
            SaveConfig();
            RefreshDocks();
            _manager?.ReloadList();
        }

        public void NotifyManagerChanged()
        {
            _manager?.ReloadCurrent();
        }

        public void OpenManager(DockConfig select)
        {
            if (_manager == null || !_manager.IsLoaded)
            {
                _manager = new ManagerWindow(this);
                _manager.Closed += (s, e) => _manager = null;
                _manager.Show();
            }
            _manager.Activate();
            _manager.ReloadList();
            if (select != null) _manager.SelectDock(select);
        }

        // ================= трей ===========================================
        private void BuildTray()
        {
            _tray = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Visible = true,
                Text = "DeskFolders — папки на рабочем столе"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Менеджер папок…", null, (s, a) => OpenManager(null));
            menu.Items.Add("Добавить папку", null, (s, a) => AddDock());
            menu.Items.Add(new ToolStripSeparator());

            var hoverItem = new ToolStripMenuItem("Открывать по наведению мыши")
            {
                Checked = _config.OpenOnHover,
                CheckOnClick = true
            };
            hoverItem.CheckedChanged += (s, a) =>
            {
                _config.OpenOnHover = hoverItem.Checked;
                SaveConfig();
            };
            menu.Items.Add(hoverItem);

            _autoStartItem = new ToolStripMenuItem("Запускать при старте Windows")
            {
                Checked = IsAutoStartEnabled(),
                CheckOnClick = true
            };
            _autoStartItem.CheckedChanged += (s, a) =>
            {
                if (_syncingAutoStart) return;   // изменение пришло из кода — не зацикливаемся
                SetAutoStart(_autoStartItem.Checked);
            };
            menu.Items.Add(_autoStartItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("❤ Поддержать автора", null, (s, a) => OpenDonate());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, (s, a) => Shutdown());

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, a) => OpenManager(null);
        }

        /// <summary>Иконка приложения из самого exe (запасной вариант — системная).</summary>
        private static Icon LoadAppIcon()
        {
            try
            {
                var ico = Icon.ExtractAssociatedIcon(ExePath);
                if (ico != null) return ico;
            }
            catch { }
            return SystemIcons.Application;
        }

        // ================= автозапуск (HKCU\...\Run) ======================
        private static string ExePath =>
            Process.GetCurrentProcess().MainModule?.FileName ?? "";

        private const string ApprovedKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", false);
                if (run?.GetValue(RunKeyName) == null) return false;

                // Учитываем «Автозагрузку приложений» из Диспетчера задач: даже если
                // запись в Run есть, Windows не запустит приложение, когда оно там
                // отключено (первый байт данных нечётный, напр. 0x03).
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, false);
                if (approved?.GetValue(RunKeyName) is byte[] b && b.Length > 0 && (b[0] & 1) != 0)
                    return false;

                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Приводит запись автозапуска к актуальному пути exe. Если приложение
        /// пересобрали или переместили, старая запись в Run указывает на несуществующий
        /// файл — тогда «галочка стоит, а запуска нет». Здесь мы это чиним при каждом старте.
        /// </summary>
        private void SyncAutoStart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                if (key.GetValue(RunKeyName) is not string cur) return;   // автозапуск выключен

                string want = "\"" + ExePath + "\"";
                if (!string.Equals(cur, want, StringComparison.OrdinalIgnoreCase))
                    key.SetValue(RunKeyName, want);
            }
            catch (Exception ex) { LogError("autostart-sync", ex); }
        }

        /// <summary>Текущее состояние автозапуска (для галочек в трее и в окне настроек).</summary>
        public bool IsAutoStartOn() => IsAutoStartEnabled();

        /// <summary>Включает/выключает автозапуск. Можно вызывать из трея и из окна настроек.</summary>
        public void SetAutoStart(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enabled)
                    {
                        key.SetValue(RunKeyName, "\"" + ExePath + "\"");
                        // На случай, если приложение было отключено в «Автозагрузке» Диспетчера
                        // задач — снимаем отметку «отключено», иначе Windows его не запустит.
                        SetApprovedEnabled();
                    }
                    else
                    {
                        key.DeleteValue(RunKeyName, false);
                    }
                }
                _config.AutoStart = enabled;
                SaveConfig();
            }
            catch (Exception ex) { LogError("autostart", ex); }

            // синхронизируем галочку в трее и уведомляем окно настроек
            if (_autoStartItem != null && _autoStartItem.Checked != enabled)
            {
                _syncingAutoStart = true;
                _autoStartItem.Checked = enabled;
                _syncingAutoStart = false;
            }
            AutoStartChanged?.Invoke(enabled);
        }

        private static void SetApprovedEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, true)
                                ?? Registry.CurrentUser.CreateSubKey(ApprovedKeyPath);
                key?.SetValue(RunKeyName,
                    new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                    RegistryValueKind.Binary);
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            try
            {
                if (_msgSource != null)
                {
                    UnregisterHotKey(_msgSource.Handle, HOTKEY_ID);
                    _msgSource.RemoveHook(WndProc);
                    _msgSource.Dispose();
                }
            }
            catch { }
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
            base.OnExit(e);
        }
    }
}
