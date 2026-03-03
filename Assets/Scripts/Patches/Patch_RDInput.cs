using HarmonyLib;
using RDReplay.Core;
using UnityEngine;
using AudioSettings = UnityEngine.AudioSettings;

namespace RDReplay.Patches
{
    /// <summary>
    /// 统一在 RDInput.Update 中驱动录制 & 回放：
    /// - 录制模式：在 Postfix 中根据 RDInput 的各种 Press 字段，记录逻辑按键事件到 ReplayRecorder。
    /// - 回放模式：
    ///   - Prefix：根据当前 audioPos 从 ReplayPlayer 取出本帧应触发的输入事件（节奏键 + 逻辑按键），写入 ReplayContext。
    ///   - Postfix：用 ReplayContext 中的数据覆盖 RDInput.p1/p2 以及 Left/Right/Up/Down/Cancel/Skip/Restart/Quit 的 Press/IsPressed/Release，
    ///              让判定、菜单、关卡分支等逻辑完全按录像运行。
    /// </summary>
    [HarmonyPatch(typeof(RDInput), nameof(RDInput.Update))]
    public static class Patch_RDInput
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (ReplayContext.CurrentMode != ReplayMode.Replaying)
            {
                // 非回放模式，仅清理节奏键的本帧 Press/Release 标志
                ReplayContext.ReplayPress[0] = ReplayContext.ReplayPress[1] = false;
                ReplayContext.ReplayRelease[0] = ReplayContext.ReplayRelease[1] = false;
                return;
            }

            var player = ReplayPlayer.Instance;
            if (player == null || !player.IsPlaying)
            {
                ReplayContext.ReplayPress[0] = ReplayContext.ReplayPress[1] = false;
                ReplayContext.ReplayRelease[0] = ReplayContext.ReplayRelease[1] = false;
                return;
            }

            // 先清空节奏键和逻辑键的“本帧按下/抬起”标志，IsPressed 状态保持上一帧
            ReplayContext.ReplayPress[0] = ReplayContext.ReplayPress[1] = false;
            ReplayContext.ReplayRelease[0] = ReplayContext.ReplayRelease[1] = false;
            for (int i = 0; i < ReplayContext.LogicalPress.Length; i++)
            {
                ReplayContext.LogicalPress[i] = false;
                ReplayContext.LogicalRelease[i] = false;
            }

            double currentAudioPos = scrConductor.instance?.audioPos ?? AudioSettings.dspTime;

            // 1) 节奏键（P1/P2 Press/Release）
            var dueEvents = player.CollectDueInputs(currentAudioPos);
            if (dueEvents != null && dueEvents.Count > 0)
            {
                foreach (var ev in dueEvents)
                {
                    int idx = ev.player; // 0 = P1, 1 = P2
                    if (idx < 0 || idx > 1) continue;

                    bool isPress = ev.action == "press";
                    bool isRelease = ev.action == "release";

                    if (isPress)
                    {
                        ReplayContext.ReplayPress[idx] = true;
                        ReplayContext.ReplayIsPressed[idx] = true;
                    }

                    if (isRelease)
                    {
                        ReplayContext.ReplayRelease[idx] = true;
                        ReplayContext.ReplayIsPressed[idx] = false;
                    }
                }
            }

