using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using RDReplay.Core;
using RDReplay.Patches;

namespace RDReplay.UI
{
    /// <summary>
    /// 回放控制条：
    /// - 顶部进度条 + 时间显示（只显示，不可点击）
    /// - 底部控制面板：返回按钮 + 显隐按钮（折叠/展开整块面板）
    /// </summary>
    public class ReplayControlUI : MonoBehaviour
    {
        [Header("进度条")]
        public RectTransform progressFill;   // 用来改 X 缩放的 Image
        public TMP_Text      currentTimeText;
        public TMP_Text      totalTimeText;

        [Header("控制按钮")]
        public Button backButton;           // 返回回放列表场景（ScnReplay）
        public Button togglePanelButton;    // 显隐控制面板

        [Header("控制面板")]
        public RectTransform controlPanel;  // 整个控制面板的 RectTransform
        public float shownY  = 95f;
        public float hiddenY = -50f;

        private scnGame       _game;
        private scrConductor  _conductor;
        private ReplayPlayer  _player;
        private ReplayData    _data;
        private CanvasGroup   _canvasGroup;

        private static ReplayControlUI _instance;
        private bool _panelHidden;

        // 面板动画时长（统一用 DOTween）
        private const float PanelTweenDuration = 0.2f;

        void Awake()
        {
            // 单例 + 跨场景常驻
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            SetVisible(false); // 默认隐藏，等进入回放模式事件再显示

            if (backButton != null)        backButton.onClick.AddListener(OnBackClicked);
            if (togglePanelButton != null) togglePanelButton.onClick.AddListener(OnTogglePanelClicked);

            // 控制面板初始始终为隐藏y，并且_hidden=true，这样不管后续怎么切换状态都始终和_hidden逻辑一致
            if (controlPanel != null)
            {
                var pos = controlPanel.anchoredPosition;
                pos.y = hiddenY;
                controlPanel.anchoredPosition = pos;
                _panelHidden = true;
            }

            // 订阅回放模式切换事件
            ReplayModeEvents.ReplayModeChanged += OnReplayModeChanged;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            ReplayModeEvents.ReplayModeChanged -= OnReplayModeChanged;
        }

        void Update()
        {
            if (ReplayContext.CurrentMode != ReplayMode.Replaying) return;

            // 懒加载：有时候回放开始事件触发时 scnGame/scnConductor 还没初始化
            if (_conductor == null || _data == null)
            {
                _game      = scnGame.instance;
                _conductor = scrConductor.instance;
                _player    = ReplayPlayer.Instance;
                _data      = _player != null ? _player.Data : null;

                if (_data != null && totalTimeText != null)
                    totalTimeText.text = FormatTime(_data.duration);

                if (_conductor == null || _data == null) return;
            }

            // 确保回放时鼠标可见
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            // 使用 ReplayPlayer 的起始 DSP 时间来计算相对进度，避免多次回放/不同起点造成错乱
            double rel = (_player != null)
                ? _player.GetElapsed(_conductor.audioPos)
                : (_conductor.audioPos - _data.sessionStartDsp);
            float t = _data.duration > 0f ? Mathf.Clamp01((float)(rel / _data.duration)) : 0f;

            if (progressFill != null)
            {
                var s = progressFill.localScale;
                s.x = t;
                progressFill.localScale = s;
            }

            if (currentTimeText != null)
                currentTimeText.text = FormatTime(Mathf.Clamp((float)rel, 0f, _data.duration));
        }

        private void OnReplayModeChanged(bool isReplaying)
        {
            if (isReplaying)
            {
                // 若设置中关闭了控制 UI，则进入回放时完全不显示进度条/控制条
                if (!ReplaySettings.AutoOpenControlPanelOnReplay)
                {
                    SetVisible(false);
                    return;
                }

                // 进入回放模式：刷新引用，显示 UI
                _game      = scnGame.instance;
                _conductor = scrConductor.instance;
                _player    = ReplayPlayer.Instance;
                _data      = _player != null ? _player.Data : null;

                // 初始化时间显示和进度条
                if (_data != null)
                {
                    if (totalTimeText != null)
                        totalTimeText.text = FormatTime(_data.duration);
                    if (currentTimeText != null)
                        currentTimeText.text = FormatTime(0f);
                }
                if (progressFill != null)
                {
                    var s = progressFill.localScale;
                    s.x = 0f;
                    progressFill.localScale = s;
                }

                // 每次进入回放都把控制面板重置为隐藏状态并且 _panelHidden = true
                if (controlPanel != null)
                {
                    controlPanel.DOKill();
                    var pos = controlPanel.anchoredPosition;
                    pos.y = hiddenY;
                    controlPanel.anchoredPosition = pos;
                    _panelHidden = true;
                }

                // 自动展开 ：如果设置AutoOpenControlPanelOnReplay为true，则直接显示面板，不触发OnTogglePanelClicked
                // 正确做法是直接展开到shownY，而不是模拟点击按钮，因为按钮逻辑是切换
                if (ReplaySettings.AutoOpenControlPanelOnReplay && controlPanel != null)
                {
                    controlPanel.DOKill();
                    var pos = controlPanel.anchoredPosition;
                    pos.y = shownY;
                    controlPanel.anchoredPosition = pos;
                    _panelHidden = false;
                }

                SetVisible(true);
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            else
            {
                // 退出回放模式：隐藏 UI
                SetVisible(false);
            }
        }

        private void SetVisible(bool visible)
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = visible;
            _canvasGroup.blocksRaycasts = visible;
        }

        string FormatTime(float t)
        {
            int totalSec = Mathf.FloorToInt(t);
            int m = totalSec / 60;
            int s = totalSec % 60;
            int ms = Mathf.FloorToInt((t - totalSec) * 1000f);
            return $"{m:00}:{s:00}.{ms / 10:00}";
        }

        void OnBackClicked()
        {
            // 结束当前回放并返回回放列表场景
            if (ReplayPlayer.Instance != null)
                ReplayPlayer.DestroyInstance();
            ReplayContext.CurrentMode = ReplayMode.None;
            ReplayModeEvents.RaiseReplayStopped();

            scnBase.GoToScene("ScnReplay");
        }

        void OnTogglePanelClicked()
        {
            if (controlPanel == null || togglePanelButton == null) return;

            _panelHidden = !_panelHidden;
            float targetY = _panelHidden ? hiddenY : shownY;

            // 用 DOTween 做面板 Y 方向动画
            controlPanel.DOKill();
            controlPanel.DOAnchorPosY(targetY, PanelTweenDuration).SetEase(Ease.OutCubic);
        }

    }
}
