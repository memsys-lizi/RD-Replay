using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using AudioSettings = UnityEngine.AudioSettings;
using RDReplay.Core;

namespace RDReplay.Patches
{
    /// <summary>
    /// Patch：每帧处理录制/回放逻辑。
    ///
    /// 录制模式：
    ///   在 scnGame 进入 Handmode 的首帧触发 Recorder.Begin()（延迟初始化）。
    ///
    /// 回放模式：
    ///   收集当前帧应注入的 InputEvent，调用 scnGame.UpdateGameplayInput 注入。
    ///   通过 ReplayContext.IsReplayInjecting 标志告知 Patch_Input 这是注入调用，不要再次记录。
    /// </summary>
    [HarmonyPatch(typeof(scnGame), "Update")]
    public static class Patch_Update
    {
        [HarmonyPostfix]
        public static void Postfix(scnGame __instance)
        {
            HandleRecordingInit(__instance);
            HandleReplayInput(__instance);
        }

        // ── 录制初始化 ───────────────────────────────────────────────

        private static void HandleRecordingInit(scnGame game)
        {
            if (ReplayContext.RecorderBegan) return;

            // 录制模式：等离开 PreStart（包括 SpacePressedPreStart / Handmode / Cutscene）后触发 Begin()
            if (ReplayContext.CurrentMode == ReplayMode.Recording)
            {
                if (ReplayContext.PendingGameInstance == null) return;
                // GameState 枚举：PreStart(0) SpacePressedPreStart(1) HandmodePreCutscene(2) Handmode(3) Cutscene(4)...
                if (game.gameState == GameState.PreStart) return;

                ReplayContext.RecorderBegan = true;
                // 继续往下走，执行 Begin()
            }
            // 回放模式：离开 PreStart 后触发 ReplayPlayer.Begin()
            else if (ReplayContext.CurrentMode == ReplayMode.Replaying)
            {
                if (ReplayPlayer.Instance == null) return;
                if (game.gameState == GameState.PreStart) return;

                ReplayContext.RecorderBegan = true;
                ReplayPlayer.Instance.Begin();
                Plugin.Log.LogInfo($"[Patch_Update] ReplayPlayer.Begin() at state={game.gameState}");
                return;
            }
            else return;

            double dspBase = scrConductor.instance?.audioPos ?? AudioSettings.dspTime;

            // 收集关卡元数据
            // RDLevelSettings 是 struct（值类型），不能用 ?.
            var lvl    = game.currentLevel;
            string levelId     = game.levelIdentifier ?? "";
            string levelName   = (lvl?.data != null) ? lvl.data.settings.song   ?? levelId : levelId;
            string levelAuthor = (lvl?.data != null) ? lvl.data.settings.author ?? ""      : "";
            bool isCustom      = !string.IsNullOrEmpty(GetLevelPath(lvl));
            float speed        = ReplayContext.PendingLevelSpeed;
            bool twoPlayer     = GC.twoPlayerMode;
            string p1Skin      = ""; // TODO: 接入皮肤系统
            string p2Skin      = "";

            // 记录当下判定难度对应的基础 hitMargin（剥离掉关卡的 hitMarginMultiplier）
            float marginP1 = scnGame.GetHitMargin(RDPlayer.P1);
            float marginP2 = scnGame.GetHitMargin(RDPlayer.P2);
            float mult     = 1f;
            if (lvl != null && lvl.hitMarginMultiplier > 0f)
                mult = lvl.hitMarginMultiplier;
            float baseHitMarginP1 = marginP1 / mult;
            float baseHitMarginP2 = marginP2 / mult;

            ReplayRecorder.Instance?.Begin(
                levelId, levelName, levelAuthor,
                isCustom, speed, twoPlayer,
                p1Skin, p2Skin, dspBase,
                baseHitMarginP1, baseHitMarginP2);

            // 对于所有关卡（内置 / 自制），都在游戏开始后尽早截一张图，
            // 作为预览图优先源，避免只截到黑屏或结果界面。
            SaveCoordinator.Instance?.StartCoroutine(
                SaveCoordinator.CaptureMidRunScreenshotCoroutine());

            Plugin.Log.LogInfo($"[Patch_Update] RecorderBegan at dspBase={dspBase:F6}, state={game.gameState}");
        }

        // ── 回放输入注入 ──────────────────────────────────────────

        private static void HandleReplayInput(scnGame game)
        {
            // 回放输入已经完全通过 Patch_RDInput(RDInput.Update) 来驱动
            // 时间跳转由游戏自然触发（通过注入的 p1IsPressed/p2IsPressed 让 skippingCutsceneElapsedTime 累积）
            // 我们只需要在 Patch_TimeJump 的 Postfix 中同步游标
        }

        // ── 内部工具 ─────────────────────────────────────────────────

        private static string GetLevelPath(LevelBase lvl)
        {
            if (lvl == null) return null;
            var f = typeof(LevelBase).GetField("levelPath",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            return f?.GetValue(lvl) as string;
        }
    }
}
