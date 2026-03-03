using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using RDReplay.Core;
using RDReplay.Storage;

namespace RDReplay.UI
{
    /// <summary>
    /// 回放设置面板 UI：
    /// - 回放保存路径输入框
    /// - 进入回放时是否自动展开控制面板的开关
    /// - 本脚本只负责 UI <-> ReplaySettings，同一画布内谁想弹出面板，只需调用 TogglePanel()
    /// </summary>
    public class ReplaySettingsUI : MonoBehaviour
    {
        [Header("根节点（缩放动画）")]
        public RectTransform panelRoot;

        [Header("控件")]
        public TMP_InputField replayPathInput;
        public Toggle autoOpenPanelToggle;

        [Header("动画")]
        public float tweenDuration = 0.2f;

        private bool _visible;

        void Awake()
        {
            // 初始隐藏
            if (panelRoot != null)
            {
                panelRoot.localScale = Vector3.zero;
                _visible = false;
            }

            // 初始化路径输入框
            if (replayPathInput != null)
            {
                string path = ReplaySettings.ReplaysRootOverride;
                if (string.IsNullOrEmpty(path))
                    path = ReplayFileManager.ReplaysRoot;
                replayPathInput.text = path;
                // 值变化时立即更新设置，结束编辑时刷新根目录/创建文件夹
                replayPathInput.onValueChanged.AddListener(OnReplayPathValueChanged);
                replayPathInput.onEndEdit.AddListener(OnReplayPathEndEdit);
            }

            // 初始化 Toggle
            if (autoOpenPanelToggle != null)
            {
                autoOpenPanelToggle.isOn = ReplaySettings.AutoOpenControlPanelOnReplay;
                autoOpenPanelToggle.onValueChanged.AddListener(OnAutoOpenPanelToggled);
            }
        }

        /// <summary>
        /// 供外部按钮调用：弹出/收起设置面板。
        /// </summary>
        public void TogglePanel()
        {
            if (panelRoot == null) return;

            _visible = !_visible;
            Vector3 targetScale = _visible ? Vector3.one : Vector3.zero;

            panelRoot.DOKill();
            panelRoot
                .DOScale(targetScale, tweenDuration)
                .SetEase(_visible ? Ease.OutBack : Ease.InBack);
        }

        // 输入框内容变化时立刻更新到设置（不做磁盘/目录操作，保持轻量）
        private void OnReplayPathValueChanged(string value)
        {
            ReplaySettings.ReplaysRootOverride = value;
        }

        private void OnReplayPathEndEdit(string value)
        {
            ReplaySettings.ReplaysRootOverride = value;
            ReplayFileManager.RefreshRootFromSettings();

            // 访问一次以触发目录创建（如有需要）
            var _ = ReplayFileManager.ReplaysRoot;
        }

        private void OnAutoOpenPanelToggled(bool isOn)
        {
            ReplaySettings.AutoOpenControlPanelOnReplay = isOn;
        }
    }
}

