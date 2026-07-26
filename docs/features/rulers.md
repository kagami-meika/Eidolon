# 功能：标尺系统

实现：`Eidolon.Core/Rulers.cs` + `CanvasView` 绘制/命中 + 左栏 UI。

## 设计原则

1. 标尺是**叠加引导**，不写入图层像素
2. 吸附在**文档空间**；绘制经 `ObserveStrokePoint` + `Constrain`
3. 单次笔划锁定**一条轨迹**（速度方向采样后再定）
4. 悬浮预览受 **「显示标尺」** 控制（`Visible && PreviewEnabled`）

## 种类 `RulerKind`

| 种类 | 几何 | 控制点 | 吸附/约束 |
|---|---|---|---|
| `None` | — | — | 无 |
| `Straight` | 原点 + 角度 → 无限直线 | Origin | 锁到该直线 |
| `Ellipse` | 平面正方形四角射影（单应）+ 内切圆映射 | EllA–D | 锁到射影椭圆 |
| `Symmetry` | 隐藏原点 + 角度 → 无限轴 | 无可见端点；命中轴线本体 | 镜像第二笔 |
| `VanishingPoint` | 单灭点 | Vp | 下笔后锁 点→灭点 射线 |
| `Perspective1` | 灭点 + 画布水平/垂直 | Vp | 射线 / H / V 三选一锁定 |
| `Perspective2` | 视平线(原点+角) + 线上两灭点 | Vp0,Vp1（在视平线上） | 两灭点射线 / ⊥视平线 |
| `Perspective3` | 视平线 + 两灭点 + 第三灭点 | Vp0–2 | 三灭点射线择近 |
| `Fisheye6` | 视界圆 + 3 极角 → 6 灭点 / 3 灭线圆 / 3 预览圆 | FishHorizonCenter / FishHorizonRim / FishTheta1–3 | `TryComputeFisheyeGeo` 立体射影; 锁最近圆 |

### 六点鱼眼透视（简化为假的实现）

基于球极投影（Stereographic Projection），见 `docs/features/Stereographic Projection.md`。

控制点：
- `FishHorizonCenter`：视界圆心（Ctrl 拖动平移全局）
- `FishHorizonRim`：视界圆半径控制（拖动调整半径）
- `FishTheta1/2/3`：三个灭点极角控制（拖动改变极角），默认 60°/180°/300°
- `FishGlobalAngleDeg`：旋转偏移（Alt 旋转）
- **Shift + 拖动极角控制点**（Theta1–3）：以拖动开始时该控制点的极角为基准，在单位圆上按 **n×45°** 相对离散步进（不是吸附到绝对 0/45/90/…）；未按 Shift 时自由连续拖动
- 整组 **Shift 等比缩放** 仍可用：未命中极角控制点时（例如 Rim / 圆心附近）Shift 拖动
- 如果极角不满足任意内角在 90°–180° 条件，`TryComputeFisheyeGeo` 返回 false，跳过渲染

渲染：6 个灭点（成对）+ 3 个灭线圆 + 3 个经当前指针/触点的预览圆。

### 大半径圆（数值稳定）

三点求圆使用 **平移到 A 的局部坐标 + 垂线平分线闭式解**（double），避免全局 `x²+y²` 相减抵消。

当三角形极扁（`R ≫ edge`）时：

- **几何退化为无限直线**（最长边方向），不再存爆炸半径
- 吸附：投到该直线
- 预览：不调用 `DrawEllipse(hugeR)`，只对 **视口相交弧** 做弦长自适应角采样；过大 R 时用 **局部弦/切线** 近似

投影：`C + R * normalize(P-C)` 全程 double。

求圆：局部坐标 + **decimal** 线性解，再 double 半径。

预览采样：`SampleVisibleSegments` 返回**不相交**折线；跨间隙不连线，避免弓弦/弓形多余线段。大 R 退化为切线（2 点），不用多点假弧。

## 视平线 / 对称轴模型

- **不使用两端点**
- `HorizonOrigin` + `HorizonAngleDeg`（无限视平线）
- `SymmetryOrigin` + `SymmetryAngleDeg`
- 灭点 Vp0/Vp1 设置时 `ProjectToHorizon`
- 选择：Ctrl/Alt/Shift 可对整组变换；轴线本体可命中

