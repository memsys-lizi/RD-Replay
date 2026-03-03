using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using RDReplay.Patches;
using RDReplay.UI;

[BepInPlugin("com.rdreplay.mod", "RD-Replay", "0.1.0")]
public class ModEntry : BaseUnityPlugin
{
    public static ModEntry Instance { get; private set; }

    /// <summary>公开日志实例，供 Plugin.Log 静态属性访问</summary>
    public ManualLogSource Log => Logger;

    public string ModPath => Path.GetDirectoryName(
        System.Reflection.Assembly.GetExecutingAssembly().Location);

    private AssetBundle _scenesBundle;
    private AssetBundle _resourcesBundle;
    private Harmony     _harmony;

    private void Start()
    {
        Instance = this;

        // 关闭 DOTween 的调试日志，避免 "This Tween has been killed" 等警告
        try
        {
            DG.Tweening.DOTween.debugMode = false;
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[ModEntry] Failed to disable DOTween debug mode: {ex.Message}");
        }

        // 加载 AssetBundle
        string scenesPath    = Path.Combine(ModPath, "scenes.assets");
        string resourcesPath = Path.Combine(ModPath, "resources.assets");

        if (File.Exists(scenesPath))
            _scenesBundle = AssetBundle.LoadFromFile(scenesPath);
        else
            Logger.LogWarning($"[ModEntry] scenes.assets not found at {scenesPath}");

        if (File.Exists(resourcesPath))
            _resourcesBundle = AssetBundle.LoadFromFile(resourcesPath);
        else
            Logger.LogWarning($"[ModEntry] resources.assets not found at {resourcesPath}");

        // 挂载 SaveCoordinator（跨场景持久存活）
        var coordinatorGO = new GameObject("[RDReplay_SaveCoordinator]");
        coordinatorGO.AddComponent<SaveCoordinator>();

        // 尝试从 resources AssetBundle 实例化提示 UI 预制体（名称需与打包时保持一致）
        if (_resourcesBundle != null)
        {
            try
            {
                var toastPrefab = _resourcesBundle.LoadAsset<GameObject>("RDReplay_Toast");
                if (toastPrefab != null)
                {
                    Object.Instantiate(toastPrefab);
                }
                else
                {
                    Logger.LogWarning("[ModEntry] RDReplay_Toast prefab not found in resources bundle. Toast UI disabled.");
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"[ModEntry] Failed to load RDReplay_Toast prefab: {ex.Message}");
            }
        }
        else
        {
            Logger.LogWarning("[ModEntry] resources bundle not loaded. Toast UI disabled.");
        }

        // 应用所有 Harmony Patch
        _harmony = new Harmony("com.rdreplay.mod");
        _harmony.PatchAll();

        Logger.LogInfo("[RD-Replay] Mod loaded successfully.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        _scenesBundle?.Unload(false);
        _resourcesBundle?.Unload(false);
    }
}

/// <summary>
/// 全局日志访问点，供各模块统一使用，避免到处传 Logger 引用。
/// </summary>
public static class Plugin
{
    private static ManualLogSource _fallback;

    public static ManualLogSource Log
    {
        get
        {
            if (ModEntry.Instance != null) return ModEntry.Instance.Log;
            // ModEntry 未就绪时使用独立 source（仅启动早期阶段会触发）
            return _fallback ??= BepInEx.Logging.Logger.CreateLogSource("RD-Replay");
        }
    }
}
