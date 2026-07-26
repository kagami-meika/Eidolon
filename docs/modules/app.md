# 模块：Eidolon.App

路径：`src/Eidolon.App/`  
目标：`net8.0-windows` + WPF

## 职责

壳窗口、画布控件、主题、对话框、设置、i18n、缩时录制、导出 UI。

## 关键入口

| 文件 | 说明 |
|---|---|
| `App.xaml(.cs)` | 启动、日志、加载设置、全局异常 |
| `MainWindow.xaml(.cs)` | 主 UI 与命令 |
| `Controls/CanvasView.cs` | 画布交互与显示 |
| `Controls/TimelapseRecorder.cs` | 缩时帧与编码 |
| `Controls/ColorRampBar.cs` | 颜色通道色带 |
| `AppSettings.cs` | 设置读写 |
| `SettingsWindow.cs` | 设置对话框 |
| `ImageExport.cs` | 多格式导出调度 |
| `NewDocumentDialog.cs` | 新建画布 |
| `ThemedMessageWindow.cs` | 主题化消息框 |
| `TextRasterizer.cs` | 文字层缓存 |
| `ImeInput.cs` | IME/快捷键隔离 |
| `Localization/*` | SR + LocExtension |
| `Resources/strings.*.json` | 文案 |
| `Themes/*` | Warm 主题 + 控件模板 |

## UI 布局

1. 标题栏：品牌 + 版本 + 最小化/最大化/关闭（DPI 放大）
2. 操作栏：`设置 | 新建 打开 保存 导出 | 撤销 重做 | 选区 | 视图 | 关于`
3. 左栏：颜色 → 工具 → 工具选项 → 笔刷 → 标尺 → 缩时
4. 中：`CanvasView`
5. 右：图层面板 + 混合/不透明度
6. 底：状态栏

## 主题

- 仅 **Warm**（`Theme.Warm.xaml`）
- Cool 主题已删除
- 控件自绘：Slider / Button / ComboBox / ScrollBar 等（`Controls.xaml`）

## i18n

- `SR.Get` / `SR.Format` / `{loc:Loc Key}`
- 默认加载 `cn`，`en` 文化覆盖
- Core/Brush 内部英文键；UI 显示中文/英文

## 设置

见 [features/settings.md](../features/settings.md)。

## 日志

- `AppLog` 默认 → `%APPDATA%/Eidolon/eidolon_*.log`，总大小保持 < 1MB（删最旧）
- `--debug` → 工作目录 `./Logs/eidolon_*.log`，不限制大小；未指定级别时至少 Debug
- `--log-level Debug|Info|None` 或 `EIDOLON_LOG_LEVEL`
