using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using RDReplay.Core;
using RDReplay.UI;

namespace RDReplay.Storage
{
    /// <summary>
    /// 回放文件夹管理：保存、加载、枚举回放，处理预览图获取逻辑。
    ///
    /// 目录结构：
    ///   {AppData}\...\RhythmDoctor\Replays\
    ///     {YYYY-MM-DD_HH-mm-ss}_{levelId}\
    ///       replay.json    —— 完整数据（已签名）
    ///       meta.json      —— 轻量元数据
    ///       preview.png    —— 预览图
    /// </summary>
    public static class ReplayFileManager
    {
        // ── 路径 ────────────────────────────────────────────────────

        /// <summary>回放根目录，懒加载（支持 ReplaySettings 自定义覆盖）。</summary>
        public static string ReplaysRoot
        {
            get
            {
                if (string.IsNullOrEmpty(_replaysRoot))
                {
                    // 优先使用用户在设置中自定义的根目录
                    string overrideRoot = ReplaySettings.ReplaysRootOverride;
                    if (!string.IsNullOrEmpty(overrideRoot))
                    {
                        _replaysRoot = overrideRoot;
                    }
                    else
                    {
                        _replaysRoot = Path.Combine(
                            Application.persistentDataPath,
                            "RhythmDoctor", "Replays");
                    }
                    Directory.CreateDirectory(_replaysRoot);
                }
                return _replaysRoot;
            }
        }
        private static string _replaysRoot;

        /// <summary>
        /// 当用户在设置中修改回放根目录时，重置缓存，下次访问 ReplaysRoot 时重新计算。
        /// </summary>
        public static void RefreshRootFromSettings()
        {
            _replaysRoot = null;
        }

        // ── 保存 ────────────────────────────────────────────────────

        /// <summary>
        /// 保存完整回放数据。
        /// 创建以时间戳命名的子文件夹，写入 replay.json 和 meta.json。
        /// 预览图由外部调用 <see cref="CopyPreviewImage"/> 或 <see cref="TakeScreenshotCoroutine"/> 写入。
        /// </summary>
        /// <returns>回放文件夹的完整路径</returns>
        public static string SaveReplay(ReplayData data)
        {
            string safeLevelId = SanitizeFileName(data.levelId);
            string timestamp   = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string folderName  = $"{timestamp}_{safeLevelId}";
            string folderPath  = Path.Combine(ReplaysRoot, folderName);
            Directory.CreateDirectory(folderPath);

            // replay.json（带签名）
            string replayJson  = ReplayCrypto.SignAndSerialize(data);
            string replayPath  = Path.Combine(folderPath, "replay.json");
            File.WriteAllText(replayPath, replayJson);
            long fileSizeBytes = new FileInfo(replayPath).Length;

            // meta.json（轻量，无签名，含文件大小）
            var meta = BuildMeta(data, "preview.png", fileSizeBytes);
            string metaJson = JsonConvert.SerializeObject(meta, Formatting.Indented);
            File.WriteAllText(Path.Combine(folderPath, "meta.json"), metaJson);

            Plugin.Log.LogInfo($"[FileManager] Saved replay to: {folderPath}");

            // 弹出右上角提示
            ReplayToastUI.ShowReplaySaved();
            return folderPath;
        }

        /// <summary>
        /// 从磁盘加载完整回放数据并验证签名。
        /// </summary>
        /// <param name="replayFolder">回放文件夹路径</param>
        /// <param name="data">反序列化结果</param>
        /// <returns>验证是否通过</returns>
        public static bool LoadReplay(string replayFolder, out ReplayData data)
        {
            data = null;
            string path = Path.Combine(replayFolder, "replay.json");

            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning($"[FileManager] replay.json not found: {path}");
                return false;
            }

            string json = File.ReadAllText(path);
            return ReplayCrypto.VerifyAndDeserialize(json, out data);
        }

        /// <summary>
        /// 枚举所有回放文件夹，读取各自的 meta.json，返回元数据列表（倒序排列，最新在前）。
        /// </summary>
        public static List<(string folder, ReplayMeta meta)> ListReplays()
        {
            var result = new List<(string, ReplayMeta)>();

            if (!Directory.Exists(ReplaysRoot)) return result;

            string[] dirs = Directory.GetDirectories(ReplaysRoot);
            // 倒序（文件夹名含时间戳，字典序倒排 = 时间倒序）
            Array.Sort(dirs, (a, b) => string.Compare(b, a, StringComparison.Ordinal));

            foreach (string dir in dirs)
            {
                string metaPath = Path.Combine(dir, "meta.json");
                if (!File.Exists(metaPath)) continue;

                try
                {
                    string json = File.ReadAllText(metaPath);
                    var meta = JsonConvert.DeserializeObject<ReplayMeta>(json);
                    if (meta != null)
                        result.Add((dir, meta));
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[FileManager] Failed to read meta: {metaPath} — {ex.Message}");
                }
            }

            return result;
        }

        // ── 预览图 ──────────────────────────────────────────────────

        /// <summary>
        /// 尝试从关卡目录复制预览图到回放文件夹。
        /// 关卡预览图命名规则：levelDir/preview.png（RD 标准）。
        /// </summary>
        /// <returns>是否成功复制</returns>
        public static bool CopyPreviewImage(string levelDir, string replayFolder)
        {
            if (string.IsNullOrEmpty(levelDir)) return false;

            // RD 标准预览图：preview.png（首选）或 preview.jpg
            foreach (string name in new[] { "preview.png", "preview.jpg" })
            {
                string src = Path.Combine(levelDir, name);
                if (File.Exists(src))
                {
                    string dst = Path.Combine(replayFolder, "preview.png");
                    File.Copy(src, dst, overwrite: true);
                    Plugin.Log.LogInfo($"[FileManager] Copied preview: {src} → {dst}");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 若关卡没有预览图，截取当前屏幕帧作为预览。
        /// 必须在 MonoBehaviour 中作为协程运行（需要等待帧末 WaitForEndOfFrame）。
        /// </summary>
        public static IEnumerator TakeScreenshotCoroutine(string replayFolder)
        {
            yield return new WaitForEndOfFrame();

            int w = Screen.width;
            int h = Screen.height;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.Destroy(tex);

            string dst = Path.Combine(replayFolder, "preview.png");
            File.WriteAllBytes(dst, png);
            Plugin.Log.LogInfo($"[FileManager] Screenshot saved to: {dst}");
        }

        // ── 内部工具 ─────────────────────────────────────────────────

        private static ReplayMeta BuildMeta(ReplayData data, string previewFileName, long fileSizeBytes = 0)
        {
            return new ReplayMeta
            {
                version       = data.version,
                gameVersion   = data.gameVersion,
                levelId       = data.levelId,
                levelName     = data.levelName,
                levelAuthor   = data.levelAuthor,
                isCustomLevel = data.isCustomLevel,
                levelSpeed    = data.levelSpeed,
                twoPlayerMode = data.twoPlayerMode,
                recordedAt    = data.recordedAt,
                duration      = data.duration,
                rank          = data.result?.rank,
                mistakes      = data.result?.mistakes ?? 0f,
                fileSizeBytes = fileSizeBytes,
                previewImage  = previewFileName,
            };
        }

        private static string SanitizeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            // 限制长度，避免路径过长
            if (s.Length > 40) s = s.Substring(0, 40);
            return s;
        }
    }
}
