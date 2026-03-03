using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using DG.Tweening;
using RDReplay.Storage;
using RDReplay.Patches;

namespace RDReplay.UI
{
    /// <summary>
    /// 回放列表场景主控。只负责两件事：
    ///   1. 顶部返回按钮
    ///   2. 把录像列表实例化成 ReplayItemUI 条目塞进 ScrollView
    /// 播放、删除等具体操作全部由 ReplayItemUI 自己处理。
    /// </summary>
    public class ReplayListUI : MonoBehaviour
    {
        [Header("顶部")]
        public Button  backButton;
        public Button  settingsButton;      // 打开/关闭设置面板
        public Button  aboutButton;         // 打开/关闭关于面板
        public TMP_Text titleText;

        [Header("设置")]
        public ReplaySettingsUI settingsUI; // 设置面板控制脚本

        [Header("关于")]
        public RectTransform aboutPanelRoot; // 关于面板根节点（缩放动画），面板内容你自己放
        public float aboutTweenDuration = 0.2f;
        private bool _aboutVisible;

        [Header("列表")]
        public Transform  itemContainer;  // ScrollView > Viewport > Content
        public GameObject itemPrefab;     // 绑定 ReplayItem.prefab

        [Header("空状态")]
        public TMP_Text emptyHintText;

        // ── Unity 生命周期 ───────────────────────────────────────────

        private void Start()
        {
            if (titleText != null)
                titleText.text = "回放系统";

            backButton?.onClick.RemoveAllListeners();
            backButton?.onClick.AddListener(() =>
            {
                // 告诉下一个 LevelSelect：我们是从回放界面返回的，应该把相机放在地下室电脑处
                ReplayContext.ReturnToBasementComputer = true;
                scnBase.GoToScene("scnLevelSelect");
            });

            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveAllListeners();
                settingsButton.onClick.AddListener(OnSettingsButtonClicked);
            }

            if (aboutButton != null)
            {
                aboutButton.onClick.RemoveAllListeners();
                aboutButton.onClick.AddListener(OnAboutButtonClicked);
            }

            // 关于面板默认隐藏（缩放为 0），内容由你在 Unity 里布置
            if (aboutPanelRoot != null)
            {
                aboutPanelRoot.localScale = Vector3.zero;
                _aboutVisible = false;
            }

            // 若未在 Inspector 赋值，尝试在子物体里自动寻找设置 UI
            if (settingsUI == null)
                settingsUI = GetComponentInChildren<ReplaySettingsUI>(includeInactive: true);

            RefreshList();
        }

        // ── 列表刷新（由 ReplayItemUI 删除后也可回调） ───────────────

        public void RefreshList()
        {
            if (itemContainer != null)
                foreach (Transform child in itemContainer)
                    Destroy(child.gameObject);

            var entries = ReplayFileManager.ListReplays();
            bool isEmpty = entries == null || entries.Count == 0;

            emptyHintText?.gameObject.SetActive(isEmpty);
            if (isEmpty) return;

            foreach (var (folder, meta) in entries)
            {
                if (itemPrefab == null || itemContainer == null) break;

                var go   = Instantiate(itemPrefab, itemContainer);
                var item = go.GetComponent<ReplayItemUI>();
                item?.Setup(folder, meta, this);
            }
        }

        private void OnSettingsButtonClicked()
        {
            if (settingsUI != null)
                settingsUI.TogglePanel();
        }

        private void OnAboutButtonClicked()
        {
            if (aboutPanelRoot == null) return;

            _aboutVisible = !_aboutVisible;
            Vector3 targetScale = _aboutVisible ? Vector3.one : Vector3.zero;

            aboutPanelRoot.DOKill();
            aboutPanelRoot
                .DOScale(targetScale, aboutTweenDuration)
                .SetEase(_aboutVisible ? DG.Tweening.Ease.OutBack : DG.Tweening.Ease.InBack);
        }
    }
}
