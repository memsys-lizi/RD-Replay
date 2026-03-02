namespace RDReplay.Storage
{
    /// <summary>
    /// 回放文件夹内的轻量元数据，供回放列表 UI 快速读取（不解析完整 replay.json）。
    /// 目前直接使用 ReplayData 中的字段子集，序列化为 meta.json。
    /// </summary>
    public class ReplayMeta
    {
        public int    version;
        public string gameVersion;
        public string levelId;
        public string levelName;
        public string levelAuthor;
        public bool   isCustomLevel;
        public float  levelSpeed;
        public bool   twoPlayerMode;
        public string recordedAt;
        public float  duration;
        public string rank;
        public float  mistakes;
        /// <summary>replay.json 文件大小（字节），用于 UI 显示</summary>
        public long   fileSizeBytes;
        /// <summary>预览图文件名（相对回放文件夹），通常为 "preview.png"</summary>
        public string previewImage;
    }
}
