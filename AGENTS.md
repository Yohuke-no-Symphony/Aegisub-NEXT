# AGENTS.md — Aegisub-NEXT 開發指引（給 AI 代理與協作者）

本檔記錄在本機（Windows 11, `C:\APP\@Video\Aegisub-NEXT`）上**實際驗證過**的建置、測試方式、工具鏈與已知問題。修改程式前請先讀完。

## 專案概觀

AegiNext 是以 .NET 10 + Avalonia 撰寫的字幕/特效工作台，媒體解碼、播放與匯出走原生 FFmpeg/SDL3（`native/`）。

| 路徑 | 內容 |
|---|---|
| `src/AegiNext.Core` | 工程模型（`ProjectDocument`、`ProjectMediaBinding`、驗證） |
| `src/AegiNext.Application` | 工程存取（`ProjectStore`、版本遷移）、預設庫 |
| `src/AegiNext.SubtitleFormats` | ASS/SRT 等字幕格式匯入匯出 |
| `src/AegiNext.Media` | 探測、解碼、播放、預覽轉換、音訊時鐘與分析 |
| `src/AegiNext.Rendering` | 字幕/圖形渲染 |
| `src/AegiNext.Export` | 影片匯出與字幕封裝 |
| `src/AegiNext.Koumori` | ONNX 智慧打軸 |
| `src/AegiNext.Cli` | 命令列 |
| `src/AegiNext.Desktop` | Avalonia 桌面程式（`aegi-next.exe`） |
| `Tests/AegiNext.*.Tests` | 各模組 xUnit 測試 |
| `docs/en`, `docs/zh-cn` | 使用者文件（中英兩份需同步更新） |

## 本機工具鏈（已安裝）

- .NET SDK **10.0.401**，安裝於 `%USERPROFILE%\.dotnet`（非系統安裝；需設定環境變數，見下）。
- PowerShell 7.6（`pwsh`；`build.ps1` 需要 7.2+），Windows PowerShell 5.1 為預設 shell。
- Git 2.54、GitHub CLI `gh` 2.96（已登入）。
- Scoop：`cmake` 4.4.5、`mingw` 16.2.0、`7zip`、`ripgrep`（`rg`）。新工具請優先用 Scoop 安裝。
- 原生相依（FFmpeg、SDL3、MSVC runtime）由 `build.ps1 -InstallDependencies` 下載到 `.dependencies/win-x64/`；原生建置產物在 `artifacts/native/`。兩者皆已被 `.gitignore` 忽略。

## 環境設定（每個新 shell 先執行）

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$env:DOTNET_MULTILEVEL_LOOKUP = 0
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
$env:PATH = "$env:DOTNET_ROOT;$env:USERPROFILE\scoop\apps\mingw\current\bin;$env:USERPROFILE\scoop\shims;$env:PATH"
Set-Location C:\APP\@Video\Aegisub-NEXT
```

## 建置（實際可用的指令）

```powershell
# 原生函式庫（首次或 native/ 有改動時）
pwsh -NoProfile -File .\build.ps1 -Target Native -Configuration Debug -InstallDependencies

# 整個方案（含測試專案）；TreatWarningsAsErrors 開啟，必須 0 警告
dotnet build AegiNext.sln -c Debug

