// 此文件保留为空壳，所有 Patch 已迁移到 Patches/ 子目录下的独立文件：
//   Patches/Patch_GameStart.cs    —— 游戏开始 / 退出
//   Patches/Patch_Input.cs        —— 按键录制 / 屏蔽
//   Patches/Patch_Judgment.cs     —— Pulse / SpaceBarReleased 判定记录
//   Patches/Patch_RandEval.cs     —— Rand(N) 随机序列录制 / 回放
//   Patches/Patch_EndLevel.cs     —— 游戏结束 / Ctrl+R 保存
//   Patches/Patch_Update.cs       —— 帧循环：录制初始化 / 输入注入
//   Patches/Patch_LevelSelect.cs  —— Level Select 注入 Replay 入口
//   Patches/ReplayContext.cs      —— 全局运行时状态
//   Patches/SaveCoordinator.cs    —— 协程执行器（截图 / 保存）