## 吸附强度滑条

左栏 `0–40`：

| 值 | 行为 |
|---|---|
| 0 | 关闭吸附（`SnapEnabled=false`） |
| 中间 | 距离阈值软吸附 |
| 最大 | `ForceSnap` 强制投影 |

## 轨迹锁定

1. `BeginStrokeConstraint`：记录锚点，**不立刻锁**
2. `ObserveStrokePoint`：收集速度方向样本（≥3）
3. 平均方向稳定后 `LockTrackAt`
4. 整笔 `ProjectLocked` 只走该轨迹
5. `EndStrokeConstraint` 清理
6. 预热阶段（样本 < 3）：`ProvisionalSnap` 按当前方向临时投影到预览轨迹

## 选择工具

- 拖控制点改几何
- **Ctrl** 平移整套标尺
- **Alt** 绕质心旋转
- **Shift** 等比缩放
- 未命中不 CaptureMouse

## 悬浮预览

| 种类 | 预览 |
|---|---|
| 灭点 | 指针→灭点 |
| 一点透视 | 灭点射线 + 水平 + 垂直 |
| 两点透视 | 两灭点射线 + ⊥视平线 |
| 三点透视 | 三灭点射线 |
| 鱼眼 | 红绿蓝三圆（立体射影几何） |

绘制：`LineThrough(tip, dir)` 过指针无限线，避免旧 `LineInf` 偏移 bug。

## 独立吸附线开关

透视标尺（Perspective1/2/3、Fisheye6）的三根预览线可分别独立开关吸附：

| 开关 | 一点透视 | 两点透视 | 三点透视 | 六点鱼眼 |
|---|---|---|---|---|
| `Ruler.LineSnap0` ●红 | VP 射线 | Vp0 射线 | Vp0 射线 | 轴0 圆 |
| `Ruler.LineSnap1` ●绿 | 水平 H | Vp1 射线 | Vp1 射线 | 轴1 圆 |
| `Ruler.LineSnap2` ●蓝 | 垂直 V | ⊥视平线 | Vp2 射线 | 轴2 圆 |

- 关闭某线后：该线**仍绘制预览**，但不参与 `LockTrackAt` 吸附决策
- 若三线全部关闭：透视标尺不锁定轨迹（自由绘制）
- 对应属性：`RulerState.PerspectiveLine0/1/2Enabled`，持久化到 `setting.json` 的 `rulerLineSnap0/1/2`
- UI 位于左栏吸附滑条下方，仅透视类标尺可见

## Fisheye6 参考点 P

六点透视可额外放置一个**参考点 P**（颜色 `#00BCD4` 青色），并通过圆心 O 自动计算其对径反演点 $P'$：

$$OP \times OP' = r^2,\quad P' \text{ 与 } P \text{ 关于 O 反向共线}$$

- **三种模式**（`FisheyePMode`，UI 下拉）：
  - `Off` — 不显示 P，无影响
  - `VisualOnly` — 显示 P / P' / P–P' 连线 + 悬浮预览线，不参与吸附
  - `Snappable` — 同上，且 P–P' 线参与 `LockTrackAt` 吸附（与三圆竞争最近轨迹）
- **控制点 P** 操作：
  - **直接拖动** — 自由移动
  - **Ctrl** — 整体平移（P 随组移动）
  - **Alt** — 整体旋转（P 随组旋转）
  - **Shift+Alt** — P 绕 O 以 **45° 离散步进** 旋转，保持半径不变
  - **Shift** — 整体等比缩放
- 渲染：P 实心青色圆点；P' 较小半透明圆点；悬浮时过 P、P'、指针三点的青色预览圆
- 持久化：`setting.json` → `fisheyePMode` (`"Off"/"VisualOnly"/"Snappable"`) + `fisheyePX` / `fisheyePY`

### 视觉效果变更

- 视界圆 + 水平/竖直十字线改为深灰色 `#1A1A1A`（更不干扰灭线圆与 P 系统）

## UI

左栏：种类 Combo、显示、吸附强度、吸附线开关（透视类）、参考点 P 模式（六点透视）、重置、选择提示文案。
