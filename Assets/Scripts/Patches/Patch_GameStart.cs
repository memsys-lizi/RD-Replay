using HarmonyLib;
using RDReplay.Core;
using RDReplay.UI;
using RDReplay.Storage;
using UnityEngine;

namespace RDReplay.Patches
{
    /// <summary>
    /// Patch 1：游戏开始时初始化录制器，记录关卡元数据和 DSP 基准时间。
    /// 目标：<see cref="scnGame.StartTheGame"/>（Prefix，在协程真正运行前拿不到实例，
    ///       因此改用 IEnumerator MoveNext Postfix 在第一帧后捕获）。
    ///
    /// 实现思路：
    ///   - Prefix 标记 "本次游戏开始"
    ///   - 在 scnGame.Update 的首帧（gameState 变为 Handmode 后）正式调用 Begin()
    ///     （见 Patch_Update.cs）
    /// </summary>
    [HarmonyPatch(typeof(scnGame), nameof(scnGame.StartTheGame))]
    public static class Patch_GameStart
    {
        [HarmonyPrefix]
        public static void Prefix(scnGame __instance, float speed)
        {
            // 清理上一局残留状态
            if (ReplayContext.CurrentMode == ReplayMode.Recording)
            {
                ReplayRecorder.Instance?.Abort();
                ReplayRecorder.DestroyInstance();
            }

            // 回放模式时由 ReplayListUI 提前设置好了 ReplayContext.CurrentMode 和 ReplayPlayer，
            // 这里只需重置 RecorderBegan，让 Patch_Update 在 Handmode 时调用 ReplayPlayer.Begin()
            // 如果 ReplayPlayer.Instance 为 null（重启导致），则重新加载录像文件
            if (ReplayContext.CurrentMode == ReplayMode.Replaying)
            {
                ReplayContext.RecorderBegan = false;

                // 检查 ReplayPlayer 是否存在，如果不存在（重启导致），则重新加载录像
                if (ReplayPlayer.Instance == null)
                {
                    if (!string.IsNullOrEmpty(ReplayContext.CurrentReplayFolder))
                    {
                        Plugin.Log.LogInfo("[Patch_GameStart] Replay mode detected but ReplayPlayer is null, reloading from file.");

                        if (ReplayFileManager.LoadReplay(ReplayContext.CurrentReplayFolder, out ReplayData data))
                        {
                            ReplayPlayer.CreateInstance(data);
                            ReplayToastUI.Show("重新加载回放");
                        }
                        else
                        {
                            Plugin.Log.LogError("[Patch_GameStart] Failed to reload replay file!");
                            ReplayToastUI.Show("回放加载失败");
                            ReplayContext.CurrentMode = ReplayMode.None;
                            return;
                        }
                    }
                    else
                    {
                        Plugin.Log.LogError("[Patch_GameStart] Replay mode detected but no replay folder path available! Aborting replay.");
                        ReplayContext.CurrentMode = ReplayMode.None;
                        return;
                    }
                }
                else
                {
                    Plugin.Log.LogInfo("[Patch_GameStart] Replay mode detected, RecorderBegan reset.");
                }

                return;
            }

            // 标记即将开始录制（实际 Begin() 在 Handmode 首帧触发）
            ReplayContext.CurrentMode = ReplayMode.Recording;
            ReplayContext.PendingGameInstance = __instance;
            ReplayContext.PendingLevelSpeed = speed;
            ReplayContext.RecorderBegan = false; // 重置，让 Patch_Update 的 Handmode 检测重新触发

            ReplayRecorder.CreateInstance();
            Plugin.Log.LogInfo("[Patch_GameStart] Recording pending, waiting for Handmode...");
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            // 回放模式：在游戏设置完倍速后，用录像中的倍速覆盖
            if (ReplayContext.CurrentMode == ReplayMode.Replaying && ReplayPlayer.Instance != null)
            {
                float recordedSpeed = ReplayPlayer.Instance.Data.levelSpeed;
                RDTime.speed = recordedSpeed;
            }
        }
    }

    /// <summary>
    /// Patch 2：游戏场景 Quit/EndLevel 后清理状态。
    /// </summary>
    [HarmonyPatch(typeof(scnGame), nameof(scnGame.Quit))]
    public static class Patch_GameQuit
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (ReplayContext.CurrentMode == ReplayMode.Recording)
            {
                ReplayRecorder.Instance?.Abort();
                ReplayRecorder.DestroyInstance();
                ReplayContext.CurrentMode = ReplayMode.None;
                Plugin.Log.LogInfo("[Patch_GameQuit] Recorder aborted on Quit.");
                ReplayToastUI.Show("录制已取消");
                // 录制模式下，保持游戏原有的退回逻辑（返回关卡选择等）
                return true;
            }
            else if (ReplayContext.CurrentMode == ReplayMode.Replaying)
            {
                ReplayPlayer.Instance?.Stop();
                ReplayPlayer.DestroyInstance();
                ReplayContext.CurrentMode = ReplayMode.None;
                ReplayContext.CurrentReplayFolder = null; // 清除文件夹路径
                Plugin.Log.LogInfo("[Patch_GameQuit] Player stopped on Quit.");
                ReplayToastUI.Show("回放已停止");
                ReplayModeEvents.RaiseReplayStopped();

                // 回放模式：退出时一律返回回放列表场景，而不是原本的关卡选择/上一场景
                Time.timeScale = 1f; // 确保从暂停菜单退出后恢复时间流逝，否则后续场景中的 DOTween/Update 不会运行
                scnBase.GoToScene("ScnReplay");
                return false; // 跳过原始 Quit 逻辑，避免再跳回 LevelSelect
            }

            // 非录制/回放模式，保持原始 Quit 行为
            return true;
        }
    }
}
