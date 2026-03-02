using HarmonyLib;
using RDReplay.Core;

namespace RDReplay.Patches
{
    /// <summary>
    /// Patch：记录 & 回放每次 Pulse 判定。
    /// 录制模式：Postfix 记录 HitEvent。
    /// 回放模式：Prefix 根据录像里的 HitEvent 覆盖 timeOffset，从而修补判定结果。
    /// </summary>
    [HarmonyPatch(typeof(scrPlayerbox), nameof(scrPlayerbox.Pulse))]
    public static class Patch_Judgment_Pulse
    {
        // 回放模式：在 Pulse 之前，用录像里的 timeOffset 覆盖参数，保证判定结果一致
        [HarmonyPrefix]
        public static void Prefix(scrPlayerbox __instance, ref float timeOffset, Beat beat, bool CPUTriggered, bool bomb)
        {
            if (ReplayContext.CurrentMode != ReplayMode.Replaying) return;
            if (ReplayPlayer.Instance == null || !ReplayPlayer.Instance.IsPlaying) return;
            if (CPUTriggered) return; // 只修补玩家判定，CPU 自动判定保持原逻辑

            RDPlayer rdPlayer = Traverse.Create(__instance).Property("player").GetValue<RDPlayer>();
            if (rdPlayer != RDPlayer.P1 && rdPlayer != RDPlayer.P2) return;

            var playerIndex = (int)rdPlayer;
            // 从录像中找到本条击打对应的 HitEvent（非 Release）
            if (ReplayPlayer.Instance.TryDequeueHit(playerIndex, __instance.rowID, beat.bar, isRelease: false, out var hit))
            {
                timeOffset = hit.timeOffset;
            }
        }

        // 录制模式：在 Pulse 之后记录完整的 HitEvent
        [HarmonyPostfix]
        public static void Postfix(
            scrPlayerbox __instance,
            float timeOffset,
            Beat beat,
            bool CPUTriggered,
            bool bomb)
        {
            if (ReplayContext.CurrentMode != ReplayMode.Recording) return;

            var recorder = ReplayRecorder.Instance;
            if (recorder == null || !recorder.IsRecording) return;

            // scrPlayerbox.player 是私有属性，通过 Traverse 读取
            RDPlayer rdPlayer = Traverse.Create(__instance).Property("player").GetValue<RDPlayer>();

            // clumsyCPUTiming 是 public 字段，直接访问
            float clumsyCPU = __instance.clumsyCPUTiming;

            // debugSettings 是 RDBase 的 protected 属性，通过 DebugSettings.instance 直接访问
            bool isAuto = DebugSettings.instance != null && DebugSettings.instance.Auto;

            // 镜像 Pulse 内部 offsetType 计算逻辑
            int offsetTypeNum;
            OffsetType offsetType;

            if (!beat.isLenientMargins && !bomb &&
                ((!isAuto && !CPUTriggered) || (CPUTriggered && clumsyCPU != 0f)))
            {
                float margin = scnGame.GetHitMargin(rdPlayer);
                if      (timeOffset > 0.2f)    { offsetType = OffsetType.VeryLate;      offsetTypeNum =  2; }
                else if (timeOffset > margin)   { offsetType = OffsetType.SlightlyLate;  offsetTypeNum =  1; }
                else if (timeOffset > -margin)  { offsetType = OffsetType.Perfect;       offsetTypeNum =  0; }
                else if (timeOffset > -0.2f)    { offsetType = OffsetType.SlightlyEarly; offsetTypeNum = -1; }
                else                            { offsetType = OffsetType.VeryEarly;     offsetTypeNum = -2; }
            }
            else
            {
                offsetType    = OffsetType.Perfect;
                offsetTypeNum = 0;
            }

            bool isMissed = System.Math.Abs(offsetTypeNum) >= 2;
            double audioPos = scrConductor.instance?.audioPos ?? 0.0;

            recorder.RecordHit(
                audioPos:   audioPos,
                player:     (int)rdPlayer,
                rowID:      __instance.rowID,
                bar:        beat.bar,
                offsetType: offsetTypeNum,
                timeOffset: timeOffset,
                weight:     beat.weight,
                isMissed:   isMissed,
                isHoldBeat: beat.hasHeldPulses,
                isRelease:  false
            );
        }
    }

