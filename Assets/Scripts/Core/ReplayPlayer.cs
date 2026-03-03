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
        private int   _timeJumpCursor;
        private int   _gameStateCursor;
        private int   _missCursor;
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
            _timeJumpCursor = 0;
            _gameStateCursor = 0;
            _missCursor = 0;
            _isPlaying   = true;
            _playbackStartDsp = scrConductor.instance?.audioPos ?? AudioSettings.dspTime;
            Plugin.Log.LogInfo($"[Player] Started. inputs={_data.inputs.Count} rand={_data.randResults.Count} timeJumps={_data.timeJumps?.Count ?? 0}");
        }

        public void Stop()
        {
            _isPlaying = false;
        }

        /// <summary>
        /// 时间跳转后同步所有游标，跳过已经过去的事件。
        /// 在 ScrubToBarNum 执行后调用，确保后续事件查询从正确的位置开始。
        /// </summary>
        /// <param name="currentAudioPos">当前的 audioPos（绝对 DSP 时间）</param>
        public void SyncCursorsAfterTimeJump(double currentAudioPos)
        {
            // 检查是否有对应的 TimeJumpEvent
            if (_data.timeJumps == null || _timeJumpCursor >= _data.timeJumps.Count)
            {
                Plugin.Log.LogWarning($"[Player] SyncCursorsAfterTimeJump called but no TimeJumpEvent available!");
                return;
            }

            // 获取录制时的时间跳转数据
            var jumpEvent = _data.timeJumps[_timeJumpCursor];
            _timeJumpCursor++;

            Plugin.Log.LogInfo($"[Player] SyncCursorsAfterTimeJump:");
            Plugin.Log.LogInfo($"  Recorded jump: {jumpEvent.audioPosBefore:F3} -> {jumpEvent.audioPosAfter:F3} (bar {jumpEvent.fromBar} -> {jumpEvent.toBar})");
            Plugin.Log.LogInfo($"  currentAudioPos: {currentAudioPos:F3}");
            Plugin.Log.LogInfo($"  _playbackStartDsp (old): {_playbackStartDsp:F3}");

            // 计算时间偏移量：录制时跳过了多少时间
            double recordedTimeSkip = jumpEvent.audioPosAfter - jumpEvent.audioPosBefore;
            Plugin.Log.LogInfo($"  Recorded time skip: {recordedTimeSkip:F3}");

            // 同步输入事件游标到跳转后的位置
            int oldInputCursor = _inputCursor;
            while (_inputCursor < _data.inputs.Count &&
                   _data.inputs[_inputCursor].audioPos < jumpEvent.audioPosAfter)
            {
                _inputCursor++;
            }

            // 同步判定事件游标
            int oldHitCursor = _hitCursor;
            while (_hitCursor < _data.hits.Count &&
                   _data.hits[_hitCursor].audioPos < jumpEvent.audioPosAfter)
            {
                _hitCursor++;
            }

            // 同步逻辑输入游标
            if (_data.logicalInputs != null)
            {
                while (_logicalCursor < _data.logicalInputs.Count &&
                       _data.logicalInputs[_logicalCursor].audioPos < jumpEvent.audioPosAfter)
                {
                    _logicalCursor++;
                }
            }

            // 同步 Miss 事件游标
            if (_data.misses != null)
            {
                while (_missCursor < _data.misses.Count &&
                       _data.misses[_missCursor].audioPos < jumpEvent.audioPosAfter)
                {
                    _missCursor++;
                }
            }

            // 同步游戏状态游标
            if (_data.gameStates != null)
            {
                while (_gameStateCursor < _data.gameStates.Count &&
                       _data.gameStates[_gameStateCursor].audioPos < jumpEvent.audioPosAfter)
                {
                    _gameStateCursor++;
                }
            }

            Plugin.Log.LogInfo($"  Cursor changes: input {oldInputCursor}->{_inputCursor}, hit {oldHitCursor}->{_hitCursor}");

            // ⭐ 关键修复：调整 _playbackStartDsp，补偿录制时跳过的时间
            // 新的 _playbackStartDsp = currentAudioPos - jumpEvent.audioPosAfter
            double oldPlaybackStartDsp = _playbackStartDsp;
            _playbackStartDsp = currentAudioPos - jumpEvent.audioPosAfter;

            Plugin.Log.LogInfo($"  _playbackStartDsp (new): {_playbackStartDsp:F3} (delta: {_playbackStartDsp - oldPlaybackStartDsp:F3})");

            // 打印接下来几个事件的信息
            if (_inputCursor < _data.inputs.Count)
            {
                Plugin.Log.LogInfo($"  Next input event: audioPos={_data.inputs[_inputCursor].audioPos:F3}, player={_data.inputs[_inputCursor].player}, action={_data.inputs[_inputCursor].action}");
            }
            if (_hitCursor < _data.hits.Count)
            {
                Plugin.Log.LogInfo($"  Next hit event: audioPos={_data.hits[_hitCursor].audioPos:F3}, player={_data.hits[_hitCursor].player}, bar={_data.hits[_hitCursor].bar}");
            }
        }

        // ── 输入查询 ────────────────────────────────────────────────

        /// <summary>
        /// 每帧调用（来自 Patch_RDInput），收集当前帧应注入的输入事件列表。
        /// currentAudioPos 即 scrConductor.audioPos（绝对 DSP 时间）。
        /// </summary>
        public List<InputEvent> CollectDueInputs(double currentAudioPos)
        {
            var due = new List<InputEvent>();
            // 将当前运行的绝对时间换算成本次回放的「相对起点偏移」
            double elapsed = currentAudioPos - _playbackStartDsp;

            // 添加详细日志
            if (_inputCursor < _data.inputs.Count)
            {
                var nextInput = _data.inputs[_inputCursor];
                if (due.Count == 0 && System.Math.Abs(nextInput.audioPos - elapsed) < 1.0)
                {
                    Plugin.Log.LogInfo($"[Player] CollectDueInputs: elapsed={elapsed:F3}, nextInput.audioPos={nextInput.audioPos:F3}, diff={nextInput.audioPos - elapsed:F3}, cursor={_inputCursor}/{_data.inputs.Count}");
                }
            }

            while (_inputCursor < _data.inputs.Count &&
                   _data.inputs[_inputCursor].audioPos <= elapsed)
            {
                due.Add(_data.inputs[_inputCursor]);
                Plugin.Log.LogInfo($"[Player] Injecting input: player={_data.inputs[_inputCursor].player}, action={_data.inputs[_inputCursor].action}, audioPos={_data.inputs[_inputCursor].audioPos:F3}, elapsed={elapsed:F3}");
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

            // 从当前游标开始查找匹配的 HitEvent
            // 注意：只在找到匹配时才移动游标，避免跳过事件
            for (int i = _hitCursor; i < _data.hits.Count; i++)
            {
                var h = _data.hits[i];

                // 如果这个 HitEvent 的 bar 已经远远超过当前 bar，停止查找
                if (h.bar > bar + 2) break;

                // 匹配条件：player, rowID, bar, isRelease 都相同
                if (h.player == player &&
                    h.rowID == rowID &&
                    h.bar == bar &&
                    h.isRelease == isRelease)
                {
                    hit = h;
                    // 只在找到匹配时才移动游标
                    _hitCursor = i + 1;
                    return true;
                }
            }

            // 没找到匹配，不移动游标
            Plugin.Log.LogWarning($"[Player] TryDequeueHit failed: no match for player={player} rowID={rowID} bar={bar} isRelease={isRelease} (cursor={_hitCursor}/{_data.hits.Count})");
            return false;
        }

        // ── 时间跳转查询 ────────────────────────────────────────────

        /// <summary>
        /// 检查当前时间点是否有待执行的时间跳转事件。
        /// 返回需要跳转的 TimeJumpEvent，如果没有则返回 null。
        /// </summary>
        public TimeJumpEvent CheckPendingTimeJump(double currentAudioPos, int currentBar)
        {
            if (_data.timeJumps == null || _data.timeJumps.Count == 0) return null;

            while (_timeJumpCursor < _data.timeJumps.Count)
            {
                var jump = _data.timeJumps[_timeJumpCursor];

                // 提前触发：当到达起始小节的前一个小节时就准备跳转
                // 这样可以避免在 fromBar 期间有事件被错误处理
                // 例如：fromBar=44，在 bar 43 时就触发跳转
                if (currentBar >= jump.fromBar - 1)
                {
                    _timeJumpCursor++;
                    return jump;
                }

                break; // 还没到时间，等待下一帧
            }

            return null;
        }

        // ── 游戏状态查询 ────────────────────────────────────────────

        /// <summary>
        /// 收集当前帧应触发的游戏状态变化事件。
        /// </summary>
        public System.Collections.Generic.List<GameStateEvent> CollectDueGameStates(double currentAudioPos)
        {
            var due = new System.Collections.Generic.List<GameStateEvent>();
            if (_data.gameStates == null || _data.gameStates.Count == 0) return due;

            double elapsed = currentAudioPos - _playbackStartDsp;

            while (_gameStateCursor < _data.gameStates.Count &&
                   _data.gameStates[_gameStateCursor].audioPos <= elapsed)
            {
                due.Add(_data.gameStates[_gameStateCursor]);
                _gameStateCursor++;
            }

            return due;
        }

        // ── Miss 判定查询 ───────────────────────────────────────────

        /// <summary>
        /// 收集当前帧应触发的 Miss 判定事件。
        /// </summary>
        public System.Collections.Generic.List<MissEvent> CollectDueMisses(double currentAudioPos)
        {
            var due = new System.Collections.Generic.List<MissEvent>();
            if (_data.misses == null || _data.misses.Count == 0) return due;

            double elapsed = currentAudioPos - _playbackStartDsp;

            while (_missCursor < _data.misses.Count &&
                   _data.misses[_missCursor].audioPos <= elapsed)
            {
                due.Add(_data.misses[_missCursor]);
                _missCursor++;
            }

            return due;
        }

        /// <summary>
        /// 检查给定的 Beat 是否应该在回放时强制触发 Miss。
        /// 用于 Beat.Update() Prefix 中判断是否需要提前触发 Miss。
        /// </summary>
        /// <param name="player">玩家索引（0=P1, 1=P2）</param>
        /// <param name="rowID">轨道 ID</param>
        /// <param name="bar">小节号</param>
        /// <param name="beatInputTime">拍子的 inputTime（绝对 DSP 时间）</param>
        /// <returns>如果应该 Miss 返回 true，否则返回 false</returns>
        public bool ShouldForceMiss(int player, int rowID, int bar, double beatInputTime)
        {
            if (_data.misses == null || _data.misses.Count == 0) return false;

            // 将 beatInputTime 转换为相对时间
            double beatInputElapsed = beatInputTime - _data.sessionStartDsp;

            // 在 Miss 列表中查找匹配的事件
            // 从当前游标开始向后查找（不移动游标，因为可能有多个 Beat 同时检查）
            for (int i = _missCursor; i < _data.misses.Count; i++)
            {
                var miss = _data.misses[i];

                // 如果 Miss 事件的 beatInputTime 比当前 Beat 晚很多，停止查找
                if (miss.beatInputTime > beatInputElapsed + 0.5) break;

                // 匹配条件：player, rowID, bar, beatInputTime 都相同
                if (miss.player == player &&
                    miss.rowID == rowID &&
                    miss.bar == bar &&
                    System.Math.Abs(miss.beatInputTime - beatInputElapsed) < 0.01) // 允许 0.01 秒误差
                {
                    return true;
                }
            }

            return false;
        }
    }
}
