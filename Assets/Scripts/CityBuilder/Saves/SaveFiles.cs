using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CityBuilder.Content;

namespace CityBuilder.Saves
{
    /// <summary>A save file on disk, as listed by <see cref="SaveFiles.List"/>.</summary>
    public readonly struct SaveInfo
    {
        public readonly string Name;
        public readonly string Path;
        public readonly DateTime WrittenUtc;

        public SaveInfo(string name, string path, DateTime writtenUtc)
        {
            Name = name;
            Path = path;
            WrittenUtc = writtenUtc;
        }
    }

    /// <summary>
    /// The saves folder: one JSON file per save, named after it, in
    /// persistentDataPath/GameAssets/Saves -- next to the downloaded bundles, in the one folder a game
    /// can always write to. On Windows that is under %USERPROFILE%\AppData\LocalLow\&lt;company&gt;\&lt;product&gt;.
    ///
    /// Every call takes an optional folder so tests can work somewhere else.
    /// </summary>
    public static class SaveFiles
    {
        public const string FolderName = "Saves";
        public const string Extension = ".json";

        public static string Folder => Path.Combine(GameAssetPaths.Root, FolderName);

        /// <summary>
        /// Writes <paramref name="data"/> as <paramref name="name"/>, replacing any save of that name,
        /// and returns the file's path. It is written beside the old file first and swapped in, so a
        /// crash part way leaves the old save whole.
        /// </summary>
        public static string Write(SaveData data, string name, string folder = null)
        {
            folder ??= Folder;
            Directory.CreateDirectory(folder);

            string path = PathFor(name, folder);
            string temp = path + ".tmp";
            File.WriteAllText(temp, CitySave.ToJson(data), Encoding.UTF8);

            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
            return path;
        }

        /// <summary>Reads the save called <paramref name="name"/>. Returns null, with the reason in <paramref name="error"/>, when it cannot.</summary>
        public static SaveData Read(string name, out string error, string folder = null)
        {
            string path = PathFor(name, folder ?? Folder);
            if (!File.Exists(path))
            {
                error = "no such save.";
                return null;
            }

            string json;
            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (IOException e)
            {
                error = e.Message;
                return null;
            }
            return CitySave.FromJson(json, out error);
        }

        /// <summary>Every save in the folder, newest first. Empty when there is no folder yet.</summary>
        public static List<SaveInfo> List(string folder = null)
        {
            folder ??= Folder;
            var saves = new List<SaveInfo>();
            if (!Directory.Exists(folder)) return saves;

            foreach (string path in Directory.GetFiles(folder, "*" + Extension))
            {
                saves.Add(new SaveInfo(Path.GetFileNameWithoutExtension(path), path, File.GetLastWriteTimeUtc(path)));
            }
            saves.Sort((a, b) => b.WrittenUtc.CompareTo(a.WrittenUtc));
            return saves;
        }

        public static bool Delete(string name, string folder = null)
        {
            string path = PathFor(name, folder ?? Folder);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        /// <summary>
        /// <paramref name="name"/> made safe as a file name on every platform: characters a file
        /// system rejects become underscores, and an empty name becomes "save".
        /// </summary>
        public static string CleanName(string name)
        {
            var clean = new StringBuilder();
            foreach (char c in (name ?? string.Empty).Trim())
            {
                bool bad = Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || "<>:\"/\\|?*".IndexOf(c) >= 0;
                clean.Append(bad ? '_' : c);
            }

            string result = clean.ToString().Trim('.', ' ');
            return result.Length > 0 ? result : "save";
        }

        static string PathFor(string name, string folder) => Path.Combine(folder, CleanName(name) + Extension);
    }
}
