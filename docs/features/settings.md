# 功能：设置

## 存储

| 项 | 值 |
|---|---|
| 目录 | `%APPDATA%/Eidolon/` |
| 文件 | `setting.json` |
| 实现 | `AppSettings.Load` / `Save` |
| 格式 | JSON camelCase，缩进 |

## 字段

| 字段 | 默认 | 含义 |
|---|---|---|
| `defaultCanvasWidth/Height` | 1920×1080 | 启动/新建默认尺寸 |
| `defaultColorModel` | 3 (OKLCH) | 0RGB 1HSV 2HSL 3OKLCH |
| `timelapseEnabled` | true | 是否显示缩时面板 |
| `timelapseDirectory` | Documents/Eidolon/Timelapse | 默认目录 |
| `timelapseFileName` | timelapse | 默认文件名 |
| `timelapseFps` | 30 | 默认帧率 |
| `language` | cn | 语言（预留） |
| `jpegCompress` | true | JPEG 是否压缩 |
| `jpegQuality` | 90 | 1–100 |
| `webpLossless` | false | WebP 无损 |
| `webpQuality` | 90 | 1–100 |
| `exportPreserveTransparency` | true | 导出保留透明 |
| `brush.*` | 见下 | 工具栏笔刷参数（关闭时写回） |
| `colors.fgR/G/B` `bgR/G/B` | 黑/白 | 前景/背景色 |

### 笔刷子对象 `brush`

| 字段 | 默认 | 含义 |
|---|---|---|
| `sizePx` | 10 | 笔刷直径 |
| `minSizeRatio` | 0.05 | 最小尺寸比 |
| `opacity` / `flow` | 1 | 不透明度 / 流量 |
| `hardness` | 0.9 | 硬度 |
| `softEdge` | 0.05 | 软边 |
| `blend` | 0 | 混色 |
| `spacing` | 0.12 | 间距 |
| `antiAlias` | true | 抗锯齿 |
| `lockAlpha` | false | 锁定透明 |
| `stabilizerStrength` | 0 | 防抖 |
| `sizeByPressure` / `opacityByPressure` | true | 压感尺寸/不透明 |
| `flowByPressure` | false | 压感流量 |
| `textureStrength` / `textureScale` / `textureSeed` | 0 / 1 / 1 | 纹理 |
| `smudgeStrength` | 0.55 | 涂抹强度 |
| `straightLineMode` | false | 直线模式 |


## UI

- 操作栏最左按钮「设置」→ `SettingsWindow`
- 启动：`App.OnStartup` / MainWindow Loaded 加载并应用到 UI
- 新建对话框种子宽高来自设置
- 工具栏笔刷与前景/背景色写入同一 `setting.json`，窗口关闭时保存

## 应用点

| 设置 | 应用位置 |
|---|---|
| 画布尺寸 | `Canvas.NewDocument`、`NewDocumentDialog` |
| 颜色模型 | `ColorModelCombo.SelectedIndex` |
| 缩时 | 面板可见性、目录/文件名/FPS 默认 |
| 导出 | `ImageExport.Export(..., AppSettings.Current)` |
| 笔刷/颜色 | `MainWindow.ApplyToolsAndColorsFromSettings` / `CaptureToolsAndColorsToSettings`（窗口 Closing 保存） |
