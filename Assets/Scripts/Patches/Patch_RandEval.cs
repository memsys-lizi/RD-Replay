using HarmonyLib;
using UnityEngine;
using RDReplay.Core;

namespace RDReplay.Patches
{
    /// <summary>
    /// Patch：拦截谱面内的 Rand(N) 随机调用（LevelBase.EvalStringWithVariables）。
    ///
    /// 录制模式：允许 UnityEngine.Random.Range 正常执行，记录结果。
    ///           使用 Postfix 在方法返回后拿到最终 int 结果并存入 randResults 序列。
    ///
    /// 回放模式：使用 Prefix 从 ReplayPlayer 取出预存结果并直接返回，
    ///           跳过 Random.Range 调用（通过 __result 赋值 + return false 实现）。
    ///
    /// 注意：EvalStringWithVariables 返回 object，Rand 分支时实际上已在方法内强转为 int。
    ///       Postfix 通过检查 hasRandWrapper 字段来判断是否有随机数分支。
    ///       由于 hasRandWrapper 是 out 参数（局部变量），无法直接从 Postfix 获取，
    ///       改为在 Prefix 阶段通过 Transpiler 注入状态标志。
    ///       简化方案：Prefix 调用 ReplaceVarsWithValues 探测 hasRandWrapper，再决定是否替换结果。
    /// </summary>
    [HarmonyPatch(typeof(LevelBase), nameof(LevelBase.EvalStringWithVariables))]
    public static class Patch_RandEval
    {
        // Prefix：回放模式下替换随机结果
        [HarmonyPrefix]
        public static bool Prefix(LevelBase __instance, string str, ref object __result)
        {
            if (ReplayContext.CurrentMode != ReplayMode.Replaying) return true;

            var player = ReplayPlayer.Instance;
            if (player == null || !player.IsPlaying) return true;

            // 检查该表达式是否含有 Rand() 包裹
            bool hasRand = str.TrimStart().StartsWith("Rand(");
            if (!hasRand) return true;

            // 先正常计算 Rand(N) 内部表达式的值（即 N），然后取序列中的预存结果
            // 为简洁，直接调用 EvalString 计算 N
            string inner = str.Trim();
            inner = inner.Substring(5);                   // 去掉 "Rand("
            inner = inner.TrimEnd(')', ' ');               // 去掉 ")"
            // 替换变量
            var varCache = new System.Collections.Generic.Dictionary<string, string>();
            bool dummy;
            string innerEval = __instance.ReplaceVarsWithValues(inner, varCache, out dummy);

            // 取序列预存值（忽略 N，完全使用录制时的结果）
            int saved = player.NextRand();
            __result = (object)saved;

            Plugin.Log.LogDebug($"[Patch_RandEval] Replay: Rand({innerEval}) -> {saved} (from sequence)");
            return false; // 跳过原方法
        }

        // Postfix：录制模式下记录随机结果
        // 注意：即使 Prefix 返回 false 跳过原方法，Postfix 仍然执行（Harmony 行为）。
        // CurrentMode != Recording 的检查已足够拦截回放模式，无需额外处理。
        [HarmonyPostfix]
        public static void Postfix(string str, object __result)
        {
            // 回放模式下 Prefix 已经处理完毕，这里什么都不做
            if (ReplayContext.CurrentMode != ReplayMode.Recording) return;

            var recorder = ReplayRecorder.Instance;
            if (recorder == null || !recorder.IsRecording) return;

            bool hasRand = str.TrimStart().StartsWith("Rand(");
            if (!hasRand) return;

            if (__result is int randValue)
            {
                recorder.RecordRand(randValue);
                Plugin.Log.LogDebug($"[Patch_RandEval] Recorded Rand result: {randValue}");
            }
        }
    }
}
