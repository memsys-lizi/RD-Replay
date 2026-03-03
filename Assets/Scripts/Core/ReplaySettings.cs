using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace RDReplay.Core
{
    /// <summary>
    /// 全局设置：回放保存路径、控制面板行为等。
    /// 使用 JSON 文件持久化到 Application.persistentDataPath 下。
    /// </summary>
    public static class ReplaySettings
    {
        private const string FileName = "RDReplaySettings.json";

        [Serializable]
        private class Data
        {
            public string replaysRootOverride;
            public bool autoOpenControlPanelOnReplay;
        }

        private static bool _loaded;
        private static Data _data;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                string dir = Application.persistentDataPath;
                string path = Path.Combine(dir, FileName);
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    _data = JsonConvert.DeserializeObject<Data>(json) ?? new Data();
                }
                else
                {
                    _data = new Data();
                }
            }
            catch (Exception)
            {
                _data = new Data();
            }
        }

        private static void Save()
        {
            try
            {
                string dir = Application.persistentDataPath;
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, FileName);
                string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Settings] Failed to save settings: {ex.Message}");
            }
        }

        /// <summary>
        /// 用户自定义的回放根目录（可为空 = 使用默认路径）。
        /// </summary>
        public static string ReplaysRootOverride
        {
            get
            {
                EnsureLoaded();
                return _data.replaysRootOverride;
            }
            set
            {
                EnsureLoaded();
                _data.replaysRootOverride = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                Save();
            }
        }

        /// <summary>
        /// 进入回放时是否自动展开控制 UI 面板。
        /// </summary>
        public static bool AutoOpenControlPanelOnReplay
        {
            get
            {
                EnsureLoaded();
                return _data.autoOpenControlPanelOnReplay;
            }
            set
            {
                EnsureLoaded();
                _data.autoOpenControlPanelOnReplay = value;
                Save();
            }
        }
    }
}

