using System;
using System.Collections.Generic;
using UnityEngine;
using RDReplay.Storage;
using RDReplay.UI;

namespace RDReplay.Core
{
    /// <summary>
    /// 录制模块：在 ReplayMode.Recording 期间收集游戏事件，最终产出 ReplayData。
    /// 由 Patch 层调用，无需感知 Harmony 细节。
    /// </summary>
    public class ReplayRecorder
    {
        // ── 单例 ────────────────────────────────────────────────────
        public static ReplayRecorder Instance { get; private set; }

        public static void CreateInstance()
        {
            Instance = new ReplayRecorder();
        }

        public static void DestroyInstance()
        {
            Instance = null;
        }

        // ── 状态 ────────────────────────────────────────────────────
        private ReplayData _data;
        private bool _isRecording;

        // 用于关联最近一次 InputEvent 与其后产生的 HitEvent（回填 rowID）
        private InputEvent _lastInputP1;
        private InputEvent _lastInputP2;

        // ── Public API ──────────────────────────────────────────────

        /// <summary>
        /// 游戏进入 Handmode 时调用（对应 StartTheGame coroutine 完成初始化后）。
        /// 填写关卡元数据并记录基准 DSP 时间。
        /// 后续所有 InputEvent/HitEvent 的 audioPos 字段都存的是
        /// 「相对 sessionStartDsp 的偏移秒数」，而不是绝对 DSP 时间。
        /// </summary>
        public void Begin(
            string levelId,
            string levelName,
            string levelAuthor,
            bool isCustomLevel,
            float levelSpeed,
            bool twoPlayerMode,
            string p1Skin,
            string p2Skin,
            double sessionStartDsp,
            float baseHitMarginP1,
            float baseHitMarginP2)
        {
            _data = new ReplayData
            {
                gameVersion  = Application.version,
                levelId      = levelId,
                levelName    = levelName,
                levelAuthor  = levelAuthor,
                isCustomLevel = isCustomLevel,
                levelSpeed   = levelSpeed,
                twoPlayerMode = twoPlayerMode,
                p1Skin       = p1Skin,
                p2Skin       = p2Skin,
                sessionStartDsp = sessionStartDsp,
                baseHitMarginP1 = baseHitMarginP1,
                baseHitMarginP2 = baseHitMarginP2,
                recordedAt   = DateTime.UtcNow.ToString("O"),
            };
            _lastInputP1 = null;
            _lastInputP2 = null;
            _isRecording = true;

            Plugin.Log.LogInfo($"[Recorder] Started. levelId={levelId} dspBase={sessionStartDsp:F6}");

            // 提示开始录制
            ReplayToastUI.ShowRecordingStarted();
        }

        public bool IsRecording => _isRecording;

        /// <summary>记录一次按键或松开事件（audioPos 为绝对 DSP 时间）</summary>
        public void RecordInput(double audioPos, int player, string action)
        {
            if (!_isRecording) return;

            var ev = new InputEvent
            {
                // 存相对时间，避免不同运行之间 DSP 起点微小差异导致重放偏移
                audioPos = audioPos - _data.sessionStartDsp,
                player   = player,
                action   = action,
            };

            _data.inputs.Add(ev);

            // 暂存引用，等待 Pulse/SpaceBarReleased 回填 rowID
            if (player == 0) _lastInputP1 = ev;
            else             _lastInputP2 = ev;
        }

        /// <summary>记录一次判定结果（由 Pulse / SpaceBarReleased 调用，audioPos 为绝对 DSP 时间）</summary>
        public void RecordHit(
            double audioPos,
            int player,
            int rowID,
            int bar,
            int offsetType,
            float timeOffset,
            float weight,
            bool isMissed,
            bool isHoldBeat,
            bool isRelease)
        {
            if (!_isRecording) return;

            var ev = new HitEvent
            {
                audioPos   = audioPos - _data.sessionStartDsp,
                player     = player,
                rowID      = rowID,
                bar        = bar,
                offsetType = offsetType,
                timeOffset = timeOffset,
                weight     = weight,
                isMissed   = isMissed,
                isHoldBeat = isHoldBeat,
                isRelease  = isRelease,
            };

            _data.hits.Add(ev);

            // 回填最近一次同玩家 InputEvent 的 rowID
            var lastInput = player == 0 ? _lastInputP1 : _lastInputP2;
            if (lastInput != null && lastInput.rowID == -1)
            {
                lastInput.rowID = rowID;
            }
        }

