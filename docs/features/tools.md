# 功能：工具与手势

实现中心：`CanvasView` + `MainWindow` 工具 Combo。

## 工具枚举 `CanvasTool`

| 工具 | 说明 |
|---|---|
| `Select` | 拖动标尺控制点；Ctrl 平移 / Alt 旋转 / Shift 等比 |
| `Brush` | 光栅笔刷（各预设） |
| `Fill` | 填充桶 |
| `RectSelect` / `Lasso` / `MagicWand` | 选区 |
| `Gradient` | 线性/径向渐变 |
| `VectorPen` | 矢量折线笔 |
| `VectorNode` | 节点选择/拖动/Alt 删除 |
| `VectorCloseFill` | 闭合并填充路径 |
| `VectorSpline` | Catmull-Rom 样条绘制 |
| `FrameRect` | 分格矩形 |
| `TextPlace` | 禁用 |

## 工具列表按层类型

- **光栅层**：选择、铅笔/喷枪/橡皮/画笔/水彩/马克笔/涂抹、柳叶笔、填充、渐变、矩形/套索/魔棒
- **矢量层**：选择、矢量笔/橡皮(占位)、节点编辑、闭合填充、样条、分格
- **分格层**：选择、分格框、矢量笔

## 笔刷参数面板（按工具过滤）

`BrushParamsPanel` 在切换笔刷预设时按 `BrushToolKind` 显示子集（`UpdateBrushParamVisibility`），与 `StrokeSession` 实际读取一致：

| 参数 | 铅笔/画笔/水彩/马克笔/喷枪 | 橡皮 | 涂抹 | 柳叶笔 |
|---|---|---|---|---|
| Size / MinSize / Hardness / SoftEdge / Spacing / AA / Size 压感 | ✓ | ✓ | ✓ | — |
| Opacity | ✓ | ✓ | ✓ | ✓ |
| Opacity 压感 | ✓ | ✓ | ✓ | — |
| Flow / Flow 压感 | ✓ | ✓ | — | — |
| Blend | 画笔/水彩/马克笔 | — | — | — |
| Texture | ✓ | — | — | — |
| Smudge | — | — | ✓ | — |
| Stabilizer / 直线 | ✓ | ✓ | ✓ | ✓ |
| Lock Alpha | ✓ | — | ✓ | ✓ |

柳叶重叠（XOR）在**设置**里的 `WillowOverlap`，不在笔刷面板。

## 指针手势

| 操作 | 输入 |
|---|---|
| 绘制/工具 | 左键 / 笔 |
| 平移 | 中键 或 空格+拖 |
| 缩放 | 滚轮 |
| 吸管 | 右键（采合成色） |
| 交换 FG/BG | X |
| 橡皮模式切换 | E |
| 压感调试 | 1/2/3/0 |

## 捕获策略（重要）

`OnMouseLeftButtonDown`：

- 调用 `HandleToolDown` → 返回是否真正开始交互
- **仅成功时** `CaptureMouse()`
- 选择工具未命中控制点：**不捕获**，避免挡住侧栏 UI

## 历史

绘制类操作结束 → `PushAlreadyDone` → `OperationPushed`（缩时一帧/操作）。
