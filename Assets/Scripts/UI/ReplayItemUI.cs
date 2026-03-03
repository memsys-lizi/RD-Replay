using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RDReplay.Core;
using RDReplay.Storage;
using RDReplay.Patches;

namespace RDReplay.UI
{
    /// <summary>
    /// 单条回放条目。负责自己所有的显示和操作。
    ///
    /// Prefab 节点绑定（Inspector 里拖拽）：
    ///   PreviewImage   (RawImage) — 预览图
    ///   LevelNameText  (TMP_Text) — 关卡名
    ///   AuthorText     (TMP_Text) — 关卡作者
    ///   ScoreText      (TMP_Text) — 分数：评级 + 失误量，如 "S  |  0 mistakes"
    ///   DurationText   (TMP_Text) — 时长，如 "2:34"
    ///   DateText       (TMP_Text) — 录制时间，如 "2026-03-02 13:20"
    ///   FileSizeText   (TMP_Text) — 文件大小，如 "20.1 MB"
    ///   PlayButton     (Button)  — 播放回放
    ///   DeleteButton   (Button)  — 删除录像
    /// </summary>
    public class ReplayItemUI : MonoBehaviour
    {
        [Header("内容")]
        public RawImage previewImage;  // 预览图
        public TMP_Text levelNameText;
        public TMP_Text authorText;
        public TMP_Text scoreText;      // 评级 + 失误量
        public TMP_Text durationText;
        public TMP_Text dateText;
        public TMP_Text fileSizeText;

        [Header("按钮")]
        public Button playButton;
        public Button deleteButton;

        // ── 内部状态 ────────────────────────────────────────────────
        private string        _folderPath;
        private ReplayListUI  _owner;   // 删除后回调刷新列表
        private float         _lastDeleteClickTime = -999f; // 上次点击删除按钮的时间
        private const float   DELETE_CONFIRM_WINDOW = 3f;   // 确认删除的时间窗口（秒）

        // ── 初始化 ───────────────────────────────────────────────────

        public void Setup(string folderPath, ReplayMeta meta, ReplayListUI owner)
        {
            _folderPath = folderPath;
            _owner      = owner;

            // 关卡名
            if (levelNameText != null)
                levelNameText.text = string.IsNullOrEmpty(meta.levelName)
                    ? (meta.levelId ?? "Unknown")
                    : meta.levelName;

            // 作者
            if (authorText != null)
                authorText.text = meta.levelAuthor ?? "";

            // 分数：评级 + 失误量
            // rank 是 RD 原生字母评级（S / A+ / B / F 等），mistakes 是血量损失总量
            if (scoreText != null)
            {
                string rank     = string.IsNullOrEmpty(meta.rank) ? "?" : meta.rank;
                float  m        = meta.mistakes;
                string mistakeStr = (m == Mathf.Floor(m))
                    ? $"{(int)m} mistakes"
                    : $"{m:F1} mistakes";
                scoreText.text = $"{rank}  |  {mistakeStr}";
            }

            // 时长
            if (durationText != null)
            {
                int mins = (int)(meta.duration / 60);
                int secs = (int)(meta.duration % 60);
                durationText.text = $"{mins}:{secs:D2}";
            }

            // 录制时间
            if (dateText != null)
            {
                if (System.DateTime.TryParse(meta.recordedAt, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    dateText.text = dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                else
                    dateText.text = meta.recordedAt ?? "";
            }

            // 文件大小
            if (fileSizeText != null)
                fileSizeText.text = FormatBytes(meta.fileSizeBytes);

            // 按钮
            playButton?.onClick.RemoveAllListeners();
            playButton?.onClick.AddListener(OnPlayClicked);

            deleteButton?.onClick.RemoveAllListeners();
            deleteButton?.onClick.AddListener(OnDeleteClicked);

            // 预览图（线程池异步读取，不卡主线程）
            if (previewImage != null && !string.IsNullOrEmpty(meta.previewImage))
            {
                string imgPath = Path.Combine(folderPath, meta.previewImage);
                if (File.Exists(imgPath))
                    StartCoroutine(LoadPreviewCoroutine(imgPath));
            }
        }

        // ── 播放 ─────────────────────────────────────────────────────

        private void OnPlayClicked()
        {
            if (!ReplayFileManager.LoadReplay(_folderPath, out ReplayData data))
            {
                Plugin.Log.LogWarning($"[ReplayItemUI] 签名校验失败，文件可能被篡改: {_folderPath}");
                ReplayToastUI.Show("回放文件校验失败");
                return;
            }

            ReplayContext.CurrentMode = ReplayMode.Replaying;
            ReplayContext.CurrentReplayFolder = _folderPath; // 保存文件夹路径，用于重启
            ReplayPlayer.CreateInstance(data);
            ReplayModeEvents.RaiseReplayStarted();

            // 设置静态倍速字段，让 GoToLevel 使用正确的倍速
            scnGame.levelSpeed = data.levelSpeed;

            scnBase.GoToLevel(data.levelId);
        }

        // ── 删除 ─────────────────────────────────────────────────────

        private void OnDeleteClicked()
        {
            float currentTime = Time.realtimeSinceStartup;
            float timeSinceLastClick = currentTime - _lastDeleteClickTime;

            // 如果在时间窗口内再次点击，执行删除
            if (timeSinceLastClick < DELETE_CONFIRM_WINDOW)
            {
                try
                {
                    Directory.Delete(_folderPath, recursive: true);
                    Plugin.Log.LogInfo($"[ReplayItemUI] 已删除: {_folderPath}");
                    ReplayToastUI.Show("回放已删除");
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning($"[ReplayItemUI] 删除失败: {ex.Message}");
                    ReplayToastUI.Show("删除失败");
                    return;
                }

                // 通知 ReplayListUI 刷新
                _owner?.RefreshList();

                // 重置时间
                _lastDeleteClickTime = -999f;
            }
            else
            {
                // 第一次点击，提示用户再次点击确认
                _lastDeleteClickTime = currentTime;
                ReplayToastUI.Show("再次点击删除按钮以确认");
            }
        }

        // ── 预览图加载 ───────────────────────────────────────────────

        private IEnumerator LoadPreviewCoroutine(string path)
        {
            byte[] bytes = null;
            bool   done  = false;

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try   { bytes = File.ReadAllBytes(path); }
                catch { bytes = null; }
                done = true;
            });

            while (!done) yield return null;

            if (bytes == null || bytes.Length == 0) yield break;

            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes))
                previewImage.texture = tex;   // 对RawImage赋予Texture
            else
                Destroy(tex);
        }

        // ── 工具 ─────────────────────────────────────────────────────

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)   return "";
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024)    return $"{kb:F1} KB";
            double mb = kb / 1024.0;
            return $"{mb:F1} MB";
        }
    }
}