            // 2) RDInput 逻辑按键（Left/Right/Up/Down/Cancel/Skip/Restart/Quit）
            var logicalEvents = player.CollectDueLogicalInputs(currentAudioPos);
            if (logicalEvents != null && logicalEvents.Count > 0)
            {
                foreach (var ev in logicalEvents)
                {
                    int idx = (int)ev.type;
                    if (idx < 0 || idx >= ReplayContext.LogicalPress.Length) continue;

                    bool isPress = ev.phase == "press";
                    bool isRelease = ev.phase == "release";

                    if (isPress)
                    {
                        ReplayContext.LogicalPress[idx] = true;
                        ReplayContext.LogicalIsPressed[idx] = true;
                    }

                    if (isRelease)
                    {
                        ReplayContext.LogicalRelease[idx] = true;
                        ReplayContext.LogicalIsPressed[idx] = false;
                    }
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            // 录制模式：在 RDInput.Update 之后，读取本帧 RDInput 状态并记录逻辑按键事件
            if (ReplayContext.CurrentMode == ReplayMode.Recording)
            {
                var recorder = ReplayRecorder.Instance;
                if (recorder != null && recorder.IsRecording)
                {
                    double audioPos = scrConductor.instance?.audioPos ?? AudioSettings.dspTime;

                    if (RDInput.skipPressed)
                        recorder.RecordLogical(audioPos, LogicalInputType.Skip, "press");
                    if (RDInput.restartPressed)
                        recorder.RecordLogical(audioPos, LogicalInputType.Restart, "press");
                    if (RDInput.quitPressed)
                        recorder.RecordLogical(audioPos, LogicalInputType.Quit, "press");
                    if (RDInput.cancelPress)
                        recorder.RecordLogical(audioPos, LogicalInputType.Cancel, "press");
                    if (RDInput.leftPress)
                        recorder.RecordLogical(audioPos, LogicalInputType.Left, "press");
                    if (RDInput.rightPress)
                        recorder.RecordLogical(audioPos, LogicalInputType.Right, "press");
                    if (RDInput.upPress)
                        recorder.RecordLogical(audioPos, LogicalInputType.Up, "press");
                    if (RDInput.downPress)
                        recorder.RecordLogical(audioPos, LogicalInputType.Down, "press");

                    // RD 中导航/分支几乎都只看 WentDown（Press），Release/IsPressed 由引擎自己推导，
                    // 若未来发现有场景强依赖 Release，再按同样模式补一层 RecordLogical("release") 即可。
                }

                return;
            }

            // 回放模式：根据 ReplayContext 覆盖 RDInput 的状态
            if (ReplayContext.CurrentMode != ReplayMode.Replaying) return;
            if (ReplayPlayer.Instance == null || !ReplayPlayer.Instance.IsPlaying) return;
            // PreStart 阶段不拦截输入，让玩家可以按空格开始游戏
            if (scnBase.instance is scnGame g && g.gameState == GameState.PreStart) return;
            // 暂停菜单打开时，不接管输入，让玩家可以用键盘操作 RDPauseMenu
            if (scnBase.instance is scnGame game && game.paused) return;

            // 1) 节奏键
            RDInput.p1Press     = ReplayContext.ReplayPress[0];
            RDInput.p1IsPressed = ReplayContext.ReplayIsPressed[0];
            RDInput.p1Release   = ReplayContext.ReplayRelease[0];

            RDInput.p2Press     = ReplayContext.ReplayPress[1];
            RDInput.p2IsPressed = ReplayContext.ReplayIsPressed[1];
            RDInput.p2Release   = ReplayContext.ReplayRelease[1];

            RDInput.anyPlayerPress     = RDInput.p1Press     || RDInput.p2Press;
            RDInput.anyPlayerIsPressed = RDInput.p1IsPressed || RDInput.p2IsPressed;
            RDInput.anyPlayerRelease   = RDInput.p1Release   || RDInput.p2Release;

            // 2) 逻辑按键：Left/Right/Up/Down/Cancel/Skip/Restart/Quit
            RDInput.skipPressed   = ReplayContext.LogicalPress[(int)LogicalInputType.Skip];
            RDInput.restartPressed= ReplayContext.LogicalPress[(int)LogicalInputType.Restart];
            RDInput.quitPressed   = ReplayContext.LogicalPress[(int)LogicalInputType.Quit];

            RDInput.leftPress     = ReplayContext.LogicalPress[(int)LogicalInputType.Left];
            RDInput.leftIsPressed = ReplayContext.LogicalIsPressed[(int)LogicalInputType.Left];
            RDInput.leftRelease   = ReplayContext.LogicalRelease[(int)LogicalInputType.Left];

            RDInput.rightPress     = ReplayContext.LogicalPress[(int)LogicalInputType.Right];
            RDInput.rightIsPressed = ReplayContext.LogicalIsPressed[(int)LogicalInputType.Right];
            RDInput.rightRelease   = ReplayContext.LogicalRelease[(int)LogicalInputType.Right];

            RDInput.upPress     = ReplayContext.LogicalPress[(int)LogicalInputType.Up];
            RDInput.upIsPressed = ReplayContext.LogicalIsPressed[(int)LogicalInputType.Up];
            RDInput.upRelease   = ReplayContext.LogicalRelease[(int)LogicalInputType.Up];

            RDInput.downPress     = ReplayContext.LogicalPress[(int)LogicalInputType.Down];
            RDInput.downIsPressed = ReplayContext.LogicalIsPressed[(int)LogicalInputType.Down];
            RDInput.downRelease   = ReplayContext.LogicalRelease[(int)LogicalInputType.Down];
        }
    }
}

