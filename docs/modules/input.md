# 模块：Eidolon.Input

路径：`src/Eidolon.Input/`

## 职责

数位板/笔压感采集，与 WPF 解耦。

## 服务

### WinTabTabletService

- 加载 `Wintab32.dll`
- `WTOpen` / 包队列 / `Poll`
- `LastPressure` 归一化 0–1
- `Status` 英文诊断串

### PointerPenService

- 处理 `WM_POINTER*` 
- 读 pointer 压力与 eraser 标志
- `GetPressureOrDefault`

## App 集成

`CanvasView`：

1. `HwndSource` hook → 两服务处理消息
2. 合成渲染 tick 中 `Poll` WinTab
3. `ResolvePressure` 优先级：**Pointer > WinTab > Stylus > 鼠标默认**

`Eidolon.App.csproj` 启用：

```xml
Switch.System.Windows.Input.Stylus.EnablePointerSupport = true
```

## 已知问题

- 抬笔后短时间可能双发 stylus/mouse 事件 → `_lastStylusTicks` 80ms 防抖
- 多设备 TabletDevices 枚举仅日志用途
