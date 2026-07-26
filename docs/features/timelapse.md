# 功能：缩时录制

实现：`Controls/TimelapseRecorder.cs` + MainWindow 左栏面板。

## 行为

1. 用户指定**目录**与**文件名**、帧率（默认 30）
2. 开始录制后，每次 `History.OperationPushed`（新操作，非撤销）捕获一帧
3. 帧 = 全文档 `CompositeToBgra` → PNG 序列（临时目录）
4. 停止 → 异步 ffmpeg 编码 MP4（libx264，偶数尺寸 scale）
5. 无 ffmpeg → 保留 PNG 序列文件夹

## 与设置联动

`AppSettings`：

- `TimelapseEnabled`：是否显示左栏面板
- `TimelapseDirectory` / `TimelapseFileName` / `TimelapseFps`：默认值

## 文档切换

- `BindHistory` 在 `DocumentChanged` 时重绑 `OperationPushed`
- 录制中换文档：`BindDocument` 更新目标文档

## 依赖

- 优先：`AppContext.BaseDirectory/ffmpeg.exe`
- 其次：`tools/ffmpeg.exe`、PATH、常见安装路径
- 下载脚本：`tools/download-ffmpeg.ps1`

## 注意

- 一操作一帧，非实时时间轴
- 大画布长录制磁盘占用大
- 编码中 UI 禁用停止按钮，状态「正在编码…」
