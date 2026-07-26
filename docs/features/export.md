# 功能：导出

入口：操作栏「导出」→ `ImageExport.Export`。

## 格式

| 扩展名 | 实现 | 透明 | 设置项 |
|---|---|---|---|
| `.png` | `EidolonFileStore.ExportPng` | 可 | `ExportPreserveTransparency` |
| `.jpg`/`.jpeg` | WPF JpegEncoder | 否（铺白） | `JpegCompress`, `JpegQuality` |
| `.bmp` | WPF BmpEncoder | 否 | — |
| `.webp` | ffmpeg `libwebp` | 可 | `WebpLossless`, `WebpQuality` |
| `.psd` | `PsdWriter` 最小 RGB PSD | 合成层 | 透明合成可选 |

## 对话框过滤器

i18n 键：`Dialog.FilterExport`（多格式列表）。

## PSD 限制（alpha-03）

- 单 composite 层 + 合并图像数据
- 非完整图层树/混合/蒙版语义
- beta 可增强为真实多图层写出

## WebP 限制

- 需要本机 ffmpeg
- 失败时抛出明确错误文案
