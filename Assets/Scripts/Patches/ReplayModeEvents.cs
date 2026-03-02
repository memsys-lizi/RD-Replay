using System;

namespace RDReplay.Patches
{
    /// <summary>
    /// 回放模式切换事件：用于像 ReplayControlUI 这样的全局 UI 订阅。
    /// </summary>
    public static class ReplayModeEvents
    {
        /// <summary>
        /// 回放模式变化事件。参数：true = 进入回放模式，false = 退出回放模式。
        /// </summary>
        public static event Action<bool> ReplayModeChanged;

        public static void RaiseReplayStarted()
        {
            ReplayModeChanged?.Invoke(true);
        }

        public static void RaiseReplayStopped()
        {
            ReplayModeChanged?.Invoke(false);
        }
    }
}

