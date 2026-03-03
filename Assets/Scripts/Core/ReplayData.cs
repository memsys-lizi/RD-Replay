using System.Collections.Generic;

namespace RDReplay.Core
{
    /// <summary>
    /// RDInput 逻辑按键类型（不关心物理键位，只关心“Left/Right/Up/Down/Cancel/Skip/Restart/Quit”等动作）。
    /// </summary>
    public enum LogicalInputType
    {
        Skip,
        Restart,
        Quit,
        Cancel,
        Left,
        Right,
        Up,
        Down,
    }

    /// <summary>
    /// 单次逻辑按键事件（用于菜单/分支等非节奏输入的重放）。
    /// </summary>
    public class LogicalInputEvent
    {
        /// <summary>按键发生时的 audioPos（相对 sessionStartDsp 的偏移秒数）。</summary>
        public double audioPos;

        /// <summary>逻辑按键类型。</summary>
        public LogicalInputType type;

        /// <summary>"press" 或 "release"。</summary>
        public string phase;
    }

    /// <summary>
    /// 时间轴跳转事件（Cutscene Skip、Checkpoint 等导致的 ScrubToBarNum 调用）
    /// </summary>
    public class TimeJumpEvent
    {
        /// <summary>跳转发生时的 audioPos（跳转前，相对 sessionStartDsp）</summary>
        public double audioPosBefore;

        /// <summary>跳转后的 audioPos（相对 sessionStartDsp）</summary>
        public double audioPosAfter;

        /// <summary>跳转前的小节号</summary>
        public int fromBar;

        /// <summary>跳转到的目标小节号</summary>
        public int toBar;

        /// <summary>跳转原因："cutscene_skip" / "checkpoint" / "scrub" / "other"</summary>
        public string reason;
    }

    /// <summary>
    /// 游戏状态变化事件（GameState 切换）
    /// </summary>
    public class GameStateEvent
    {
        /// <summary>状态变化时的 audioPos（相对 sessionStartDsp）</summary>
        public double audioPos;

        /// <summary>变化前的状态（GameState 枚举值）</summary>
        public int fromState;

        /// <summary>变化后的状态（GameState 枚举值）</summary>
        public int toState;

        /// <summary>当前小节号</summary>
        public int barNumber;
    }

    /// <summary>
    /// Miss 判定事件（玩家未按，拍子自动 Miss）
    /// </summary>
    public class MissEvent
    {
        /// <summary>Miss 发生时的 audioPos（相对 sessionStartDsp）</summary>
        public double audioPos;

        /// <summary>0 = P1, 1 = P2, 2 = CPU</summary>
        public int player;

        /// <summary>Miss 所在轨道 ID</summary>
        public int rowID;

        /// <summary>所在小节号</summary>
        public int bar;

        /// <summary>拍子的 inputTime（预期按键时间）</summary>
        public double beatInputTime;

        /// <summary>错误权重</summary>
        public float weight;

        /// <summary>是否是 Hold 拍</summary>
        public bool isHoldBeat;
    }

    /// <summary>
    /// 单次按键/松开事件
    /// </summary>
    public class InputEvent
    {
        /// <summary>按键时的 AudioSettings.dspTime（双精度，微秒级精度）</summary>
        public double audioPos;

        /// <summary>0 = P1, 1 = P2</summary>
        public int player;

        /// <summary>"press" 或 "release"</summary>
        public string action;

        /// <summary>命中的轨道 ID，-1 表示未命中任何轨道（先存 -1，由 Patch_Judgment 回填）</summary>
        public int rowID = -1;
    }

    /// <summary>
    /// 单次判定结果事件（Pulse / SpaceBarReleased 产生）
    /// </summary>
    public class HitEvent
    {
        /// <summary>判定发生时的 audioPos</summary>
        public double audioPos;

        /// <summary>0 = P1, 1 = P2</summary>
        public int player;

        /// <summary>判定所在轨道 ID</summary>
        public int rowID;

        /// <summary>所在小节号（来自 beat.bar）</summary>
        public int bar;

        /// <summary>判定档位：VeryEarly(-2) / SlightlyEarly(-1) / Perfect(0) / SlightlyLate(1) / VeryLate(2) / Missed(-9999)</summary>
        public int offsetType;

        /// <summary>audioPos - beat.inputTime（秒，负=早，正=晚）</summary>
        public float timeOffset;

        /// <summary>错误权重（来自 beat.weight），影响血条扣减量</summary>
        public float weight;

        /// <summary>是否完全错过（|offsetType| >= 2）</summary>
        public bool isMissed;

        /// <summary>是否是 Hold 拍</summary>
        public bool isHoldBeat;

        /// <summary>是否是 Hold 松开判定</summary>
        public bool isRelease;
    }

