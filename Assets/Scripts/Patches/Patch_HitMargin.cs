using HarmonyLib;
using RDReplay.Core;

namespace RDReplay.Patches
{
    /// <summary>
    /// 回放模式下锁定判定难度：
    /// 无论玩家在设置里把 DefibMode 改成什么，都按录制时的 baseHitMarginP1/P2 来计算判定窗口。
    /// </summary>
    [HarmonyPatch(typeof(scnGame), nameof(scnGame.GetHitMargin))]
    public static class Patch_HitMargin
    {
        [HarmonyPrefix]
        public static bool Prefix(RDPlayer player, ref float __result)
        {
            if (ReplayContext.CurrentMode != ReplayMode.Replaying) return true;
            if (ReplayPlayer.Instance == null) return true;

            var data = ReplayPlayer.Instance.Data;
            if (data == null) return true;

            float baseMargin = player switch
            {
                RDPlayer.P1 => data.baseHitMarginP1,
                RDPlayer.P2 => data.baseHitMarginP2,
                _           => 0f,
            };
            if (baseMargin <= 0f) return true; // 录制时没填到，退回原逻辑

            // 回放时仍然尊重关卡自己的 hitMarginMultiplier（同一谱面，动态缩放保持一致）
            float mult = 1f;
            if (scnGame.instance != null &&
                scnGame.instance.currentLevel != null &&
                scnGame.instance.currentLevel.hitMarginMultiplier > 0f)
            {
                mult = scnGame.instance.currentLevel.hitMarginMultiplier;
            }

            __result = baseMargin * mult;
            return false; // 跳过原始 GetHitMargin，实现“锁难度”
        }
    }
}