        /// <summary>记录 Rand(N) 调用结果（由 EvalStringWithVariables Prefix 调用）</summary>
        public void RecordRand(int result)
        {
            if (!_isRecording) return;
            _data.randResults.Add(result);
        }

        /// <summary>记录一次 RDInput 层的逻辑按键事件（Left/Right/Up/Down/Cancel/Skip/Restart/Quit）。</summary>
        public void RecordLogical(double audioPos, LogicalInputType type, string phase)
        {
            if (!_isRecording) return;

            var ev = new LogicalInputEvent
            {
                audioPos = audioPos - _data.sessionStartDsp,
                type     = type,
                phase    = phase,
            };

            _data.logicalInputs.Add(ev);
        }

        /// <summary>记录时间轴跳转事件（Cutscene Skip、Checkpoint 等导致的 ScrubToBarNum 调用）。</summary>
        public void RecordTimeJump(double audioPosBefore, double audioPosAfter, int fromBar, int toBar, string reason)
        {
            if (!_isRecording) return;

            var ev = new TimeJumpEvent
            {
                audioPosBefore = audioPosBefore - _data.sessionStartDsp,
                audioPosAfter  = audioPosAfter - _data.sessionStartDsp,
                fromBar        = fromBar,
                toBar          = toBar,
                reason         = reason,
            };

            _data.timeJumps.Add(ev);
            Plugin.Log.LogInfo($"[Recorder] TimeJump: bar {fromBar} -> {toBar}, reason={reason}");
        }

        /// <summary>记录游戏状态变化事件（GameState 切换）。</summary>
        public void RecordGameState(double audioPos, int fromState, int toState, int barNumber)
        {
            if (!_isRecording) return;

            var ev = new GameStateEvent
            {
                audioPos  = audioPos - _data.sessionStartDsp,
                fromState = fromState,
                toState   = toState,
                barNumber = barNumber,
            };

            _data.gameStates.Add(ev);
        }

        /// <summary>记录 Miss 判定事件（玩家未按，拍子自动 Miss）。</summary>
        public void RecordMiss(double audioPos, int player, int rowID, int bar, double beatInputTime, float weight, bool isHoldBeat)
        {
            if (!_isRecording) return;

            var ev = new MissEvent
            {
                audioPos      = audioPos - _data.sessionStartDsp,
                player        = player,
                rowID         = rowID,
                bar           = bar,
                beatInputTime = beatInputTime - _data.sessionStartDsp,
                weight        = weight,
                isHoldBeat    = isHoldBeat,
            };

            _data.misses.Add(ev);
        }

        /// <summary>
        /// 游戏结束时调用，写入成绩并停止录制。
        /// 不负责保存文件，保存操作由外部（Patch_EndLevel 监听 Ctrl+R）触发。
        /// </summary>
        public ReplayData Finish(ReplayResult result)
        {
            if (!_isRecording) return null;

            _isRecording = false;

            // 计算本局时长（最后一个事件距基准的偏移）
            double lastOffset = 0.0;
            if (_data.inputs.Count > 0)
                lastOffset = Math.Max(lastOffset, _data.inputs[_data.inputs.Count - 1].audioPos);
            if (_data.hits.Count > 0)
                lastOffset = Math.Max(lastOffset, _data.hits[_data.hits.Count - 1].audioPos);

            _data.duration = (float)lastOffset;
            _data.result   = result;

            Plugin.Log.LogInfo($"[Recorder] Finished. inputs={_data.inputs.Count} hits={_data.hits.Count} rand={_data.randResults.Count} duration={_data.duration:F2}s");

            return _data;
        }

        /// <summary>
        /// 放弃当前录制（如玩家中途退出）
        /// </summary>
        public void Abort()
        {
            _isRecording = false;
            _data = null;
            _lastInputP1 = null;
            _lastInputP2 = null;
            Plugin.Log.LogInfo("[Recorder] Aborted.");
        }
    }
}
