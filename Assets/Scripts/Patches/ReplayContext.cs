using RDReplay.Core;

namespace RDReplay.Patches
{
    /// <summary>
    /// 各 Patch 之间共享的全局运行时状态容器。
    /// 作为轻量协调中心，避免 Patch 类之间直接互相引用。
    /// </summary>
    public static class ReplayContext
    {
        /// <summary>当前回放系统模式</summary>
        public static ReplayMode CurrentMode = ReplayMode.None;

        /// <summary>StartTheGame 调用时记录的游戏实例引用，等待 Handmode 帧触发 Begin()</summary>
        public static scnGame PendingGameInstance;

        /// <summary>游戏速度（传给 Recorder.Begin 的 levelSpeed）</summary>
        public static float PendingLevelSpeed;

        /// <summary>是否已在当前局中触发 Recorder.Begin()（防止重复触发）</summary>
        public static bool RecorderBegan;

        /// <summary>
        /// 游戏结束后缓存的 ReplayData，等待玩家按 Ctrl+R 确认保存。
        /// null 表示没有待保存的录像。
        /// </summary>
        public static RDReplay.Core.ReplayData PendingReplay;

        /// <summary>待保存录像的回放文件夹路径（SaveReplay 之后由 Patch_EndLevel 填写）</summary>
        public static string PendingReplayFolder;

        /// <summary>
        /// 回放模式下 Patch_Update 正在注入输入时置 true，
        /// 用于告知 Patch_Input 这是注入调用而非真实键盘输入，不需要再录制。
        /// </summary>
        public static bool IsReplayInjecting;

        /// <summary>
        /// 回放模式下的“长按”状态，用来驱动 RDInput.p1IsPressed / p2IsPressed，
        /// 以便 cutscene skip 等逻辑能按录像里的长按来工作。
        /// index 0 = P1, 1 = P2
        /// </summary>
        public static bool[] ReplayIsPressed = new bool[2];

        /// <summary>
        /// 本帧内是否发生了“按下”事件（用于驱动 RDInput.p1Press / p2Press）。
        /// index 0 = P1, 1 = P2
        /// </summary>
        public static bool[] ReplayPress = new bool[2];

        /// <summary>
        /// 本帧内是否发生了“抬起”事件（用于驱动 RDInput.p1Release / p2Release）。
        /// index 0 = P1, 1 = P2
        /// </summary>
        public static bool[] ReplayRelease = new bool[2];

        /// <summary>
        /// 回放模式下 RDInput 逻辑按键的状态（Left/Right/Up/Down/Cancel/Skip/Restart/Quit）。
        /// 索引使用 LogicalInputType 枚举的 int 值。
        /// </summary>
        public static bool[] LogicalPress   = new bool[(int)LogicalInputType.Down + 1];
        public static bool[] LogicalIsPressed = new bool[(int)LogicalInputType.Down + 1];
        public static bool[] LogicalRelease = new bool[(int)LogicalInputType.Down + 1];

        /// <summary>截屏字节（JPEG/PNG），若关卡没有预览图则由 SaveCoordinator 截取</summary>
        public static byte[] PendingScreenshotBytes;

        /// <summary>关卡所在目录，用于复制预览图</summary>
        public static string PendingLevelDir;

        /// <summary>
        /// 标记下一次进入 scnLevelSelect 时，应该把相机放回地下室电脑（Ian 桌面）。
        /// 从回放场景返回关卡选择前由 ReplayListUI 设置。
        /// </summary>
        public static bool ReturnToBasementComputer;

        // ── 时间跳转暂存（用于 Patch_TimeJump 的 Prefix/Postfix 协作）──────
        /// <summary>待记录的时间跳转：跳转前的小节号（-1 表示无待记录跳转）</summary>
        public static int PendingTimeJumpFromBar = -1;

        /// <summary>待记录的时间跳转：跳转目标小节号</summary>
        public static int PendingTimeJumpToBar = -1;

        /// <summary>待记录的时间跳转：跳转前的 audioPos</summary>
        public static double PendingTimeJumpAudioPosBefore = 0.0;

        public static void Reset()
        {
            CurrentMode = ReplayMode.None;
            PendingGameInstance = null;
            PendingLevelSpeed = 1f;
            RecorderBegan = false;
            PendingReplay = null;
            PendingReplayFolder = null;
            IsReplayInjecting = false;
            PendingScreenshotBytes = null;
            PendingLevelDir = null;
            ReplayIsPressed[0] = ReplayIsPressed[1] = false;
            ReplayPress[0] = ReplayPress[1] = false;
            ReplayRelease[0] = ReplayRelease[1] = false;

            for (int i = 0; i < LogicalPress.Length; i++)
            {
                LogicalPress[i] = false;
                LogicalIsPressed[i] = false;
                LogicalRelease[i] = false;
            }

            PendingTimeJumpFromBar = -1;
            PendingTimeJumpToBar = -1;
            PendingTimeJumpAudioPosBefore = 0.0;
        }
    }
}
