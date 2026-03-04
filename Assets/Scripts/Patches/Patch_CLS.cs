using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace RDReplay.Patches
{
    /// <summary>
    /// Patch scnCLS 添加新的 Replays Ward 选项，点击后跳转到回放场景。
    /// </summary>
    public static class Patch_CLS
    {
        // 用一个特殊的 WardOptionName 值表示回放模式（超出枚举范围）
        private const int REPLAY_WARD_OPTION = 999;

        /// <summary>
        /// Patch Start 在 Ward 选项列表中添加 Replays 选项。
        /// </summary>
        [HarmonyPatch(typeof(scnCLS), "Start")]
        public static class StartPatch
        {
            [HarmonyPostfix]
            public static void Postfix(scnCLS __instance)
            {
                // 延迟一帧执行，让 Layout 先完成初始化
                __instance.StartCoroutine(AddReplayOptionDelayed(__instance));
            }

            private static System.Collections.IEnumerator AddReplayOptionDelayed(scnCLS __instance)
            {
                // 等待场景完全加载完毕
                yield return new WaitForSeconds(1f);

                var wardOptions = __instance.wardOptions;
                if (wardOptions == null || wardOptions.Count == 0)
                {
                    Plugin.Log.LogWarning("[Patch_CLS] wardOptions is null or empty.");
                    yield break;
                }

                // 找到 Exit 选项作为参考
                scnCLS.WardOption exitOption = null;
                foreach (var opt in wardOptions)
                {
                    if (opt.name == scnCLS.WardOptionName.Exit)
                    {
                        exitOption = opt;
                        break;
                    }
                }

                if (exitOption == null)
                {
                    Plugin.Log.LogWarning("[Patch_CLS] Failed to find Exit option.");
                    yield break;
                }

                // 找到 Library 选项作为模板
                scnCLS.WardOption libraryOption = null;
                foreach (var opt in wardOptions)
                {
                    if (opt.name == scnCLS.WardOptionName.Library)
                    {
                        libraryOption = opt;
                        break;
                    }
                }

                if (libraryOption == null)
                {
                    Plugin.Log.LogWarning("[Patch_CLS] Failed to find Library option.");
                    yield break;
                }

                Plugin.Log.LogInfo($"[Patch_CLS] Exit position: {exitOption.rect.anchoredPosition}");

                // 完全深度复制 Library 的所有 GameObject
                var replayRect = UnityEngine.Object.Instantiate(libraryOption.rect.gameObject, libraryOption.rect.parent).GetComponent<RectTransform>();
                var replaySilhouette = UnityEngine.Object.Instantiate(libraryOption.signSilhouette, libraryOption.signSilhouette.transform.parent);
                var replayDetailContainer = UnityEngine.Object.Instantiate(libraryOption.detailContainer, libraryOption.detailContainer.transform.parent);

                // 重命名
                replayRect.name = "WardOption_Replays";
                replaySilhouette.name = "Silhouette_Replays";
                replayDetailContainer.name = "DetailContainer_Replays";

                // 删除复制的 rect 中的箭头容器（如果有的话）
                var arrowsInCopy = replayRect.Find("WardArrowsContainer");
                if (arrowsInCopy != null)
                {
                    UnityEngine.Object.Destroy(arrowsInCopy.gameObject);
                    Plugin.Log.LogInfo("[Patch_CLS] Removed duplicate arrows from replicated rect.");
                }

                // 手动设置位置：放在 Exit 下方
                var pos = exitOption.rect.anchoredPosition;
                pos.y -= 100f; // 向下偏移 100 单位
                replayRect.anchoredPosition = pos;

                Plugin.Log.LogInfo($"[Patch_CLS] Set Replay position to: {replayRect.anchoredPosition}");

                // 创建新的 WardOption（使用空字符串作为 token，我们会在 Patch 中直接设置文本）
                var replayOption = new scnCLS.WardOption
                {
                    name = (scnCLS.WardOptionName)REPLAY_WARD_OPTION,
                    rect = replayRect,
                    signSilhouette = replaySilhouette,
                    detailContainer = replayDetailContainer,
                    detailTitleToken = "",
                    detailDescriptionToken = "",
                    DefaultSprite = libraryOption.DefaultSprite,
                    introAnimData = libraryOption.introAnimData,
                    idleAnimData = libraryOption.idleAnimData
                };

                // 从复制的 GameObject 中重新获取所有组件引用
                replayOption.spriteAnimation = replayRect.GetComponent<SpriteAnimation>();

                var allImages = replayRect.GetComponentsInChildren<Image>(true);
                if (allImages.Length > 0)
                {
                    replayOption.signImage = allImages[0];
                    replayOption.signBaseImage = allImages[0];
                }

                var flashTransform = replayRect.Find("Flash");
                if (flashTransform != null)
                {
                    replayOption.signFlashImage = flashTransform.GetComponent<Image>();
                }
                else if (allImages.Length > 1)
                {
                    replayOption.signFlashImage = allImages[1];
                }

                // 修改显示文本
                var textComponents = replayRect.GetComponentsInChildren<Text>(true);
                foreach (var text in textComponents)
                {
                    text.text = "回放库";
                }

                // 修改 DetailContainer 中的文本
                var detailTexts = replayDetailContainer.GetComponentsInChildren<Text>(true);
                foreach (var text in detailTexts)
                {
                    if (text.name.Contains("Title"))
                        text.text = "回放库";
                    else if (text.name.Contains("Description"))
                        text.text = "查看并播放已保存的回放录像";
                }

                // 初始化为隐藏状态
                replayOption.Hide();

                // 插入到列表末尾（Exit 后面）
                wardOptions.Add(replayOption);

                Plugin.Log.LogInfo($"[Patch_CLS] Added Replays ward option at end of list.");
            }
        }

        /// <summary>
        /// Patch CurrentWardOptionIndex setter 来直接设置回放选项的文本，避免本地化查找。
        /// </summary>
        [HarmonyPatch(typeof(scnCLS), "CurrentWardOptionIndex", MethodType.Setter)]
        public static class CurrentWardOptionIndexPatch
        {
            [HarmonyPostfix]
            public static void Postfix(scnCLS __instance)
            {
                var trav = Traverse.Create(__instance);
                var currentOption = trav.Property("CurrentWardOption").GetValue<scnCLS.WardOption>();

                if (currentOption != null && (int)currentOption.name == REPLAY_WARD_OPTION)
                {
                    // 直接设置文本，覆盖 RDString.Get() 的结果
                    var wardDetailTitleText = trav.Field("wardDetailTitleText").GetValue<Text>();
                    var wardDetailTitleDescription = trav.Field("wardDetailTitleDescription").GetValue<Text>();

                    if (wardDetailTitleText != null)
                        wardDetailTitleText.text = "回放库";

                    if (wardDetailTitleDescription != null)
                        wardDetailTitleDescription.text = "查看并播放已保存的回放录像";
                }
            }
        }

        /// <summary>
        /// Patch SelectWardOption 处理 Replays 选项，直接跳转到回放场景。
        /// </summary>
        [HarmonyPatch(typeof(scnCLS), "SelectWardOption")]
        public static class SelectWardOptionPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(scnCLS __instance)
            {
                var trav = Traverse.Create(__instance);
                var currentOption = trav.Property("CurrentWardOption").GetValue<scnCLS.WardOption>();

                if (currentOption == null)
                    return true;

                // 检查是否是我们的回放选项
                if ((int)currentOption.name == REPLAY_WARD_OPTION)
                {
                    Plugin.Log.LogInfo("[Patch_CLS] Selected Replays option, jumping to ScnReplay.");

                    // 播放选中反馈动画
                    if (currentOption.signFlashImage != null)
                    {
                        currentOption.signFlashImage.color = Color.white;
                        currentOption.signFlashImage.DOKill();
                        currentOption.signFlashImage.DOFade(0f, 1f).SetUpdate(true).SetEase(Ease.OutQuint);
                    }

                    // 直接跳转到回放场景
                    scnBase.GoToScene("ScnReplay");

                    return false; // 跳过原始方法
                }

                return true;
            }
        }
    }
}
