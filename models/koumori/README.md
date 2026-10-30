# Koumori 内置模型 / Bundled model

将完整运行模型包放在本目录。`publish.ps1` 和 `release.ps1` 会校验 schema 1/2/3 清单及 SHA-256，并自动打包清单、其引用的运行文件和 schema 2/3 的编码器原始许可证；不复制 README、checkpoint 或训练资料。目录不存在或仅含 README 时跳过；已有 ONNX/前端配置但缺少清单，或模型不完整、清单无效、摘要不符时，发布失败。

Place the complete runtime model package here. `publish.ps1` and `release.ps1` validate the schema 1/2/3 manifest and SHA-256 digests, then bundle the manifest, its referenced runtime files, and the original encoder license for schema 2/3 automatically. READMEs, checkpoints, and training material are excluded. A missing directory or one containing only a README is skipped; ONNX/frontend files without a manifest, an incomplete model, an invalid manifest, or a digest mismatch fail publishing.

发布目标为 Windows 的 `AegiNext/models/koumori/` 或 macOS 的 `AegiNext.app/Contents/MacOS/models/koumori/`，复制发生在签名和安装包/DMG 生成前。构建和 clean 不处理模型；开发运行时需手动复制到桌面程序输出目录中的 `models/koumori/`，也可在自动打轴设置中选择“手动指定”。发布包没有内置模型时同样支持手动目录。

Publishing copies models to `AegiNext/models/koumori/` on Windows or `AegiNext.app/Contents/MacOS/models/koumori/` on macOS before signing and installer/DMG creation. Build and clean leave models alone. For development, copy models manually to `models/koumori/` under the desktop output directory, or select **Specify manually** in automatic timing settings. Packages without bundled models also support a manual directory.

Download the runtime package from cloud disk and keep these files together:  [Google Drive](https://drive.google.com/drive/folders/1Fp5-526j-L9PWRWjFLk54p4volQqY-9n?usp=sharing)

```text
koumori
├── manifest.json
├── model.onnx
├── snap-model.onnx
├── encoder.onnx
├── frontend.json
├── encoder-frontend.json
├── decoder.json
└── WHISPER-LICENSE.txt
```
