using HarmonyLib;
using RDReplay.Core;
using UnityEngine;

namespace RDReplay.Patches
{
    /// <summary>
    /// Patch：记录和强制触发 Miss 判定事件。
    ///
    /// 录制模式：
    ///   Postfix 记录 Miss 事件（玩家未按，拍子自动 Miss）。
    ///
    /// 回放模式：
    ///   Prefix 检查录像中是否有对应的 Miss 事件，如果有则强制触发 Miss 并跳过原逻辑。
    ///   这样可以确保回放时的判定结果 100% 和录像一致，不受时间轴偏差影响。
    /// </summary>
    [HarmonyPatch(typeof(Beat), "Update")]
    public static class Patch_Miss
    {
        /// <summary>
        /// 回放模式：在 Beat.Update() 执行前检查是否应该强制触发 Miss。
        /// 如果录像里这一拍是 Miss，就提前触发 Miss 效果并跳过原逻辑。
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(Beat __instance)
        {
            // 只在回放模式下拦截
            if (ReplayContext.CurrentMode != ReplayMode.Replaying) return true;

            var player = ReplayPlayer.Instance;
            if (player == null || !player.IsPlaying) return true;

            // 检查这个 Beat 是否已经 dead（已经被处理过）
            if (__instance.dead) return true;

            // 获取 Beat 的信息
            var row = __instance.row;
            if (row == null || row.playerBox == null) return true;

            RDPlayer rdPlayer = row.GetCurrentPlayer();
            int playerIndex = (int)rdPlayer;
            int rowID = __instance.rowID;
            int bar = __instance.bar;
            double beatInputTime = __instance.inputTime;

            // 检查录像中这一拍是否应该 Miss
            bool shouldMiss = player.ShouldForceMiss(playerIndex, rowID, bar, beatInputTime);

            if (!shouldMiss)
            {
                // 录像里不是 Miss，继续正常逻辑（等待 Patch_Judgment_Pulse 覆盖判定结果）
                return true;
            }

            // ── 录像里是 Miss，强制触发 Miss ──

            // 检查是否到了应该 Miss 的时间（audioPos > inputTime + 0.4）
            double audioPos = scrConductor.instance?.audioPos ?? UnityEngine.AudioSettings.dspTime;
            bool isTimeToMiss = audioPos > beatInputTime + 0.4;

            if (!isTimeToMiss)
            {
                // 还没到 Miss 的时间，继续等待
                return true;
            }

            // 强制触发 Miss 效果（复制 Beat.Update() 第 229-291 行的逻辑）
            __instance.DestroyBeat();

            if (__instance.unhittable)
            {
                // unhittable 的拍子不触发 Miss 效果
                return false; // 跳过原逻辑
            }

            // 显示 Miss 提示
            if (LEDSign.showMarginError)
            {
                __instance.game?.statusText.SetStatusText(RDString.Get("status.wayTooLateFeedback"), null, 4f, narrate: true);
            }

            // 检查是否应该触发 Miss 效果（避免同一帧重复触发）
            if (Time.frameCount > row.lastMissedFrame)
            {
                // 触发角色表情和特效
                row.ent.ExpressionPlusFX("missed");

                // 添加 Miss 判定到统计
                __instance.game.AddHitOffset(rowID, OffsetType.Missed);

                // 扣血
                __instance.game.OnMistakeOrHeal(0.4f, __instance.weight, row);

                // 裂纹效果
                row.ent.CrackAdvance(__instance.weight);

                // 更新连续 Miss 统计
                __instance.game.mistakesManager.UpdateConsecutiveMistakes(rdPlayer, OffsetType.Missed);

                // 如果是 BeatOneshot，杀死 subdivBuddy
                if (__instance is BeatOneshot)
                {
                    ((BeatOneshot)__instance).KillSubdivBuddyWeights();
                }

                // 播放 Miss 音效
                bool isShadow = row.isShadow && row.shadowOrHostID != -1;
                if (!isShadow || row.isShadow || __instance.game.rows[row.shadowOrHostID].GetCurrentPlayer() != rdPlayer)
                {
                    string groupPath = "MistakesParent";
                    if (rdPlayer == RDPlayer.P1)
                    {
                        groupPath = "PlayerOneMistakes";
                    }
                    else if (rdPlayer == RDPlayer.P2)
                    {
                        groupPath = "PlayerTwoMistakes";
                    }

                    if (!__instance.dontPlayMistakeSound)
                    {
                        scrConductor.PlayFeedback(GameSoundType.BigMistake, 1.25f, RDUtils.GetMixerGroup(groupPath), 0.8f, rdPlayer);
                    }
                }

                // 边框闪烁反馈
                __instance.game.FlashBorderFeedback(correct: false, row);

                // 记录最后 Miss 的帧
                row.lastMissedFrame = Time.frameCount;
            }

            // 执行 OnMiss 事件
            foreach (scrExecuteOnHit item in RDExecuteOn.FindAll<scrExecuteOnHit>())
            {
                bool executed = false;
                if ((item.hitType == HitType.MissCompletely || item.hitType == HitType.AnyMiss) &&
                    item.isActive &&
                    (item.rowID == -1 || item.rowID == rowID))
                {
                    item.Execute(__instance);
                    executed = true;
                }

                if ((!item.persistent && executed) || item.autodelete)
                {
                    item.Remove();
                }
            }

            // 运行带 [onMiss] 标签的事件
            Beat.RunEventsTaggedOnMiss(rowID);

            // 调用关卡的 OnHit 回调
            __instance.game.currentLevel.OnHit(HitType.BigMiss, __instance);

            Plugin.Log.LogInfo($"[Patch_Miss] Forced Miss: player={playerIndex} rowID={rowID} bar={bar}");

            // 返回 false，跳过原 Beat.Update() 逻辑
            return false;
        }

        /// <summary>
        /// 录制模式：在 Beat.Update() 执行后检查是否触发了 Miss，如果是则记录。
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(Beat __instance)
        {
            if (ReplayContext.CurrentMode != ReplayMode.Recording) return;

            var recorder = ReplayRecorder.Instance;
            if (recorder == null || !recorder.IsRecording) return;

            // 检查是否刚刚触发了 Miss（通过检查 dead 标志和时间）
            if (!__instance.dead) return;

            // 检查是否是因为超时 Miss（audioPos > inputTime + 0.4）
            double audioPos = scrConductor.instance?.audioPos ?? UnityEngine.AudioSettings.dspTime;
            double inputTime = __instance.inputTime;

            // 如果当前时间超过 inputTime + 0.4，说明是 Miss
            if (audioPos <= inputTime + 0.4) return;

            // 检查是否是 unhittable 或 bomb（这些不算 Miss）
            if (__instance.unhittable || __instance.bomb) return;

            // 获取玩家信息
            var row = __instance.row;
            if (row == null || row.playerBox == null) return;

            RDPlayer rdPlayer = row.GetCurrentPlayer();
            int player = (int)rdPlayer;

            // 检查是否是 Auto 模式或 CPU 控制（这些不记录 Miss）
            bool isAuto = DebugSettings.instance != null && DebugSettings.instance.Auto;
            if (isAuto || row.cpuControlled) return;

            // 记录 Miss 事件
            recorder.RecordMiss(
                audioPos: audioPos,
                player: player,
                rowID: __instance.rowID,
                bar: __instance.bar,
                beatInputTime: inputTime,
                weight: __instance.weight,
                isHoldBeat: __instance.hasHeldPulses
            );
        }
    }
}
