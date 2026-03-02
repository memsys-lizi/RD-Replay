using System.Collections;
using UnityEngine;
using RDReplay.Core;
using RDReplay.Storage;

namespace RDReplay.Patches
{
    /// <summary>
    /// 轻量 MonoBehaviour，用于在 Patch 层启动需要跨帧的协程
    /// （截图需要 WaitForEndOfFrame，必须在 MonoBehaviour 中执行）。
    ///
    /// 由 ModEntry.Awake() 挂载到一个持久 GameObject 上。
    /// </summary>
    public class SaveCoordinator : MonoBehaviour
    {
        public static SaveCoordinator Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ── 静态协程入口（由 Patch 层调用）──────────────────────────

        /// <summary>
        /// 游戏结束后准备预览图：
        /// 先尝试从关卡目录复制，失败则截屏。
        /// 结果暂存在 ReplayContext.PendingPreviewSource 中，
        /// 实际写入磁盘在 SaveReplayCoroutine 中进行。
        /// </summary>
        public static IEnumerator PreparePreviewCoroutine(ReplayData data, string levelDir)
        {
            // 先尝试复制关卡预览图（有关卡目录时直接记录路径，复制在保存时完成）
            if (!string.IsNullOrEmpty(levelDir))
            {
                ReplayContext.PendingLevelDir = levelDir;
                yield break;
            }

            // 如果已经在游戏过程中截过图，就不再重复截屏
            if (ReplayContext.PendingScreenshotBytes != null)
                yield break;

            // 没有关卡目录，截屏作为预览图
            yield return new WaitForEndOfFrame();

            int w = Screen.width;
            int h = Screen.height;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            ReplayContext.PendingScreenshotBytes = tex.EncodeToPNG();
            Destroy(tex);

            Plugin.Log.LogInfo("[SaveCoordinator] Screenshot taken as preview fallback.");
        }

        /// <summary>
        /// 在谱面运行过程中截取一张屏幕作为预览图（仅当当前还没有截图时）。
        /// 可在录制开始后尽早调用，以避免结果画面是全黑。
        /// </summary>
        public static IEnumerator CaptureMidRunScreenshotCoroutine()
        {
            if (ReplayContext.PendingScreenshotBytes != null)
                yield break;

            // 等大约 5 秒（真实时间），避免刚开始还是黑场 / 纯 UI
            yield return new WaitForSecondsRealtime(5f);

            yield return new WaitForEndOfFrame();

            int w = Screen.width;
            int h = Screen.height;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            ReplayContext.PendingScreenshotBytes = tex.EncodeToPNG();
            Destroy(tex);

            Plugin.Log.LogInfo("[SaveCoordinator] Screenshot taken (mid-run).");
        }

        /// <summary>
        /// 玩家按 Ctrl+R 后调用：保存完整回放数据到磁盘。
        /// </summary>
        public static IEnumerator SaveReplayCoroutine(ReplayData data)
        {
            yield return null; // 等一帧避免卡主线程

            string folder = ReplayFileManager.SaveReplay(data);
            ReplayContext.PendingReplayFolder = folder;

            // 写入预览图
            if (ReplayContext.PendingScreenshotBytes != null)
            {
                System.IO.File.WriteAllBytes(
                    System.IO.Path.Combine(folder, "preview.png"),
                    ReplayContext.PendingScreenshotBytes);
                ReplayContext.PendingScreenshotBytes = null;
                Plugin.Log.LogInfo("[SaveCoordinator] Screenshot preview written.");
            }
            else
            {
                string levelDir = ReplayContext.PendingLevelDir;
                if (!string.IsNullOrEmpty(levelDir))
                {
                    ReplayFileManager.CopyPreviewImage(levelDir, folder);
                }
                ReplayContext.PendingLevelDir = null;
            }

            Plugin.Log.LogInfo($"[SaveCoordinator] Replay saved: {folder}");

            // 显示保存成功提示（通过游戏 LED 字幕）
            try { LEDSign.status = "Replay Saved!"; } catch { }
        }
    }
}
