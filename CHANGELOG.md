# Changelog

本專案所有重要變更記錄於此。格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/),版本遵循 [SemVer](https://semver.org/lang/zh-TW/)。

## [Unreleased]

### 變更
- **推論後端三 flavor 正式出貨**(原 CUDA / Windows ML「僅骨架」狀態解除):**DirectML**(預設,任何 DX12 GPU,24H2 以下通用)/ **CUDA**(NVIDIA,24H2 以下)/ **Windows ML**(Win11 24H2+,EP 由 OS 動態下載)。切點為 OS 版本涵蓋;編譯期經 `InferenceFlavor` 屬性切 ONNX Runtime 套件 + 選 factory,呼叫端程式碼不動,各有 publish profile + CI release matrix(出三個 zip)。CPU / DirectML 已實機驗證;CUDA / Windows ML 為編譯 + publish 驗證,runtime 推論需對應硬體 / OS。

- **前端設計 token 系統收斂(P1)**:`@theme` 補齊單一真相源並改 `@theme static`(避免 runtime `var()` 引用的 token 被 Tailwind tree-shake)——
  間距 4px scale(`--space-*`)、7 階封閉 type-scale(`--text-*`,散落字級含 .5px 全歸階)、tag 分色改單向引用 `--color-t-*`(刪手抄 hex 表,`hexToRgba`→`color-mix`)、transition 時長收斂兩階(`--dur-fast`/`--dur-base`,散落 ms 就近 snap)、半透明 cyan 抽 5 個 accent 衍生 token(`--color-accent-soft`/`-ring`/`-focus`/`-glow`/`-edge`,`color-mix` 從 `--color-accent` 推導,散落裸 `rgba(34,211,238,…)` 收斂、視覺等價)。
- 抽屜投影子元件改 `:host(.fill)` 自管,移除全部 `::ng-deep`(Angular 已 deprecated)。
- 文字輸入收斂到全域 `.input` primitive:三個各自造輪子的輸入(tag 搜尋/新增、加來源、加標籤）統一掛 class,差異化保留為 `.input.is-mono`/`.input.is-sm` 修飾子;補第四態 `.input:disabled`、`.frow:active`/`:disabled`,選單列補 `:active` 按下回饋。

### 新增
- `IInferenceSessionFactory.InitializeAsync` 暖機接縫(預設 no-op;Windows ML 用於 async 註冊 EP 目錄)。
- `CudaSessionFactory`、真正的 `WindowsMlSessionFactory`(EP 目錄暖機 + `GetEpDevices` 顯式選 EP + device policy + CPU fallback)。

### 修正
- 文案正名:「收藏」與「儲存」對齊,收藏搜尋流程統一用「收藏」(按鈕 / toast / 空狀態);「標籤名」一律改「標籤」+ placeholder 精簡;`+ tag` 英文改「+ 標籤」;inspector EXIF 空狀態不再露 `taken_at` 欄位名。
- a11y:選取圖格補 `aria-pressed` 選取語意;輸入框焦點統一走全域 `:focus-visible` 焦點環;lightbox 裝飾 svg 補 `aria-hidden`。
- 手機 facet 抽屜無法捲動(投影子元件漏設 host 高度)。
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
