using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DynamicSky
{
    internal sealed class SkyPresets
    {
        internal sealed class Preset
        {
            internal readonly string Name;
            internal readonly IReadOnlyDictionary<string, string> Values;

            internal readonly string RussianName, Path, Source;
            internal readonly int Order;

            internal Preset(string name, IReadOnlyDictionary<string, string> values, string russianName = null,
                int order = 0, string path = null, string source = null)
            {
                Name = name;
                Values = values;
                RussianName = russianName;
                Order = order;
                Path = path;
                Source = source;
            }

            internal string DisplayName => RussianName == null ? Name : LegacyLocalizer.Text(Name, RussianName);
        }

        internal enum SaveResult { Saved, InvalidName, Exists }

        private readonly string directory;
        private const string ResourcePrefix = "DynamicSky.Presets/";

        internal SkyPresets(string directory)
        {
            this.directory = directory;
        }

        internal static SkyPresets ForGame() =>
            new SkyPresets(Path.Combine(LegacyHelpers.ModDataPath, "sky_presets"));

        internal static IReadOnlyList<Preset> LoadStandard(Action<string, Exception> reportError)
        {
            var assembly = typeof(SkyPresets).Assembly;
            var presets = new List<Preset>();
            foreach (string name in assembly.GetManifestResourceNames().OrderBy(key => key, StringComparer.Ordinal))
            {
                if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
                try
                {
                    using (var reader = new StreamReader(assembly.GetManifestResourceStream(name), Encoding.UTF8))
                        presets.Add(Read(reader.ReadToEnd(), null));
                }
                catch (Exception error) when (IsStorageError(error) || error is FormatException)
                {
                    reportError?.Invoke(name, error);
                }
            }
            return presets.OrderBy(preset => preset.Order).ToArray();
        }

        internal void OpenFolder()
        {
            Directory.CreateDirectory(directory);
            LegacyHelpers.OpenFolder(directory);
        }

        internal bool Delete(Preset preset)
        {
            // The display name can differ from the filename. Recheck the exact file
            // the user was shown, in case it was edited while confirmation was open.
            if (preset.Path == null || !File.Exists(preset.Path) ||
                File.ReadAllText(preset.Path, Encoding.UTF8) != preset.Source) return false;
            File.Delete(preset.Path);
            return true;
        }

        internal IReadOnlyList<Preset> LoadSaved(Action<string, Exception> reportError)
        {
            if (!Directory.Exists(directory)) return Array.Empty<Preset>();

            var presets = new List<Preset>();
            foreach (string path in Directory.EnumerateFiles(directory, "*.cfg"))
            {
                try
                {
                    presets.Add(Read(File.ReadAllText(path, Encoding.UTF8), path));
                }
                catch (Exception error) when (IsStorageError(error) || error is FormatException)
                {
                    reportError?.Invoke(path, error);
                }
            }
            return presets.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        internal SaveResult Save(string name, IEnumerable<KeyValuePair<string, string>> values)
        {
            name = name?.Trim();
            if (!IsValidFileName(name)) return SaveResult.InvalidName;

            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name + ".cfg");
            if (File.Exists(path)) return SaveResult.Exists;
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new StreamWriter(temporary, false, new UTF8Encoding(false)))
                {
                    file.WriteLine("# Dynamic Sky preset");
                    file.WriteLine("name = " + name);
                    foreach (var value in values) file.WriteLine(value.Key + " = " + value.Value);
                }
                File.Move(temporary, path);
                return SaveResult.Saved;
            }
            catch (IOException) when (File.Exists(path))
            {
                return SaveResult.Exists;
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        private static Preset Read(string source, string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = new StringReader(source))
            for (string raw = reader.ReadLine(); raw != null; raw = reader.ReadLine())
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int separator = line.IndexOf('=');
                if (separator <= 0) throw new FormatException("Expected a key and a value.");
                string key = line.Substring(0, separator).Trim();
                if (values.ContainsKey(key)) throw new FormatException("Duplicate key: " + key);
                values.Add(key, line.Substring(separator + 1).Trim());
            }
            if (path == null)
            {
                string english = Consume(values, "name_en");
                string russian = Consume(values, "name_ru");
                if (!int.TryParse(Consume(values, "order"), NumberStyles.None, CultureInfo.InvariantCulture, out int order))
                    throw new FormatException("Preset order must be a non-negative integer.");
                return new Preset(english, values, russian, order);
            }

            string name = Consume(values, "name");
            if (values.Count == 0) throw new FormatException("Preset has no settings.");
            return new Preset(name, values, path: path, source: source);
        }

        private static string Consume(Dictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                throw new FormatException("Missing preset field: " + key);
            values.Remove(key);
            return value;
        }

        private static bool IsValidFileName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 80 || name.EndsWith(".") ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return false;

            string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                stem == "CONIN$" || stem == "CONOUT$") return false;
            return !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                stem.StartsWith("LPT", StringComparison.Ordinal)) && "0123456789¹²³".Contains(stem[3]));
        }

        internal static bool IsStorageError(Exception error) =>
            error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException;
    }
}
