using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskFolders
{
    public class ShortcutItem
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";   // пусто = взять из имени файла
    }

    public class DockConfig
    {
        public string Name { get; set; } = "Папка";
        public double X { get; set; } = 200;
        public double Y { get; set; } = 200;
        public List<ShortcutItem> Items { get; set; } = new List<ShortcutItem>();
    }

    public class AppConfig
    {
        public List<DockConfig> Docks { get; set; } = new List<DockConfig>();
        public bool AutoStart { get; set; } = false;
        public bool OpenOnHover { get; set; } = false;

        // Размещение папок на рабочем столе: false = свободное (тащим куда угодно),
        // true = по сетке (как обычные ярлыки). Размер ячейки — в пикселях.
        public bool SnapToGrid { get; set; } = false;
        public int GridCellWidth { get; set; } = 100;
        public int GridCellHeight { get; set; } = 110;
        public int GridOriginX { get; set; } = 20;
        public int GridOriginY { get; set; } = 20;

        // Горячая клавиша «показать папки поверх окон».
        // Модификаторы: 1=Alt, 2=Ctrl, 4=Shift, 8=Win (можно складывать).
        // По умолчанию Ctrl+Shift+D (2|4 = 6, код клавиши D = 0x44).
        public uint HotkeyModifiers { get; set; } = 0x0006;
        public uint HotkeyVk { get; set; } = 0x44;

        // ---- загрузка / сохранение --------------------------------------
        [JsonIgnore]
        public static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "DeskFolders");

        [JsonIgnore]
        public static string FilePath => Path.Combine(Dir, "config.json");

        [JsonIgnore]
        public static string TmpPath => Path.Combine(Dir, "config.tmp");

        [JsonIgnore]
        public static string BakPath => Path.Combine(Dir, "config.bak");

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static AppConfig Load()
        {
            // 1) основной файл
            var cfg = TryLoadFrom(FilePath);
            if (cfg != null) return cfg;

            // 2) основной файл повреждён/не читается — восстанавливаем из резервной копии
            cfg = TryLoadFrom(BakPath);
            if (cfg != null)
            {
                App.LogError("config",
                    new Exception("config.json повреждён — конфигурация восстановлена из config.bak"));
                try { cfg.Save(); } catch { }   // сразу чиним основной файл
                return cfg;
            }

            // 3) ничего не удалось прочитать. Если файл всё же был — сохраняем его копию,
            //    чтобы данные не пропали молча, и только потом стартуем с нуля.
            try
            {
                if (File.Exists(FilePath))
                {
                    string dst = Path.Combine(Dir, $"config.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    File.Copy(FilePath, dst, true);
                    App.LogError("config",
                        new Exception($"config.json не читается; сохранена копия: {dst}"));
                }
            }
            catch { }

            return CreateDefault();
        }

        private static AppConfig TryLoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;   // пустой = недописанный
                return JsonSerializer.Deserialize<AppConfig>(json);
            }
            catch
            {
                return null;
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string json = JsonSerializer.Serialize(this, Opts);

                // Атомарная запись: сначала во временный файл целиком, затем подмена.
                // Если процесс прервётся (перезагрузка/выключение) во время записи,
                // пострадает только config.tmp, а config.json останется валидным.
                File.WriteAllText(TmpPath, json);

                if (File.Exists(FilePath))
                    File.Replace(TmpPath, FilePath, BakPath);   // прежний файл -> config.bak
                else
                    File.Move(TmpPath, FilePath);
            }
            catch (Exception ex)
            {
                App.LogError("config-save", ex);
            }
        }

        /// <summary>Стартовая конфигурация с одной демонстрационной папкой.</summary>
        public static AppConfig CreateDefault()
        {
            var cfg = new AppConfig();
            var dock = new DockConfig { Name = "Приложения", X = 300, Y = 300 };

            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            void Add(string file, string title)
            {
                string p = Path.Combine(sys, file);
                if (File.Exists(p)) dock.Items.Add(new ShortcutItem { Path = p, Name = title });
            }
            Add("notepad.exe", "Блокнот");
            Add("calc.exe", "Калькулятор");
            Add("mspaint.exe", "Paint");
            Add("SnippingTool.exe", "Ножницы");

            cfg.Docks.Add(dock);
            return cfg;
        }
    }
}
