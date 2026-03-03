using HarmonyLib;
using RDReplay.Core;
using UnityEngine;

namespace RDReplay.Patches
{
    /// <summary>
    /// Patch：记录游戏状态变化（GameState 切换）。
    ///
    /// 录制模式：
    ///   监听 scnGame.gameState 的 setter，记录每次状态切换。
    ///
    /// 回放模式：
    ///   暂时不强制同步 gameState（因为状态切换通常由关卡逻辑驱动），
    ///   但记录下来可以用于调试和验证回放是否正确。
    /// </summary>
    [HarmonyPatch(typeof(scnGame), nameof(scnGame.gameState), MethodType.Setter)]
    public static class Patch_GameState
    {
        [HarmonyPrefix]
        public static void Prefix(scnGame __instance, GameState value)
        {
            if (ReplayContext.CurrentMode != ReplayMode.Recording) return;

            var recorder = ReplayRecorder.Instance;
            if (recorder == null || !recorder.IsRecording) return;

            // 获取当前状态（变化前）
            GameState fromState = __instance.gameState;
            GameState toState = value;

            // 如果状态没有变化，不记录
            if (fromState == toState) return;

            double audioPos = scrConductor.instance?.audioPos ?? UnityEngine.AudioSettings.dspTime;
            int barNumber = scrConductor.instance?.barNumber ?? 0;

            recorder.RecordGameState(audioPos, (int)fromState, (int)toState, barNumber);
        }
    }
}
