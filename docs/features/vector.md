# 功能：矢量编辑（alpha-03+）

## 数据

`VectorStroke`：

- `Points`：节点列表（位置/压感/宽度）
- `Closed` / `Filled` / `FillColor`
- `PathMode`：`Polyline` | `Spline`（Catmull-Rom）

历史：`VectorLayerEditCommand` 快照整层 strokes 列表。

## 栅格化

`VectorRasterizer.DrawStroke`：

1. 若 `Filled`：扫描线填充（样条先细分）
2. 描边：折线分段 dab，或样条采样后分段

## 工具

| 工具 | 操作 |
|---|---|
| 矢量笔 | 自由折线拖拽；抬笔写入历史 |
| 样条 | **左键点击**添加控制点；**右键**结束（点到开路径端点则连接/闭合）；**Esc** 取消 |
| 节点 | 点击选中；拖动改位置；**Alt+点** 删除节点（≤2 点则删整路径） |
| 闭合/填充 | 点击路径 → `Closed=true` + `Filled=true`（**不 Capture 鼠标**） |
| 矢量橡皮 | 点击/拖过路径或节点删除 |

## 叠加 UI

矢量相关工具下绘制控制多边形与节点；样条放置中高亮当前路径；开路径端点略大以便连接。

## 输入捕获约定

- 仅**连续手势**（拖拽）才 `CaptureMouse`
- 单击型工具（闭合填充、样条加点、魔棒、填充）**不捕获**
- `MouseUp` 安全释放残留 capture，避免挡住侧栏 UI

## 限制

- 无贝塞尔手柄（仅 Catmull-Rom 过点）
- 填充为简单奇偶扫描线，自交路径表现有限
- 未单独导出 SVG
