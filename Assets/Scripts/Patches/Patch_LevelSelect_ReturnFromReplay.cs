using HarmonyLib;
using RDReplay.Patches;

/// <summary>
/// 修正从回放场景返回关卡选择时的相机 / 选择状态：
/// - 让 LevelSelect 认为我们是从 Ian 桌面返回，从而把相机放在 BasementComputer；
/// - 垂直选项默认选中我们追加的 ReplayManager（第三项）。
/// </summary>
[HarmonyPatch(typeof(scnLevelSelect), "Start")]
public static class Patch_LevelSelect_ReturnFromReplay
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!ReplayContext.ReturnToBasementComputer)
            return;

        // 不在这里清除标记，留给 ShowRanksText 的 Patch 使用后再清除

        // 伪造 lastSceneName，让 Update 第一帧把 cameFromBasementComputer 设为 true，
        // 相机会用 GetBasementComputerId() 作为起始位置。
        scnBase.lastSceneName = "scnIanDesktop";
    }
}

