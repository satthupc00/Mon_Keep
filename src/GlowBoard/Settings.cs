using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GlowBoard
{
    /// <summary>Lưu cài đặt nhỏ (vị trí cửa sổ, file đang mở…) dạng key=value.</summary>
    internal sealed class Settings
    {
        private readonly string _path;
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private Settings(string path) { _path = path; }

        public static Settings Load(string path)
        {
            var s = new Settings(path);
            try
            {
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) s._values[line.Substring(0, i).Trim()] = line.Substring(i + 1);
                    }
            }
            catch { /* file hỏng thì dùng mặc định */ }
            return s;
        }

        public string Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
        public int GetInt(string key, int def) => int.TryParse(Get(key), out var v) ? v : def;
        public bool GetBool(string key) => GetInt(key, 0) != 0;
        public void Set(string key, object value) => _values[key] = Convert.ToString(value ?? "").Replace("\r", "").Replace("\n", "");

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var sb = new StringBuilder();
                foreach (var kv in _values) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
