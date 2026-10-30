# Koumori 内置模型 / Bundled model

模型需要手动复制到可执行文件旁的 `models/koumori/`，或在自动打轴设置中选择“手动指定”并指向已有模型目录。构建、发布和 clean 不处理模型；本仓库目录仅用于本地存放。

Copy the complete model package to `models/koumori/` beside the executable, or select **Specify manually** in automatic timing settings and choose an existing model directory. Build, publish, and clean leave models alone; this repository directory is only local storage.

Windows 发布目录：`AegiNext/models/koumori/`。开发运行时使用桌面程序输出目录中的 `models/koumori/`。macOS 已签名应用推荐把模型放在 `.app` 外并选择“手动指定”；若放入 `AegiNext.app/Contents/MacOS/models/koumori/`，需重新签名并验证应用。

On Windows, use `AegiNext/models/koumori/` in the published directory. For a signed macOS app, prefer an external model directory and **Specify manually**. Adding models inside `AegiNext.app/Contents/MacOS/models/koumori/` requires signing and verifying the app again.

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
