# 模块：Eidolon.IO

路径：`src/Eidolon.IO/`  
依赖：`Eidolon.Core`

## 工程格式 `.eidolon`

实现：`EidolonFileStore`

结构（ZIP）：

```
document.json     # 元数据、图层列表、背景、活动层
tiles/...         # 瓦片像素 blob（按层）
```

- 保存：`Save(doc, path)`
- 加载：`Load(path)` → 清空默认子层后重建
- 无层时回退创建 `Layer 1`

## PNG

- `ExportPng(doc, path, withTransparency)`
- 自写最小 PNG（RGBA + zlib）
- 透明背景可选强制铺白

## PSD

- `PsdWriter.Write`：最小 RGB 8-bit PSD
- 含合成图 + 单层 composite 层信息
- **非**完整 PS 图层语义；beta 可增强

## App 侧多格式导出

`Eidolon.App/ImageExport.cs`：

| 格式 | 路径 |
|---|---|
| PNG | `EidolonFileStore.ExportPng` |
| JPEG | WPF `JpegBitmapEncoder` + 设置质量 |
| BMP | WPF `BmpBitmapEncoder` |
| WebP | 临时 PNG → **ffmpeg libwebp** |
| PSD | `PsdWriter` |

检测扩展名：`ImageExport.DetectFormat`。
