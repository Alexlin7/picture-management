# Current Backlog Handoff

用途:給下一個 agent session 快速接手。已完成細節不要在這裡展開;現況看 `README.md`,鐵則看 `CLAUDE.md` / `agent.md`,完整背景看 `2026-06-21-picture-management-design.md`。

**最後更新:2026-06-26**(PR #5 review 後續修復 F1–F10 全清、瀏覽器 e2e infra、win-x64 自包含單檔 publish 實測就緒後)。

## 當前 backlog(尚未做,依建議順序)

1. **掃描器 / ML 後續**
   - **AVIF 解碼支援(新,2026-06-25 發現)**:`.avif` 在掃描白名單(`LibraryScanner.cs`)會被當圖建 photo + 算 hash,但 **ImageSharp 3.x 不支援 AVIF 解碼** → 縮圖、metadata(mime/尺寸)、WD14 前處理全失敗(photo 半殘:查得到 hash 但無縮圖/無尺寸/無自動標)。解法:加 avif 解碼(`Magick.NET` / `SkiaSharp` / libheif binding),或從白名單拿掉 `.avif`。avif 只會變多,建議真正支援。
   - GPU 廠牌自動偵測(目前 `InferenceBackendSelector` auto 傳 `gpuVendor=null`,固定 DirectML)。
   - `Pm.Ml` 整理為 CLIP 鋪路(`2026-06-23-ml-layer-architecture-assessment.md`)。
   - **第二 tagger(cl_tagger_v2)當開關**:deferred、低優先,評估見 `2026-06-25-second-tagger-cl-tagger-evaluation.md`(非抽換、是新增;需抽 `ITagger`;先確認授權/速度/品質)。
   - per-root「重產縮圖」維護入口(重掃已補缺縮圖,獨立 rebuild-thumbs 屬 nice-to-have)。

2. **圖牆 virtual scroll(真窗格化)**
   - 現況:gallery 與 browse 都接無限捲(IntersectionObserver),但 DOM 隨載入累積。
   - 下一步:評估 `@angular/cdk/scrolling` 或自製 windowing;masonry 不定高較麻煩。

3. **小型前端體驗**
   - tag 合併時讓使用者選保留方向。
   - `/tags` 排序狀態持久化到 localStorage。
   - 左側 tag facet 側欄:top-N / 側欄過濾 / 虛擬捲動(`2026-06-25-tag-sidebar-and-import-confirm-review.md`;分區整段收折已做)。
   - import-confirm:多 root 選擇器 / 自訂常用 tag preset(同 review 文件待決)。
   - 批次 requeue「依當前查詢 filter」scope(需後端 by-query scope)。
   - 「重標全部」(破壞性高)deferred,待使用者明示。
   - browse `/browse`:tile 點擊接 inspector(`BrowseStore.select` seam 已留、目前 no-op);大型資料夾樹折疊 / 排序。
   - 響應式與手機版仍未設計;目前以桌面 workbench 為主。
   - a11y 尚未完整檢視(鍵盤操作、ARIA、焦點環)。

4. **交付與 Phase 2**
   - CUDA / Windows ML backend 仍是骨架(本 build 僅 cpu/directml)。
   - GitHub Release 自動散布(tag → CI build zip → release asset)尚未設定,骨架見 `docs/deployment.md` §9。
   - CLIP 語意搜尋未開始(Phase 2)。

## 已完成,不要重做

- **Phase 1 核心**:SQLite schema、掃描/對帳、縮圖、路徑→tag、查詢、facet、saved search、軟硬刪、manual tag、標籤庫。
- **Angular 前端**:gallery / import / reconcile / saved / roots / tags / inspector + **browse `/browse`** 已接真實 API;SPA 由 .NET serve。
- **WD14**:ONNX in-proc pipeline、TaggingWorker、opt-in host wiring、DirectML 實機驗證。
- **§B 掃描/Tagging 解耦**、**WD14 tag 顯示層 v1**、**UI 樣式系統 Spec 1**、**gallery-topbar UX Spec 3**、**gallery 改進**(真實命中數/WD14 佇列/URL 狀態/無限捲)。
- **async scan + SQLite 硬化**、**縮圖佔位 + 新增來源自動掃描**。
- **logging + app data dir**(Serilog rolling file 落 `%LOCALAPPDATA%`;EF SQL log 壓 Warning、級別走 `appsettings:Logging:LogLevel`)。
- **孤兒 photo 清理**(維護端點 + 啟動只 log + 全 app FK cascade)。
- **作品軸(copyright axis)**(WD14 copyright 拆分 + `tag_relation` + facet 側欄)。
- **資料夾路徑維度 `/browse`**(即時樹 + 麵包屑 + 子夾下鑽 + 遞迴圖牆 + 夾內疊 tag;PR #5,2026-06-25)。
- **PR #5 review 後續修復**(2026-06-26):`/code-review high` 10 條 findings F1–F10 全部驗證 + 修復(切夾競態 gen guard、`LogLevels.Parse` fallback 修正、懸空 photo_tag 略過、自動分頁停擺、裸 hex token 化、`splitTokens`/`hexToRgba`/`ApplyFolderScope` 共用抽取);詳見 `2026-06-26-pr5-folder-browse-review-and-test-plan.md`。
- **瀏覽器層 e2e infra**(Playwright):`src/Pm.Web/e2e/browse-smoke.mjs` + `npm run e2e`(真實 app serve + 瀏覽器層 mock `/api`;驗證捲動補頁、切夾無交叉)。
- **win-x64 自包含單檔 publish**:`-p:PublishProfile=win-x64`(`Properties/PublishProfiles/win-x64.pubxml`)+ 實跑驗證(serve 前端/API/SQLite 自建/資料落 `%LOCALAPPDATA%`);完整指南 `docs/deployment.md`。

## 固定決策

- UI 行為庫用 Angular CDK;視覺維持自刻,不引整套 Material/PrimeNG。
- tag CI 去重靠 `name_ci`;顯示拼寫保留。
- tag kind 跨來源採語意升級不降級;標籤庫明示修改可覆寫。
- WD14 顯示清理只動前端 display model,不改 SQLite canonical tag。
- 掃描永遠純讀取與就地索引;不引入會搬動原圖的投放夾模式。
- 新增來源後自動掃描(非破壞性、預期下一步),不用 confirm 對話框擋。
- **資料夾瀏覽維度**:即時樹讀 `rel_path` 不落表;遞迴顯示與計數(含子夾、distinct present);path tag 維持服務搜尋維度(兩維度正交;見 folder-browse spec)。
- **log 級別是行為層 knob**,走 `appsettings` 的 `Logging:LogLevel`(Default + per-category override),改 json + 重啟生效、不硬編。
