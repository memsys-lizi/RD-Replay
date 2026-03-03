using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace RDReplay.UI
{
    /// <summary>
    /// 右上角提示条（类似 NVIDIA 录制提示）：
    /// - 左侧绿色细条 + 右侧白色块
    /// - 绿色先滑入，再滑入白色块并显示文字，停留后一起滑出
    /// 单例 + 全局静态 Show 方法，预制体挂在根节点上。
    /// </summary>
    public class ReplayToastUI : MonoBehaviour
    {
        [Header("节点")]
        public RectTransform greenBar;
        public RectTransform whiteBar;
        public TMP_Text      messageText;
        public CanvasGroup   canvasGroup;

        [Header("动画参数（使用 X 缩放实现滑入/收回）")]
        public float greenEnterDuration = 0.15f; // 绿色滑入时间
        public float whiteEnterDelay    = 0.05f; // 绿色完成后到白色开始之间的延迟
        public float whiteEnterDuration = 0.15f; // 白色滑入时间
        public float textFadeDuration   = 0.15f; // 文字渐显/渐隐时间
        public float stayDuration       = 1.8f;  // 完全显示后停留时间
        public float exitStepDuration   = 0.15f; // 退出阶段每一步的时长（文字 → 白块 → 绿条）

        private static ReplayToastUI _instance;

        private readonly Queue<string> _queue = new Queue<string>();
        private bool _isPlaying;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (canvasGroup == null)
                canvasGroup = GetComponentInChildren<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            // 初始：整体完全收起（X 缩放为 0），文本透明
            if (greenBar != null)
            {
                var s = greenBar.localScale;
                s.x = 0f;
                greenBar.localScale = s;
            }

            if (whiteBar != null)
            {
                var s = whiteBar.localScale;
                s.x = 0f;
                whiteBar.localScale = s;
            }

            canvasGroup.alpha = 0f;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        // ── 对外静态 API ─────────────────────────────────────────────

        /// <summary>显示一条提示文字（入队，按顺序播放）。</summary>
        public static void Show(string message)
        {
            if (_instance == null) return;
            _instance.Enqueue(message);
        }

        public static void ShowRecordingStarted()
        {
            Show("开始录制");
        }

        public static void ShowReplaySaved()
        {
            Show("回放已保存");
        }

        // ── 队列驱动 ────────────────────────────────────────────────

        private void Enqueue(string message)
        {
            _queue.Enqueue(message);
            if (!_isPlaying)
                StartCoroutine(PlayQueue());
        }

        private IEnumerator PlayQueue()
        {
            _isPlaying = true;
            while (_queue.Count > 0)
            {
                string msg = _queue.Dequeue();
                yield return PlaySingle(msg);
            }
            _isPlaying = false;
        }

        private IEnumerator PlaySingle(string message)
        {
            if (greenBar == null || whiteBar == null || messageText == null)
                yield break;

            messageText.text = message;

            // 重置：绿色/白色都收回（X 缩放 0），文本透明
            if (greenBar != null)
            {
                greenBar.DOKill();
                var s = greenBar.localScale;
                s.x = 0f;
                greenBar.localScale = s;
            }

            if (whiteBar != null)
            {
                whiteBar.DOKill();
                var s = whiteBar.localScale;
                s.x = 0f;
                whiteBar.localScale = s;
            }

            canvasGroup.DOKill();
            canvasGroup.alpha = 0f;

            // 绿色先滑入（通过 X 缩放 0 → 1）
            greenBar.DOScaleX(1f, greenEnterDuration).SetEase(Ease.OutCubic);
            yield return new WaitForSeconds(greenEnterDuration);

            // 稍微等一下再滑入白色块
            if (whiteEnterDelay > 0f)
                yield return new WaitForSeconds(whiteEnterDelay);

            whiteBar.DOKill();
            whiteBar.DOScaleX(1f, whiteEnterDuration).SetEase(Ease.OutCubic);
            yield return new WaitForSeconds(whiteEnterDuration);

            // 白色滑块到位后再渐显文字
            canvasGroup.DOFade(1f, textFadeDuration).SetEase(Ease.Linear);
            yield return new WaitForSeconds(textFadeDuration);

            // 停留一段时间
            if (stayDuration > 0f)
                yield return new WaitForSeconds(stayDuration);

            // 退出：严格按进入顺序反着来
            // 1) 先让文字渐隐
            canvasGroup.DOFade(0f, textFadeDuration).SetEase(Ease.Linear);
            yield return new WaitForSeconds(textFadeDuration);

            // 2) 再收白色滑块
            whiteBar.DOKill();
            whiteBar.DOScaleX(0f, exitStepDuration).SetEase(Ease.InCubic);
            yield return new WaitForSeconds(exitStepDuration);

            // 3) 最后收绿色细条
            greenBar.DOKill();
            greenBar.DOScaleX(0f, exitStepDuration).SetEase(Ease.InCubic);
            yield return new WaitForSeconds(exitStepDuration);
        }
    }
}

