using System.Collections;
using HarmonyLib;
using RDReplay.Core;
using UnityEngine;

namespace RDReplay.Patches
{
    /// <summary>
    /// Patch：拦截时间轴跳转（ScrubToBarNum），记录跳转事件并在回放时同步游标。
    ///
    /// 录制模式：
    ///   记录 ScrubToBarNum 调用的参数（fromBar, toBar, audioPos before/after）。
    ///
    /// 回放模式：
    ///   不主动调用 ScrubToBarNum（让游戏自然触发），只在 Postfix 中同步 ReplayPlayer 的游标。
    ///   这样可以确保跳转时机和录制时完全一致，避免时机不对导致的事件错位。
    /// </summary>
    [HarmonyPatch(typeof(scrConductor), nameof(scrConductor.ScrubToBarNum))]
    public static class Patch_TimeJump_ScrubToBarNum
    {
        [HarmonyPrefix]
        public static void Prefix(scrConductor __instance, int barNum)
        {
            if (ReplayContext.CurrentMode != ReplayMode.Recording) return;

            var recorder = ReplayRecorder.Instance;
            if (recorder == null || !recorder.IsRecording) return;

            // 记录跳转前的状态
            double audioPosBefore = __instance.audioPos;
            int fromBar = __instance.barNumber;

            // 将跳转信息暂存到 ReplayContext，等 Postfix 时记录跳转后的 audioPos
            ReplayContext.PendingTimeJumpFromBar = fromBar;
            ReplayContext.PendingTimeJumpToBar = barNum;
            ReplayContext.PendingTimeJumpAudioPosBefore = audioPosBefore;
        }

        [HarmonyPostfix]
        public static void Postfix(scrConductor __instance, int barNum)
        {
            // ── 录制模式：记录跳转事件 ──
            if (ReplayContext.CurrentMode == ReplayMode.Recording)
            {
                var recorder = ReplayRecorder.Instance;
                if (recorder == null || !recorder.IsRecording) return;

                // 检查是否有待记录的跳转
                if (ReplayContext.PendingTimeJumpFromBar == -1) return;

                // 记录跳转后的状态
                double audioPosAfter = __instance.audioPos;
                int fromBar = ReplayContext.PendingTimeJumpFromBar;
                int toBar = ReplayContext.PendingTimeJumpToBar;
                double audioPosBefore = ReplayContext.PendingTimeJumpAudioPosBefore;

                // 判断跳转原因（根据游戏状态推断）
                string reason = "unknown";
                if (__instance.game != null)
                {
                    var gameState = __instance.game.gameState;
                    if (gameState == GameState.Cutscene || gameState == GameState.CutsceneSkippable)
                    {
                        reason = "cutscene_skip";
                    }
                    else if (__instance.isScrubbing)
                    {
                        reason = "scrub";
                    }
                }

                recorder.RecordTimeJump(audioPosBefore, audioPosAfter, fromBar, toBar, reason);

                // 清空暂存
                ReplayContext.PendingTimeJumpFromBar = -1;
                ReplayContext.PendingTimeJumpToBar = -1;
                ReplayContext.PendingTimeJumpAudioPosBefore = 0.0;
            }

            // ── 回放模式：同步游标 ──
            else if (ReplayContext.CurrentMode == ReplayMode.Replaying)
            {
                var player = ReplayPlayer.Instance;
                if (player == null || !player.IsPlaying) return;

                // 获取当前 audioPos（真实 DSP 时间）
                double currentAudioPos = __instance.audioPos;
                int currentBar = __instance.barNumber;

                Plugin.Log.LogInfo($"[Patch_TimeJump] ========== TIME JUMP DETECTED ==========");
                Plugin.Log.LogInfo($"[Patch_TimeJump] Current bar: {currentBar}, Current audioPos: {currentAudioPos:F3}");

                // 使用录制时的 TimeJumpEvent 数据来同步
                player.SyncCursorsAfterTimeJump(currentAudioPos);

                Plugin.Log.LogInfo($"[Patch_TimeJump] ========== TIME JUMP SYNC COMPLETE ==========");
            }
        }
    }
}