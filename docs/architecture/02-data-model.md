# 数据模型

主类型：`src/Eidolon.Core/Document.cs` 及相邻文件。

## Document

| 成员 | 含义 |
|---|---|
| `Width` / `Height` / `Dpi` | 画布尺寸与 DPI |
| `Background` | `White` / `Transparent` / `Color` |
| `Root` | 根 `GroupLayer` |
| `ActiveLayerId` | 当前活动图层 |
| `Colors` | 前景/背景 `ColorRgba8` |
| `History` | `HistoryStack` |
| `Selection` | 选区 mask |
| `Rulers` | `RulerState` |
| `IsDirty` | 未保存变更 |
| `FilePath` | 工程路径（可选） |

工厂方法：`AddRasterLayer` / `AddVectorLayer` / `AddTextLayer` / `AddFrameLayer`（默认名为英文键，UI 本地化）。

## Layer 体系

```
LayerNode
  ├─ RasterLayer   Surface: TileSurface
  ├─ VectorLayer   Strokes + RasterCache
  ├─ TextLayer     Content + RasterCache
  ├─ FrameLayer    Frames + RasterCache
  └─ GroupLayer    Children[]
```

通用属性：`Id`, `Name`, `Visible`, `Opacity`, `Blend`, `ClippedToBelow`, `Locks`。

`LayerLocks`：`None`, `Transparency`, `Pixels` 等（位标志）。

## TileSurface

- 默认 tile 边长 **256**
- 键：`Key(tx,ty) = ((long)tx << 32) | (uint)ty`
- 像素：`ColorRgba8[]`（直 alpha）
- 支持按 tile 快照/恢复 → 撤销

## History

- `IDocumentCommand`：`Name`, `Undo`, `Redo`
- `HistoryStack`：`Execute` / `PushAlreadyDone` / `Undo` / `Redo`
- 事件：`Changed`（任意变更）、`OperationPushed`（仅新操作，缩时用）
- 主命令：`TileEditCommand`（图层 id + before/after tiles）

## Selection

- mask 存于 tile 表面
- 模式：`Replace` / `Add` / `Subtract` / `Intersect`
- 支持矩形、套索点列、魔棒（容差）

## Color

- 存储：sRGB `ColorRgba8`
- 模型转换：`ColorModels`（RGB/HSV/HSL/OKLCH）
- OKLCH 出域：`OklchToRgbChecked`

## RulerState

见 [features/rulers.md](../features/rulers.md)。挂在 `Document.Rulers`，不进图层像素。
