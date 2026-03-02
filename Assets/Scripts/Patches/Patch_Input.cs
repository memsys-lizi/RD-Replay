using HarmonyLib;
using AudioSettings = UnityEngine.AudioSettings;
using RDReplay.Core;

namespace RDReplay.Patches
{
    /// <summary>
    /// Patch：记录玩家按键（UpdateGameplayInput Prefix）。
    ///
    /// 录制模式：在 keyPressed / keyReleased 有效时记录到 ReplayRecorder。
    /// 回放模式：阻止真实键盘输入传入游戏（由 Patch_Update 注入录像中的按键事件）。
    /// </summary>
    [HarmonyPatch(typeof(scnGame), nameof(scnGame.UpdateGameplayInput))]
    public static class Patch_Input
    {
        [HarmonyPrefix]
        public static bool Prefix(RDPlayer player, bool keyPressed, bool keyReleased)
        {
            if (ReplayContext.CurrentMode == ReplayMode.Replaying)
            {
                // 回放模式下不再在这里做任何拦截，所有输入状态已经在 Patch_RDInput 中
                // 被录像数据覆盖，真实键盘的影响也因此被屏蔽。
                return true;
            }

            if (ReplayContext.CurrentMode == ReplayMode.Recording)
            {
                var recorder = ReplayRecorder.Instance;
                if (recorder == null || !recorder.IsRecording) return true;

                double audioPos = scrConductor.instance?.audioPos
                                  ?? AudioSettings.dspTime;

                if (keyPressed)
                    recorder.RecordInput(audioPos, (int)player, "press");

                if (keyReleased)
                    recorder.RecordInput(audioPos, (int)player, "release");
            }

            return true; // 录制模式不阻止原方法
        }
    }
}
