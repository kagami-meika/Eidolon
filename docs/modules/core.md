# 模块：Eidolon.Core

路径：`src/Eidolon.Core/`  
目标框架：`net8.0`（无 WPF）

## 职责

文档模型、瓦片表面、合成器、选区、填充/渐变、矢量栅格化、历史、颜色模型、标尺几何。

## 关键文件

| 文件 | 说明 |
|---|---|
| `Document.cs` | 文档根对象 |
| `Layers.cs` | LayerNode / Raster / Group / locks |
| `VectorTextFrame.cs` | Vector/Text/Frame 层 |
| `TileSurface.cs` | Tile + TileSurface |
| `Compositor.cs` | 合成与混合 |
| `History.cs` | HistoryStack + TileEditCommand |
| `Selection.cs` | 选区 |
| `FloodFill.cs` | 填充桶 |
| `GradientFill.cs` | 线性/径向渐变 |
| `VectorRasterizer.cs` | 矢量/分格画入 tile |
| `ColorModels.cs` | RGB/HSV/HSL/OKLCH |
| `ColorRgba.cs` | 颜色结构 |
| `Geometry.cs` | Float2 / IntRect |
| `ViewportState.cs` | 视图矩阵 + PointerSample |
| `Rulers.cs` | 标尺状态与吸附 |
| `BlendMode.cs` | 混合枚举 |

## 对外约定

- 像素存储：**直 alpha**；显示预乘在 `CompositeToPbgra`
- 历史：实时绘制用 `PushAlreadyDone`，不重复 Redo
- 默认图层名用英文稳定串（`Layer 1`），UI 再本地化
- 标尺不写入像素；吸附通过 `RulerState.Constrain` / `ObserveStrokePoint`

## 测试覆盖（当前）

`tests/Eidolon.Tests` 覆盖 tile、混合、stroke、undo/redo、存读、锁透明、选区矩形、线性渐变等（以测试工程为准）。