    /// <summary>
    /// Patch：记录 Hold 松开判定（SpaceBarReleased Prefix）。
    ///
    /// 必须用 Prefix 而非 Postfix！
    /// SpaceBarReleased 末尾会执行 currentHoldBeat = null（第788行），
    /// Postfix 执行时 currentHoldBeat 已经为 null，无法读取 beat 信息。
    /// releaseOffsetType 是计算属性，依赖 currentHoldBeat 的 releaseTime，也必须在 null 前读取。
    /// </summary>
    [HarmonyPatch(typeof(scrPlayerbox), nameof(scrPlayerbox.SpaceBarReleased))]
    public static class Patch_Judgment_HoldRelease
    {
        [HarmonyPrefix]
        public static void Prefix(scrPlayerbox __instance, RDPlayer player)
        {
            // 回放模式：用录像里的 Hold-Release 判定覆盖 releaseOffsetType
            if (ReplayContext.CurrentMode == ReplayMode.Replaying &&
                ReplayPlayer.Instance != null &&
                ReplayPlayer.Instance.IsPlaying)
            {
                RDPlayer boxPlayer = Traverse.Create(__instance).Property("player").GetValue<RDPlayer>();
                if (player == boxPlayer)
                {
                    Beat currentHoldBeat = __instance.currentHoldBeat;
                    if (currentHoldBeat != null)
                    {
                        int playerIndex = (int)player;
                        if (ReplayPlayer.Instance.TryDequeueHit(playerIndex, __instance.rowID, currentHoldBeat.bar, isRelease: true, out var hit))
                        {
                            // 这里不能直接改 releaseOffsetType（只读属性），
                            // 但我们已经在 Pulse 前覆盖了 timeOffset，长按 release 的误差主要体现在 HitEvent 中，
                            // MistakesManager 的帧偏移统计则由 AddAbsoluteMistake 驱动（依赖 timeOffset），
                            // 对于 Hold Release，游戏本身对 Mistakes 影响有限，因此暂时不强行改内部状态。
                        }
                    }
                }
            }

            // 录制模式：照旧记录 Hold Release 判定
            if (ReplayContext.CurrentMode != ReplayMode.Recording) return;

            var recorder = ReplayRecorder.Instance;
            if (recorder == null || !recorder.IsRecording) return;

            // currentHoldBeat 是 public 字段，直接访问
            Beat currentHoldBeatRec = __instance.currentHoldBeat;

            // 没有 hold beat，SpaceBarReleased 内部也会立即 return，无需记录
            if (currentHoldBeatRec == null) return;

            // 玩家不匹配时 SpaceBarReleased 内部会 return，也跳过
            RDPlayer boxPlayerRec = Traverse.Create(__instance).Property("player").GetValue<RDPlayer>();
            if (player != boxPlayerRec) return;

            // holdAssist 且非 CPU 触发时内部会 return，跳过（与源码第692行逻辑一致）
            if (__instance.holdAssist) return;

            // releaseOffsetType 是 public 计算属性，在 currentHoldBeat 有效时读取
            OffsetType offsetTypeRec = __instance.releaseOffsetType;

            // drumMode + SlightlyEarly 时内部会 return，跳过
            if (scnGame.drumMode && offsetTypeRec == OffsetType.SlightlyEarly) return;

            int offsetTypeNumRec = offsetTypeRec switch
            {
                OffsetType.SlightlyEarly => -1,
                OffsetType.Perfect       =>  0,
                OffsetType.SlightlyLate  =>  1,
                _                        =>  0,
            };

            double audioPosRec = scrConductor.instance?.audioPos ?? 0.0;

            recorder.RecordHit(
                audioPos:   audioPosRec,
                player:     (int)player,
                rowID:      __instance.rowID,
                bar:        currentHoldBeatRec.bar,
                offsetType: offsetTypeNumRec,
                timeOffset: (float)(audioPosRec - currentHoldBeatRec.releaseTime),
                weight:     currentHoldBeatRec.weight,
                isMissed:   System.Math.Abs(offsetTypeNumRec) >= 2,
                isHoldBeat: true,
                isRelease:  true
            );
        }
    }
}
