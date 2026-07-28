# 模块：Eidolon.Brush

路径：`src/Eidolon.Brush/`  
依赖：`Eidolon.Core`

## 职责

笔刷参数、预设、笔划会话、像素 dab 绘制（含涂抹/纹理）。

## 关键类型

| 类型 | 文件 | 说明 |
|---|---|---|
| `BrushParameters` | `BrushParameters.cs` | 尺寸/硬度/流量/间距/压感标志/纹理/涂抹… |
| `BrushPreset` | 同上 | Pencil/Eraser/Airbrush/Brush/Watercolor/Marker/Smudge |
| `StrokeSession` | （笔划会话） | Begin/Move/End → TileEditCommand |
| `PixelPaintOp` | `PixelPaintOp.cs` | dab 盖章、选区覆盖、锁透明、smudge、grain |

## 预设名称

内部英文：`Pencil`, `Eraser`, `Airbrush`, `Brush`, `Watercolor`, `Marker`, `Smudge`。  
UI 通过 `LocalizeBrushName` / i18n 键 `Tool.*` 显示。

## 笔划生命周期

1. `Begin`：记录 before tiles，首点 dab
2. `Move`：位置稳定器 + **独立压感稳定器** + 间距插值 dab（压感沿段插值），累积 dirty
3. `End`：after tiles → `TileEditCommand("Stroke")`

## 稳定器

`Stabilizer` 对 **位置** 与 **压感** 使用同一 strength，但为 **两条独立 lag 通道**（`Filter` / `FilterPressure`），互不耦合。  
标尺只约束位置；压感仍走独立平滑，避免沿轨迹的粗细/透明度台阶。

## 与选区

绘制时读取 `Selection` coverage；空选区表示全画布可画。

## 对称轴

App 层可同时开第二条镜像 `StrokeSession`（`ShouldMirrorStroke`）。
