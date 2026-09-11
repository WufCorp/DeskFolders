using System;
using System.IO;

namespace DeskFolders
{
    /// <summary>
    /// Хранилище ярлыков. Файлы ярлыков (.lnk/.url) копируются в собственную папку,
    /// чтобы они продолжали работать, даже если исходный ярлык удалить с рабочего стола.
    /// </summary>
    internal static class ShortcutStore
    {
        public static string Dir => Path.Combine(AppConfig.Dir, "shortcuts");

        /// <summary>
        /// Возвращает путь, который нужно сохранить в конфиге. Для ярлыков (.lnk/.url)
        /// делает независимую копию в хранилище; для остального оставляет исходный путь.
        /// </summary>
        public static string Import(string sourcePath)
        {
            try
            {
                if (string.IsNullOrEmpty(sourcePath)) return sourcePath;
                string ext = Path.GetExtension(sourcePath).ToLowerInvariant();

                // готовый ярлык — копируем как есть
                if (ext == ".lnk" || ext == ".url")
                {
                    if (!File.Exists(sourcePath)) return sourcePath;
                    Directory.CreateDirectory(Dir);
                    string id = Guid.NewGuid().ToString("N").Substring(0, 8);
                    string dest = Path.Combine(Dir, id + "_" + Path.GetFileName(sourcePath));
                    File.Copy(sourcePath, dest, true);
                    return dest;
                }

                // программа — создаём на неё ярлык (.lnk) с чистым именем
                if (ext == ".exe" || ext == ".bat" || ext == ".cmd" || ext == ".com")
                {
                    if (!File.Exists(sourcePath)) return sourcePath;
                    string lnk = CreateShortcut(sourcePath);
                    return lnk ?? sourcePath;
                }

                // всё остальное (папки, документы) — путь как есть
                return sourcePath;
            }
            catch
            {
                return sourcePath;
            }
        }

        /// <summary>Создаёт .lnk на программу в хранилище. Имя ярлыка — без расширения.</summary>
        private static string CreateShortcut(string targetPath)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string id = Guid.NewGuid().ToString("N").Substring(0, 8);
                string name = Path.GetFileNameWithoutExtension(targetPath);
                string lnkPath = Path.Combine(Dir, id + "_" + name + ".lnk");

                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                dynamic shell = Activator.CreateInstance(t);
                dynamic sc = shell.CreateShortcut(lnkPath);
                sc.TargetPath = targetPath;
                try { sc.WorkingDirectory = Path.GetDirectoryName(targetPath); } catch { }
                sc.Save();
                try
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sc);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }
                catch { }
                return File.Exists(lnkPath) ? lnkPath : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Удаляет копию из хранилища, если данный путь ей принадлежит.</summary>
        public static void Cleanup(string storedPath)
        {
            try
            {
                if (string.IsNullOrEmpty(storedPath)) return;
                string full = Path.GetFullPath(storedPath);
                string dir = Path.GetFullPath(Dir);
                if (full.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                    File.Delete(full);
            }
            catch { }
        }
    }
}
