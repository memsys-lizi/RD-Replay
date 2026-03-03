using System;
using System.Collections;
using HarmonyLib;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DG.Tweening;

namespace RDReplay.Patches
{
    /// <summary>
    /// 把“回放系统”挂到地下室那台电脑上：
    ///   - 在 BasementComputer 的垂直菜单里追加一个目标 ReplayManager
    ///   - 当选择这个目标时，跳转到 ScnReplay 场景
    /// 其余 sleevePaint / IanDesktop 行为保持原样。
    /// </summary>
    public static class Patch_LevelSelect
    {
        private const string BasementComputerAction = "BasementComputer";
        private const string ReplayTargetId         = "ReplayManager";

        // 记录当前这一帧 ShowRanksText 之前，是否“原来选中的是回放系统”
        private static bool _wasOnReplayOption;

        /// <summary>
        /// 在 ShowRanksText 运行前/后，对 BasementComputer 的垂直菜单做两件事：
        /// 1. Prefix：如果之前选中的是我们的第三项（index==2），先把 selectedVerticalIndex 临时夹到 0/1，防止原始代码用 2 访问只有 2 个元素的新列表导致越界；
        /// 2. Postfix：重新把 ReplayManager 选项插回列表末尾，如果之前在第三项，就把 index 设回 2。
        /// </summary>
        [HarmonyPatch(typeof(scnLevelSelect), "ShowRanksText")]
        public static class BasementComputerMenuPatch
        {
            [HarmonyPrefix]
            public static void Prefix(scnLevelSelect __instance, int index)
            {
                _wasOnReplayOption = false;

                if (!(__instance.selectableEntities[index] is SelectableObject so) ||
                    so.id != "BasementComputer")
                    return;

                var trav = Traverse.Create(__instance);
                int selIndex = trav.Field("selectedVerticalIndex").GetValue<int>();

                // 之前我们最多只会把菜单扩展到 3 项（0,1,2），2 代表“回放系统”
                if (selIndex == 2)
                {
                    _wasOnReplayOption = true;
                    // 原始 ShowRanksText 会重新创建一个只含 sleevePaint/IanDesktop 的 2 项列表，
                    // 这里先把索引夹到 1，确保原代码不会用 2 访问新列表导致越界。
                    trav.Field("selectedVerticalIndex").SetValue(1);
                }
            }

            [HarmonyPostfix]
            public static void Postfix(scnLevelSelect __instance, int index)
            {
                if (!(__instance.selectableEntities[index] is SelectableObject so) ||
                    so.id != "BasementComputer")
                    return;

                var trav    = Traverse.Create(__instance);
                var listObj = trav.Field("selectedVerticalDestinations").GetValue();
                if (listObj is not IList list || list.Count == 0)
                    return;

                // 已经有 ReplayManager 了就不重复加
                bool hasReplay = false;
                foreach (var item in list)
                {
                    var targetId = Traverse.Create(item).Field("targetObjectId").GetValue<string>();
                    if (targetId == ReplayTargetId)
                    {
                        hasReplay = true;
                        break;
                    }
                }

                if (!hasReplay)
                {
                    // 通过反射创建 LevelSelectDestination(\"ReplayManager\", \"回放系统\", \"查看并管理录像\")
                    var destType = AccessTools.Inner(typeof(scnLevelSelect), "LevelSelectDestination");
                    var ctor = AccessTools.Constructor(destType, new[]
                    {
                        typeof(string), typeof(string), typeof(string)
                    });
                    var dest = ctor.Invoke(new object[]
                    {
                        ReplayTargetId,
                        "回放系统",
                        "查看并管理录像"
                    });

                    list.Add(dest);
                }

                // 如果原来选的是第三项，追加之后把索引设回 2
                if (_wasOnReplayOption)
                {
                    trav.Field("selectedVerticalIndex").SetValue(2);
                }
                // 如果是从回放场景返回 LevelSelect，则强制默认选中我们的 ReplayManager 选项
                else if (ReplayContext.ReturnToBasementComputer)
                {
                    // ReplayManager 被追加在列表末尾（index = list.Count - 1）
                    trav.Field("selectedVerticalIndex").SetValue(list.Count - 1);
                    // 标记只用一次，并恢复 lastSceneName 避免退出时返回错误场景
                    ReplayContext.ReturnToBasementComputer = false;
                    // 恢复 lastSceneName 为空或默认值，避免游戏认为我们来自 Ian 桌面
                    scnBase.lastSceneName = "";
                }

                // 重新读取最新的 selectedVerticalIndex，避免上面修改后本地 selIndex 还停留在旧值
                int selIndex = trav.Field("selectedVerticalIndex").GetValue<int>();

                // 若当前垂直选中的是回放项，强制覆盖 description 文本
                if (selIndex >= 0 && selIndex < list.Count)
                {
                    var currentDest = list[selIndex];
                    string targetId = Traverse.Create(currentDest).Field("targetObjectId").GetValue<string>();
                    if (targetId == ReplayTargetId)
                    {
                        var desc = trav.Field("description").GetValue<Text>();
                        if (desc != null)
                        {
                            desc.text = "回放系统\n<color=#6AF2F0>查看并管理录像</color>";
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 在 PerformEntityAction 前拦截 BasementComputer：
        /// 若当前垂直菜单选的是 ReplayManager，就直接进 ScnReplay，
        /// 否则让原逻辑处理（袖子绘图 / Ian 桌面）。
        /// </summary>
        [HarmonyPatch(typeof(scnLevelSelect), "PerformEntityAction")]
        public static class BasementComputerActionPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(scnLevelSelect __instance)
            {
                if (!(__instance.selectedEntity is SelectableObject so) ||
                    so.action != BasementComputerAction)
                    return true; // 非 BasementComputer，交给原方法

                var trav = Traverse.Create(__instance);
                var listObj = trav.Field("selectedVerticalDestinations").GetValue();
                if (listObj is not IList list || list.Count == 0)
                    return true;

                int selIndex = trav.Field("selectedVerticalIndex").GetValue<int>();
                if (selIndex < 0 || selIndex >= list.Count)
                    return true;

                var currentDest = list[selIndex];
                string targetId = Traverse.Create(currentDest).Field("targetObjectId").GetValue<string>();

                if (targetId != ReplayTargetId)
                    return true; // 仍然是 sleevePaint / IanDesktop，走原 BasementComputer 逻辑

                try
                {
                    Plugin.Log.LogInfo("[Patch_LevelSelect] BasementComputer -> ScnReplay");

                    // 完全沿用原 BasementComputer 的过场方式：
                    // 先画面淡出 + 停止环境音，再由 scnBase.GoToScene 统一切场景和销毁音源
                    RDBase.Vfx.FadeOut();
                    __instance.StopAllAmbiences(0.5f);

                    DOVirtual.DelayedCall(0.5f, () =>
                    {
                        scnBase.GoToScene("ScnReplay");
                    });
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Patch_LevelSelect] Failed to load ScnReplay: {ex.Message}");
                }

                // 我们已经处理完这个分支，阻止原方法执行
                return false;
            }
        }
    }
}
