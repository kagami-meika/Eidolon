# 开发工作流

## 环境

- Windows 10/11
- .NET 8 SDK
- 可选：ffmpeg（WebP 导出 + 缩时 MP4）

## 常用命令

```powershell
cd C:\desktop\todo\Eidolon

# 运行
dotnet run --project src/Eidolon.App

# 调试日志
dotnet run --project src/Eidolon.App -- --log-level Debug

# 测试
dotnet test

# 发布
dotnet publish src/Eidolon.App -c Release -r win-x64 --self-contained false
```

## 日志

- 默认目录：`%APPDATA%/Eidolon/`（`eidolon_*.log` 总大小 < 1MB）
- `--debug`：工作目录 `./Logs/`，不限制大小
- 级别：`--log-level` 或 `EIDOLON_LOG_LEVEL`（`--debug` 时默认至少 Debug）
- 压感/工具/标尺/缩时关键路径均有 `AppLog` 标签

## 编码约定

1. Core 保持无 WPF 依赖
2. 用户可见字符串走 i18n（`strings.cn.json` / `en`）
3. 内部稳定键用英文（图层名、预设名、命令名）
4. UI 捕获鼠标必须“交互成功才 Capture”
5. 改行为同步改 `docs/`

## 主题/控件

- 仅 Warm：`Themes/Theme.Warm.xaml`
- 自绘控件：`Themes/Controls.xaml`
- 资源键前缀：`Eid.*`

## ffmpeg

```powershell
# 可选下载到 tools/
powershell -File tools/download-ffmpeg.ps1
```

App 构建在存在 `tools/ffmpeg.exe` 时复制到输出目录。