    /// <summary>
    /// 游戏结束时的成绩汇总
    /// </summary>
    public class ReplayResult
    {
        /// <summary>最终评级字符串，如 "S"、"A+"、"F"</summary>
        public string rank;

        public float mistakes;
        public float mistakesP1;
        public float mistakesP2;

        public int earlyOffsetsSumP1;
        public int lateOffsetsSumP1;
        public int earlyOffsetsSumP2;
        public int lateOffsetsSumP2;

        public int totalOffsetsSumP1 => earlyOffsetsSumP1 + lateOffsetsSumP1;
        public int totalOffsetsSumP2 => earlyOffsetsSumP2 + lateOffsetsSumP2;
    }

    /// <summary>
    /// 完整回放数据，序列化为 replay.json 存储
    /// </summary>
    public class ReplayData
    {
        // ── 版本 & 兼容性 ──────────────────────────────────────────
        /// <summary>回放格式版本，变更数据结构时递增</summary>
        public int version = 2;

        /// <summary>录制时的游戏版本号，用于兼容性提示</summary>
        public string gameVersion;

        // ── 关卡信息 ───────────────────────────────────────────────
        /// <summary>内置关卡用 internalIdentifier，自定义关卡用完整路径</summary>
        public string levelId;

        /// <summary>关卡显示名称（settings.song），直接缓存避免回放时再解析谱面</summary>
        public string levelName;

        /// <summary>关卡作者（settings.author）</summary>
        public string levelAuthor;

        /// <summary>是否是自定义关卡（从外部文件加载）</summary>
        public bool isCustomLevel;

        // ── 游玩配置 ───────────────────────────────────────────────
        /// <summary>游玩速度（0.75 / 1.0 / 1.5 等）</summary>
        public float levelSpeed;

        /// <summary>是否双人模式</summary>
        public bool twoPlayerMode;

        /// <summary>P1 皮肤标识</summary>
        public string p1Skin;

        /// <summary>P2 皮肤标识（单人时为空）</summary>
        public string p2Skin;

        /// <summary>
        /// 录制时 P1 的基础判定窗口（不含 hitMarginMultiplier），用于回放时锁定判定难度。
        /// </summary>
        public float baseHitMarginP1;

        /// <summary>
        /// 录制时 P2 的基础判定窗口（不含 hitMarginMultiplier），用于回放时锁定判定难度。
        /// </summary>
        public float baseHitMarginP2;

        // ── 时间基准 ───────────────────────────────────────────────
        /// <summary>
        /// 游戏进入 Handmode 时的 AudioSettings.dspTime。
        /// 所有 InputEvent.audioPos 和 HitEvent.audioPos 均相对此值计算偏移。
        /// </summary>
        public double sessionStartDsp;

        /// <summary>录制时的 UTC 时间（ISO 8601）</summary>
        public string recordedAt;

        /// <summary>本局游玩总时长（秒），由最后一个 InputEvent.audioPos - sessionStartDsp 计算</summary>
        public float duration;

        // ── 数据流 ─────────────────────────────────────────────────
        /// <summary>输入事件流，按 audioPos 升序排列</summary>
        public List<InputEvent> inputs = new List<InputEvent>();

        /// <summary>判定结果流，按 audioPos 升序排列</summary>
        public List<HitEvent> hits = new List<HitEvent>();

        /// <summary>
        /// Rand(N) 调用结果序列，按调用顺序记录。
        /// 用于含随机分支的谱面（如 Samurai Roulette）保证回放时路线完全一致。
        /// </summary>
        public List<int> randResults = new List<int>();

        /// <summary>
        /// RDInput 层面的逻辑按键事件（Left/Right/Up/Down/Cancel/Skip/Restart...），
        /// 用于菜单、关卡分支等非节奏判定逻辑的重放。
        /// </summary>
        public List<LogicalInputEvent> logicalInputs = new List<LogicalInputEvent>();

        /// <summary>
        /// 时间轴跳转事件（Cutscene Skip、Checkpoint 等），按 audioPosBefore 升序排列。
        /// </summary>
        public List<TimeJumpEvent> timeJumps = new List<TimeJumpEvent>();

        /// <summary>
        /// 游戏状态变化事件（GameState 切换），按 audioPos 升序排列。
        /// </summary>
        public List<GameStateEvent> gameStates = new List<GameStateEvent>();

        /// <summary>
        /// Miss 判定事件（玩家未按，拍子自动 Miss），按 audioPos 升序排列。
        /// </summary>
        public List<MissEvent> misses = new List<MissEvent>();

        // ── 成绩 ───────────────────────────────────────────────────
        public ReplayResult result;

        // ── 签名（由 ReplayCrypto 填写，不参与签名计算本身）──────────
        /// <summary>HMAC-SHA256 签名，Base64 编码，防止文件被手动篡改</summary>
        public string signature;
    }
}
