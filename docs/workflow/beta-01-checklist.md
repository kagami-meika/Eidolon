# beta-01 入口清单

基于 alpha-03 现状，进入 beta 前建议完成/确认：

## 文档

- [x] 分模块文档落盘（本目录）
- [ ] README 指向 docs 并更新功能表到 alpha-03 全量
- [ ] 变更流程：功能改动必须改对应 docs 文件

## 稳定性

- [ ] 选择工具 + 侧栏点击回归（无错误 Capture）
- [ ] 标尺：一点/两点/三点预览线可见性回归
- [ ] 对称轴 Ctrl/Alt 变换回归
- [ ] 缩时：有/无 ffmpeg 两条路径
- [ ] 导出：png/jpg/bmp/psd；webp（有 ffmpeg）
- [ ] 设置读写与默认新建尺寸

## 功能缺口（beta 候选，按优先级排序）

1. 矢量节点编辑与真实矢量导出
2. PSD 多层写出（不透明度/混合）
3. 文字工具重新启用
4. 合成性能（脏区全覆盖 / GPU 路径评估）
5. 设置：语言切换落地
6. 自动化 UI 测试（至少导出与设置）

## 发布检查

- [ ] 全项目版本号统一（InformationalVersion）
- [ ] `dotnet test` 全绿
- [ ] Release 发布包含/不含 ffmpeg 的说明
- [ ] 更新 CHANGELOG（建议 `docs/CHANGELOG.md`）

## 建议 beta-01 版本号

- `0.1.0-beta.1` / InformationalVersion `beta-01`
