using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using RDReplay.Storage;

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
        public TMP_Text titleText;

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
            backButton?.onClick.AddListener(() => SceneManager.LoadScene("scnLevelSelect"));

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
    }
}