# 只建桌面程式
dotnet build src\AegiNext.Desktop -c Debug
```

桌面程式輸出：`src\AegiNext.Desktop\bin\Debug\net10.0\win-x64\aegi-next.exe`。冒煙測試：啟動約 10 秒確認視窗出現、無例外，再關閉。

## 測試

```powershell
dotnet test Tests\AegiNext.Media.Tests -c Debug --no-build
dotnet test Tests\AegiNext.Desktop.Tests -c Debug --no-build --filter "FullyQualifiedName~AudioOnly"
```

- 可用的套件：`Application`、`Cli`、`Core`、`Desktop`、`Desktop.Ui`、`Export`、`Koumori`、`Media`、`Rendering`、`SubtitleFormats`。
- `Desktop.Ui`（Avalonia headless）很慢且曾在 main 上卡住超過 15 分鐘，請加 `--blame-hang-timeout 5m`。
- 也可用 `pwsh -NoProfile -File .\build.ps1 -Target Managed -Configuration Debug -RunTests -TestProjects Media`。

### main 上既有的失敗（2026-11-01，main `23e14f7`，本機基準）

宣稱某個失敗「與本次修改無關」前，**必須先在未修改的 main 上跑同一套件比對**。基準結果：

| 套件 | 失敗 / 總數 | 主要原因 |
|---|---|---|
| Application | 5 / 1912 | 「原子提交失敗」模擬測試（Windows 檔案語意不同）：EffectScriptPresetLibraryBatchDelete、SubtitleStyleOverwrite、SubtitleStylePresetLibraryBatchDelete、SubtitleStyleTimingBinding、ProjectStoreLargeDocument |
| Cli | 0 / 92 | — |
| Core | 2 / 949 | GraphicAssetModelTests.LinkedImages…、SubtitleAppearanceTests.SingleLayerEvaluation… |
| Desktop | 21 / 2696 | UpdatePreferences（列舉位置）、Startup 損毀預設庫 ×4、WorkspaceCloseLifecycle ×2、SubtitleTimelineWorkflow ×4、AnalysisCoordinator ×3、AssAppearanceWorkflow ×2 等 |
| Desktop.Ui | 55 / 1862（且會卡住） | DockTabStrip、TimelineTimingPostProcessorUi、EffectScriptLineNumbers、TimingWorkflowUi、TimelineDrawingCacheScaling 等 |
| Export | 3 / 472 | 預設庫原子提交失敗模擬 |
| Koumori | 0 / 203 | — |
| Media | 0 / 510 | — |
| Rendering | 19 / 595 | FontNamePreviewRendering、LinearRenderSurface、ProjectSceneRenderer 等（字型/浮點環境差異） |
| SubtitleFormats | 10 / 746 | ASS 匯入測試，與 CRLF 換行（Git autocrlf 檢出）有關，如 AssSourceImportTests、AssSubtitleFormatTests |

## 程式慣例

- **`WorkbenchCommand` 列舉值會被持久化**：新指令一律加在最後；每批新增需在 `WorkbenchPreferencesMigration` 加入對應的 `<= 最後一個指令` 集合。
- 新指令要同步：`ShortcutDefaults`（預設快捷鍵不得與既有衝突，否則留空）、`ShortcutSettingsSections`、`WorkbenchMenuCatalog`、`WorkbenchIcon`、`WorkbenchSession.Commands`。
- **i18n**：`src/AegiNext.Desktop/I18n/Languages/` 的 `en-US.json`、`ja-JP.json`、`zh-CN.json` 三份都要加。
- **工程格式**：改動 `ProjectDocument` 序列化語意時遞增 `ProjectDocument.CURRENT_VERSION`，並讓 `ProjectStore` 繼續接受舊版本（目前為 20，接受 19）。
- 檔案使用 UTF-8（無 BOM）與 CRLF；含中日文的檔案勿經由會轉碼的終端機輸出再寫回。
- 提交訊息使用 Conventional Commits（`feat:`、`fix:`、`docs:`、`test:`、`chore:`）。
- 暫存與建置輸出（`bin/`、`obj/`、`artifacts/`、`.dependencies/`）不可提交。

## Git / GitHub 規則

- `origin` = `XPRAMT/Aegisub-NEXT`（個人 fork，可推送）；`upstream` = `Yohuke-no-Symphony/Aegisub-NEXT`（原專案，**不可直接推送**，只能透過 PR 貢獻）。
- 功能各自開分支（如 `feat/open-audio`、`feat/lrc`），推送到 `origin`，再對 `upstream` 的開發分支 `dev/0.11` 開 PR（原作者實際合併到這裡，不是 `main`；功能分支請從 `upstream/dev/0.11` 切出。`gh pr create --repo Yohuke-no-Symphony/Aegisub-NEXT --base dev/0.11 --head XPRAMT:<分支>`），讓原作者能看到。對自己 fork 開 PR 沒有意義，不要做。**不要自行合併**。
- PR 說明使用簡體中文。

## 功能備忘：開啟音訊（純音訊工程）

- `ProjectMediaBinding.VideoStreamIndex` 可為 `null`（純音訊）；開啟音訊會**替換整個媒體綁定**，而非只換音軌。
- 預覽以 `AudioOnlyFrameSource`（25 fps、由音訊時鐘驅動）＋ `AudioOnlyPreviewConverter`（工程尺寸黑底）合成字幕。
- 純音訊工程停用影片匯出／字幕封裝（桌面、`VideoExporter`、CLI 皆會拒絕），關鍵影格吸附略過。
