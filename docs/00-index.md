# Eidolon 文档索引（v0.2.1）

版本：`alpha-03`  
用途：在进入 `beta-01` 前固化现状，统一后续开发/测试/发布工作流。

## 文档地图

| 文件 | 内容 |
|---|---|
| [architecture/01-overview.md](architecture/01-overview.md) | 产品目标、技术栈、解决方案结构 |
| [architecture/02-data-model.md](architecture/02-data-model.md) | Document / Layer / Tile / History |
| [architecture/03-rendering.md](architecture/03-rendering.md) | 合成、画布刷新、显示格式 |
| [modules/core.md](modules/core.md) | Eidolon.Core |
| [modules/brush.md](modules/brush.md) | Eidolon.Brush |
| [modules/input.md](modules/input.md) | Eidolon.Input |
| [modules/io.md](modules/io.md) | Eidolon.IO + 导出 |
| [modules/app.md](modules/app.md) | Eidolon.App UI / 设置 / i18n |
| [features/tools.md](features/tools.md) | 工具与手势 |
| [features/rulers.md](features/rulers.md) | 标尺系统 |
| [features/timelapse.md](features/timelapse.md) | 缩时录制 |
| [features/export.md](features/export.md) | 多格式导出 |
| [features/settings.md](features/settings.md) | 设置与持久化 |
| [workflow/dev.md](workflow/dev.md) | 开发/构建/测试/日志 |
| [workflow/beta-01-checklist.md](workflow/beta-01-checklist.md) | beta-01 入口清单 |

## 原则

1. **文档描述“当前已实现行为”**，不以未做功能为目标状态。
2. 跨模块约定写在 architecture；具体 API/文件写在 modules。
3. 用户可见行为写在 features；工程流程写在 workflow。
4. 进入 beta 后：改行为必同步改文档；PR 以对应文档小节为检查项。
