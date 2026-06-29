# Changelog

本專案所有重要變更記錄於此。格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/),版本遵循 [SemVer](https://semver.org/lang/zh-TW/)。

## [Unreleased]

### 變更
- **推論後端三 flavor 正式出貨**(原 CUDA / Windows ML「僅骨架」狀態解除):**DirectML**(預設,任何 DX12 GPU,24H2 以下通用)/ **CUDA**(NVIDIA,24H2 以下)/ **Windows ML**(Win11 24H2+,EP 由 OS 動態下載)。切點為 OS 版本涵蓋;編譯期經 `InferenceFlavor` 屬性切 ONNX Runtime 套件 + 選 factory,呼叫端程式碼不動,各有 publish profile + CI release matrix(出三個 zip)。CPU / DirectML 已實機驗證;CUDA / Windows ML 為編譯 + publish 驗證,runtime 推論需對應硬體 / OS。

### 新增
- `IInferenceSessionFactory.InitializeAsync` 暖機接縫(預設 no-op;Windows ML 用於 async 註冊 EP 目錄)。
- `CudaSessionFactory`、真正的 `WindowsMlSessionFactory`(EP 目錄暖機 + `GetEpDevices` 顯式選 EP + device policy + CPU fallback)。

### 修正
- a11y:選取圖格補 `aria-pressed` 選取語意;`.af-in` / `.addinput` 輸入框還原全域 `:focus-visible` 焦點環;lightbox 裝飾 svg 補 `aria-hidden`。
- e2e 測試遷移至 `@playwright/test` runner(webServer 自動起 app + 嚴守 web-first 鐵則),取代手寫煙霧腳本。

## [0.1.0] - 2026-06-28

首個公開版本。Phase 1 核心功能完整,以 win-x64 自包含單檔 exe 交付。

### 新增
- 就地掃描索引:SHA-256 身分 / 位置兩層分離、搬移偵測、512px webp 縮圖、EXIF。
- 布林多軸查詢(AND / 排除)+ DAG 標籤閉包 + facet 樹。
- 路徑→tag 匯入後確認規則。
- 資料夾路徑維度瀏覽 `/browse`(即時樹 + 麵包屑 + 下鑽 + 遞迴圖牆)。
- WD14 自動標籤(opt-in,ONNX in-proc,預設 DirectML);tag 顯示層(中文顯示名 + 角色解析 + 來源徽章)。
- 作品軸(copyright 拆分 + facet「作品→角色」DAG 樹)。
- 標籤庫管理(列表 / 改名 / 合併 / 刪除,全 Unicode 不分大小寫去重)。
- 軟刪 + 同 hash 自動復原;孤兒 photo 清理;單張重新處理 + 掃描自動痊癒。
- AVIF / HEIC / HEIF 解碼支援。
- 前端 RWD:桌面縮放韌性 + 側欄收合 + 完整手機版抽屜式側板;a11y(鍵盤導航 / focus ring / ARIA)。
- 大圖檢視 lightbox。
- GitHub Release 自動散布 workflow(打 `v*` tag → 測試 → build → win-x64 單檔 zip)。

[Unreleased]: https://github.com/Alexlin7/picture-management/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Alexlin7/picture-management/releases/tag/v0.1.0
