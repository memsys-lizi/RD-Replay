using System.Collections;
using HarmonyLib;
using UnityEngine;
using RDReplay.Core;
using RDReplay.Storage;

namespace RDReplay.Patches
{
    /// <summary>
    /// Patch：游戏结束时（EndLevel）停止录制并等待玩家按 Ctrl+R 保存。
    /// </summary>
    [HarmonyPatch(typeof(scnGame), nameof(scnGame.EndLevel))]
    public static class Patch_EndLevel
    {
        [HarmonyPrefix]
        public static void Prefix(scnGame __instance)
        {
            if (ReplayContext.CurrentMode == ReplayMode.Recording)
            {
                var recorder = ReplayRecorder.Instance;
                if (recorder == null || !recorder.IsRecording) return;

                // 收集成绩
                var mm = __instance.mistakesManager;
                // mRank 是 Rankscreen 私有字段，通过 Traverse 读取
                string rankStr = "";
                if (__instance.rankscreen != null)
                {
                    var rankVal = Traverse.Create(__instance.rankscreen).Field("mRank").GetValue<Rank>();
                    rankStr = rankVal.ToString();
                }

                var result = new ReplayResult
                {
                    rank           = rankStr,
                    mistakes       = mm.mistakes,
                    mistakesP1     = mm.mistakesP1,
                    mistakesP2     = mm.mistakesP2,
                    earlyOffsetsSumP1 = mm.earlyOffsetsSumP1,
                    lateOffsetsSumP1  = mm.lateOffsetsSumP1,
                    earlyOffsetsSumP2 = mm.earlyOffsetsSumP2,
                    lateOffsetsSumP2  = mm.lateOffsetsSumP2,
                };

                ReplayData data = recorder.Finish(result);
                ReplayRecorder.DestroyInstance();
                ReplayContext.CurrentMode = ReplayMode.None;

                if (data != null)
                {
                    // 缓存等待玩家按 Ctrl+R 确认保存
                    ReplayContext.PendingReplay = data;
                    ReplayContext.PendingReplayFolder = null;

                    // 获取关卡目录（自定义关卡）
                    string levelDir = GetLevelDirectory(__instance);

                    // 启动协程做后续处理（截图/复制预览图）
                    SaveCoordinator.Instance?.StartCoroutine(
                        SaveCoordinator.PreparePreviewCoroutine(data, levelDir));

                    Plugin.Log.LogInfo("[Patch_EndLevel] Replay ready. Press Ctrl+R on results screen to save.");
                }
            }
            else if (ReplayContext.CurrentMode == ReplayMode.Replaying)
            {
                // 回放模式下的清理交给 Postfix 处理（需要先应用录制时的成绩结果）
                ReplayPlayer.Instance?.Stop();
            }
        }

        [HarmonyPostfix]
        public static void Postfix(scnGame __instance)
        {
            if (ReplayContext.CurrentMode != ReplayMode.Replaying) return;
            if (ReplayPlayer.Instance == null) return;

            // 回放一局结束后清理状态 + 触发退出事件
            ReplayPlayer.DestroyInstance();
            ReplayContext.CurrentMode = ReplayMode.None;
            ReplayModeEvents.RaiseReplayStopped();
        }

        internal static string GetLevelDirectory(scnGame game)
        {
            // 自定义关卡：currentLevel.levelPath 包含 .rdlevel 完整路径
            // 内置关卡：levelPath 为空
            var lvl = game.currentLevel;
            if (lvl == null) return null;

            // RD 中自定义关卡通过 levelPath 字段（LevelBase）访问文件路径
            var pathField = typeof(LevelBase).GetField("levelPath",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (pathField == null) return null;

            string path = pathField.GetValue(lvl) as string;
            if (string.IsNullOrEmpty(path)) return null;

            return System.IO.Path.GetDirectoryName(path);
        }
    }

    /// <summary>
    /// Patch：在所有继承自 scnBase 的场景里全局监听 Ctrl+R。
    /// 只要本局存在 PendingReplay，玩家在任意场景按下 Ctrl+R 都会触发保存。
    /// </summary>
    [HarmonyPatch(typeof(scnBase), "Update")]
    public static class Patch_CtrlR_Global
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool r    = Input.GetKeyDown(KeyCode.R);

            if (!ctrl || !r) return;

            // 情况 1：已有待保存的完整录像（EndLevel 之后）
            if (ReplayContext.PendingReplay != null)
            {
                SaveCoordinator.Instance?.StartCoroutine(
                    SaveCoordinator.SaveReplayCoroutine(ReplayContext.PendingReplay));
                ReplayContext.PendingReplay = null;
                Plugin.Log.LogInfo("[Patch_CtrlR] Ctrl+R detected (global), saving pending replay...");
                return;
            }

            // 情况 2：正在录制中，允许在任意时间按 Ctrl+R 直接完成并保存当前录像（半程也可以）
            if (ReplayContext.CurrentMode == ReplayMode.Recording &&
                ReplayRecorder.Instance != null &&
                ReplayRecorder.Instance.IsRecording &&
                scnBase.instance is scnGame game)
            {
                var recorder = ReplayRecorder.Instance;

                var mm = game.mistakesManager;
                var result = new ReplayResult
                {
                    // 中途保存，没有最终 Rank，用占位字符串
                    rank              = "(InProgress)",
                    mistakes          = mm.mistakes,
                    mistakesP1        = mm.mistakesP1,
                    mistakesP2        = mm.mistakesP2,
                    earlyOffsetsSumP1 = mm.earlyOffsetsSumP1,
                    lateOffsetsSumP1  = mm.lateOffsetsSumP1,
                    earlyOffsetsSumP2 = mm.earlyOffsetsSumP2,
                    lateOffsetsSumP2  = mm.lateOffsetsSumP2,
                };

                ReplayData data = recorder.Finish(result);
                ReplayRecorder.DestroyInstance();
                ReplayContext.CurrentMode = ReplayMode.None;

                if (data != null)
                {
                    // 和 EndLevel 情况一样，准备预览图再保存
                    string levelDir = Patch_EndLevel.GetLevelDirectory(game);
                    SaveCoordinator.Instance?.StartCoroutine(
                        SaveCoordinator.PreparePreviewCoroutine(data, levelDir));
                    SaveCoordinator.Instance?.StartCoroutine(
                        SaveCoordinator.SaveReplayCoroutine(data));

                    Plugin.Log.LogInfo("[Patch_CtrlR] Ctrl+R detected during recording, saved current run.");
                }
            }
        }
    }
}
