using System.Collections.Generic;
using UnityEngine;
using AudioSettings = UnityEngine.AudioSettings;
using RDReplay.Storage;

namespace RDReplay.Core
{
    /// <summary>
    /// 回放播放模块：持有当前回放数据，提供给 Patch 层按需查询下一条输入/Rand 值。
    /// 负责将 DSP 时间转换回绝对 audioPos 并与游戏 scrConductor.audioPos 对齐。
    /// </summary>
    public class ReplayPlayer
    {
        // ── 单例 ────────────────────────────────────────────────────
        public static ReplayPlayer Instance { get; private set; }

        public static void CreateInstance(ReplayData data)
        {
            Instance = new ReplayPlayer(data);
        }

        public static void DestroyInstance()
        {
            Instance = null;
        }

        // ── 状态 ────────────────────────────────────────────────────
        private readonly ReplayData _data;
        private int   _inputCursor;
        private int   _randCursor;
        private int   _logicalCursor;
        private int   _hitCursor;
        private bool  _isPlaying;
        private double _playbackStartDsp; // 本次回放开始时的 DSP 时间，用于把相对 offset 映射回当前运行的绝对时间

        private ReplayPlayer(ReplayData data)
        {
            _data = data;
        }

        // ── Public API ──────────────────────────────────────────────

        public ReplayData Data => _data;
        public bool IsPlaying => _isPlaying;

        /// <summary>本次回放的起始 DSP 时间，用于 UI 计算相对进度。</summary>
        public double PlaybackStartDsp => _playbackStartDsp;

        /// <summary>给定当前 scrConductor.audioPos，返回本次回放已经经过的相对时间（秒）。</summary>
        public double GetElapsed(double currentAudioPos) => currentAudioPos - _playbackStartDsp;

        /// <summary>
        /// 游戏进入 Handmode 时由 Patch_GameStart 调用。
        /// 此时游戏的 DSP 基准应与 _data.sessionStartDsp 对齐（通过 Patch_GameStart 强制写入）。
        /// </summary>
        public void Begin()
        {
            _inputCursor = 0;
            _randCursor  = 0;
            _logicalCursor = 0;
            _hitCursor   = 0;
            _isPlaying   = true;
            _playbackStartDsp = scrConductor.instance?.audioPos ?? AudioSettings.dspTime;
            Plugin.Log.LogInfo($"[Player] Started. inputs={_data.inputs.Count} rand={_data.randResults.Count}");
        }

        public void Stop()
        {
            _isPlaying = false;
        }

        // ── 输入查询 ────────────────────────────────────────────────

        /// <summary>
        /// 每帧调用（来自 Patch_Update），收集当前帧应注入的输入事件列表。
        /// currentAudioPos 即 scrConductor.audioPos（绝对 DSP 时间）。
        /// </summary>
        public List<InputEvent> CollectDueInputs(double currentAudioPos)
        {
            var due = new List<InputEvent>();
            // 将当前运行的绝对时间换算成本次回放的「相对起点偏移」
            double elapsed = currentAudioPos - _playbackStartDsp;

            while (_inputCursor < _data.inputs.Count &&
                   _data.inputs[_inputCursor].audioPos <= elapsed)
            {
                due.Add(_data.inputs[_inputCursor]);
                _inputCursor++;
            }
            return due;
        }

        /// <summary>
        /// 是否还有待注入的输入事件
        /// </summary>
        public bool HasPendingInputs => _inputCursor < _data.inputs.Count;

        // ── Rand 查询 ───────────────────────────────────────────────

        /// <summary>
        /// 当 EvalStringWithVariables 调用 Rand(N) 时，由 Patch_RandEval 调用此方法取得确定性结果。
        /// 若序列已耗尽（谱面变动导致调用次数增加），返回 -1 并记录警告。
        /// </summary>
        public int NextRand()
        {
            if (_randCursor >= _data.randResults.Count)
            {
                Plugin.Log.LogWarning($"[Player] Rand sequence exhausted at cursor={_randCursor}! Returning 0.");
                return 0;
            }
            return _data.randResults[_randCursor++];
        }

        public bool HasPendingRand => _randCursor < _data.randResults.Count;

        // ── 逻辑按键查询（RDInput 层 Left/Right/Up/Down/Cancel/...）───────

        public List<LogicalInputEvent> CollectDueLogicalInputs(double currentAudioPos)
        {
            var due = new List<LogicalInputEvent>();
            if (_data.logicalInputs == null || _data.logicalInputs.Count == 0) return due;

            double elapsed = currentAudioPos - _playbackStartDsp;

            while (_logicalCursor < _data.logicalInputs.Count &&
                   _data.logicalInputs[_logicalCursor].audioPos <= elapsed)
            {
                due.Add(_data.logicalInputs[_logicalCursor]);
                _logicalCursor++;
            }

            return due;
        }

        // ── 判定结果重放（用于修补 Pulse 判定） ────────────────────────

        /// <summary>
        /// 在回放模式下，每次 scrPlayerbox.Pulse / SpaceBarReleased 被调用时，按顺序匹配下一条 HitEvent。
        /// 只匹配给定玩家、轨道、小节和是否为 Release 的条目。
        /// </summary>
        public bool TryDequeueHit(int player, int rowID, int bar, bool isRelease, out HitEvent hit)
        {
            hit = null;
            if (_data.hits == null || _data.hits.Count == 0) return false;

            while (_hitCursor < _data.hits.Count)
            {
                var h = _data.hits[_hitCursor];

                if (h.player == player &&
                    h.rowID == rowID &&
                    h.bar == bar &&
                    h.isRelease == isRelease)
                {
                    hit = h;
                    _hitCursor++;
                    return true;
                }

                _hitCursor++;
            }

            return false;
        }
    }
}
