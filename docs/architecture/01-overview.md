# 架构总览

## 产品定位

Eidolon 是 Windows 桌面开源绘画工具，工作流参考 PaintTool SAI 2：

- 瓦片图层 + CPU 合成（当前）
- 笔刷/压感/选区/渐变/矢量笔/分格
- 标尺辅助（直标尺、透视、对称、椭圆、鱼眼）
- 自有工程格式 `.eidolon` + 多格式导出
- Editorial 暖白 UI，`alpha-03`

## 技术栈

| 项 | 选择 |
|---|---|
| 运行时 | .NET 8 |
| UI | WPF (`net8.0-windows`) |
| 渲染 | CPU 合成 → `WriteableBitmap` Pbgra32 |
| 输入 | WinTab + WM_POINTER + WPF Stylus |
| 工程格式 | ZIP + JSON + tile blob |
| 导出 | PNG/JPG/BMP（托管）、WebP（ffmpeg）、PSD（自写） |
| 配置 | `%APPDATA%/Eidolon/setting.json` |
| i18n | 嵌入式 JSON（`strings.cn.json` / `strings.en.json`） |

## 解决方案结构

```
Eidolon.slnx
src/
  Eidolon.Core     # 文档模型、瓦片、合成、选区、标尺、历史
  Eidolon.Brush    # 笔刷参数、StrokeSession、PixelPaintOp
  Eidolon.Input    # WinTabTabletService、PointerPenService
  Eidolon.IO       # .eidolon 存读、PNG、PSD
  Eidolon.App      # WPF 壳、CanvasView、设置、导出、缩时、i18n
tests/
  Eidolon.Tests
tools/             # 可选 ffmpeg.exe + download-ffmpeg.ps1
Logs/              # 仅 --debug 时的运行时日志（默认写 %APPDATA%/Eidolon/）
docs/              # 本目录
```

## 依赖方向

```
App → Brush, Input, IO, Core
Brush → Core
IO → Core
Input → (无项目依赖)
Tests → Core, Brush, IO
```

**约束**：Core 不依赖 WPF；UI/编码相关放在 App。

## 版本

| 字段 | 值 |
|---|---|
| InformationalVersion | beta-01 |
| AssemblyVersion | 0.1.0.1 |
| 涉及 csproj | App / Core / Brush / IO / Input |

## 当前能力边界（beta-01）

已具备：光栅绘制管线、图层、历史、选区、多种工具、标尺吸附、缩时、设置、多格式导出。

明确未完成/占位：

- 矢量节点编辑 / 闭合填充 / 样条
- 文字工具（有 TextLayer + rasterizer，工具禁用）
- GPU 加速合成
- 完整 PSD 图层语义（当前为最小合成 PSD）
- 插件宿主（文案提及，未落地）
