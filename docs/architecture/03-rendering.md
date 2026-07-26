# 渲染与刷新

## 合成路径

入口：`Compositor`（`Eidolon.Core`）

1. `FillBackground`（白/透明/自定义色）
2. 自下而上遍历 `Root.Children`
3. 按层类型：
   - **Raster**：tile 逐像素混合
   - **Vector/Text/Frame**：`CacheDirty` 时重建 `TileSurface` 缓存，再合成
4. 输出：
   - `CompositeToBgra`：直 alpha BGRA
   - `CompositeToPbgra`：预乘 alpha（WPF 显示，避免黑边）

混合：`BlendMode` 全套（正常、正片叠底、滤色、叠加…排除）。

剪贴：`ClippedToBelow` 时用下方首个非剪贴光栅层 alpha 约束。

## 画布显示

`CanvasView`（`Eidolon.App.Controls`）：

1. 维护 `_pixels` + `WriteableBitmap`（Pbgra32）
2. `FullRedraw`：全图 `CompositeToPbgra` → `WritePixels`
3. `RedrawDirty`：脏矩形合成 + 连续缓冲写回
4. `OnRender`：
   - 画布灰底
   - `ViewportState` 矩阵（缩放/平移/旋转/镜像）
   - 文档位图（NearestNeighbor）
   - 选区蚂蚁线、工具预览、标尺叠加
   - 屏幕空间压感 HUD

## 刷新策略

| 场景 | 方式 |
|---|---|
| 笔划 dab | `RedrawDirty` |
| 撤销/重做 | `FullRedraw`（History.Changed） |
| 换层结构 | `FullRedraw` |
| 视图变换 | 仅 `InvalidateVisual`（不重合成） |
| 标尺拖动 | `InvalidateVisual` |

## 坐标系

- **文档空间**：像素坐标，原点左上
- **屏幕空间**：控件坐标
- 转换：`ViewportState.ScreenToDocument` / `CreateMatrix`
- 标尺吸附/预览均在文档空间计算，再随 viewport 变换绘制

## 性能注意（beta 关注）

- 大画布全量合成仍是 CPU 瓶颈
- 脏区路径已存在，部分操作仍 FullRedraw
- 缩时每操作全图 PNG 编码，长录制 I/O 重
