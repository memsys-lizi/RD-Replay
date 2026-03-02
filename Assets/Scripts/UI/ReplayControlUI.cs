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
        

        // 动画相关：手动补间
        private bool _isAnimatingPanel = false;
        private float _panelAnimStartY;
        private float _panelAnimEndY;
        private float _panelAnimTime;
        private float _panelAnimDuration = 0.2f;

        // 按钮图标旋转相关
        private bool _isRotatingIcon = false;
        private float _iconAnimStartZ;
        private float _iconAnimEndZ;
        private float _iconAnimTime;
        private Transform _iconTransform;

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

            if (controlPanel != null)
            {
                var pos = controlPanel.anchoredPosition;
                pos.y = shownY;
                controlPanel.anchoredPosition = pos;
                _panelHidden = false;
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

            double rel = _conductor.audioPos - _data.sessionStartDsp;
            float t = _data.duration > 0f ? Mathf.Clamp01((float)(rel / _data.duration)) : 0f;

            if (progressFill != null)
            {
                var s = progressFill.localScale;
                s.x = t;
                progressFill.localScale = s;
            }

            if (currentTimeText != null)
                currentTimeText.text = FormatTime(Mathf.Clamp((float)rel, 0f, _data.duration));

            // 动画控制面板移动（手动补间）
            if (_isAnimatingPanel && controlPanel != null)
            {
                _panelAnimTime += Time.unscaledDeltaTime;
                float animT = Mathf.Clamp01(_panelAnimTime / _panelAnimDuration);
                // ease: OutCubic
                animT = 1f - Mathf.Pow(1f - animT, 3f);
                float y = Mathf.Lerp(_panelAnimStartY, _panelAnimEndY, animT);
                var pos = controlPanel.anchoredPosition;
                pos.y = y;
                controlPanel.anchoredPosition = pos;

                if (animT >= 1f)
                {
                    _isAnimatingPanel = false;
                }
            }

            // 动画旋转图标（手动补间）
            if (_isRotatingIcon && _iconTransform != null)
            {
                _iconAnimTime += Time.unscaledDeltaTime;
                float rotT = Mathf.Clamp01(_iconAnimTime / _panelAnimDuration);
                // ease: OutCubic
                rotT = 1f - Mathf.Pow(1f - rotT, 3f);
                float z = Mathf.Lerp(_iconAnimStartZ, _iconAnimEndZ, rotT);
                _iconTransform.localRotation = Quaternion.Euler(0f, 0f, z);

                if (rotT >= 1f)
                {
                    _isRotatingIcon = false;
                }
            }
        }

        private void OnReplayModeChanged(bool isReplaying)
        {
            if (isReplaying)
            {
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

            // 停止动画
            _isAnimatingPanel = false;

            // 获取动画起点
            float currentY = controlPanel.anchoredPosition.y;
            _panelAnimStartY = currentY;
            _panelAnimEndY = targetY;
            _panelAnimTime = 0f;
            _isAnimatingPanel = true;

            // 旋转隐藏按钮图标（Z 轴 0 / 90，手动画）
            var img = togglePanelButton.GetComponent<Image>();
            if (img != null)
            {
                float currentZ = img.transform.localEulerAngles.z;
                // 修正正负角度
                if (currentZ > 180f) currentZ -= 360f;

                float targetZ = _panelHidden ? 0f : 90f;
                _iconAnimStartZ = currentZ;
                _iconAnimEndZ = targetZ;
                _iconAnimTime = 0f;
                _iconTransform = img.transform;
                _isRotatingIcon = true;
            }
        }
    }
}
