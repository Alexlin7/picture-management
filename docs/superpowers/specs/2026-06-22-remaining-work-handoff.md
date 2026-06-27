# Current Backlog Handoff

用途:給下一個 agent session 快速接手。已完成細節不要在這裡展開;現況看 `README.md`,鐵則看 `CLAUDE.md` / `agent.md`,完整背景看 `2026-06-21-picture-management-design.md`。

**最後更新:2026-06-28**(backlog ②③ 一票項目落地後:virtual scroll、tag 合併選方向、/tags 排序持久化、facet top-N/過濾、import 多 root + preset + path-rule bug 修復、requeue 依查詢、browse tile→inspector;6 PR + 1 fix PR 已 merge 進 main)。

## 當前 backlog(尚未做,依建議順序)

1. **掃描器 / ML 後續**
   - ~~**AVIF 解碼支援**~~ ✅ **已完成(2026-06-26)**:新增 `Pm.Imaging` 橋接,HEIF 家族(avif/heic/heif)繞道 **Magick.NET-Q8(libheif)** 解碼成像素再包回 ImageSharp,縮圖/metadata/WD14 全正常;白名單補 `.heic/.heif`。註:Magick-Q8 有 HEIF 解碼但無 HEIC 編碼(x265 授權),本專案只需解碼故無影響。
   - ~~GPU 廠牌自動偵測~~ ❌ **不做(moot,2026-06-27)**:Backend 一律由設定明示(appsettings 出貨帶 `directml`、各機 launchSettings 覆寫),`Select` 永遠走 configured 短路、不觸及 `gpuVendor`;且廠商於 publish 時由套件綁定(DirectML build 跨全廠商、CUDA 走專屬 profile),runtime 偵測廠商無消費者。決議與理由見 `2026-06-23-ml-layer-architecture-assessment.md` §6/§8 與 `Wd14Setup` 註解。
   - `Pm.Ml` 整理為 CLIP 鋪路:✅ 唯一該現在做的 `ModelArtifactDownloader` 抽取**已完成(2026-06-27)**;其餘整理(base class / 泛型 preprocess / 統一介面)依評估**defer 到 CLIP 真實形狀出來**再做(見 `2026-06-23-ml-layer-architecture-assessment.md` §3〜§5)。
   - **第二 tagger(cl_tagger_v2)當開關**:deferred、低優先,評估見 `2026-06-25-second-tagger-cl-tagger-evaluation.md`(非抽換、是新增;需抽 `ITagger`;先確認授權/速度/品質)。
   - per-root「重產縮圖」維護入口(重掃已補缺縮圖,獨立 rebuild-thumbs 屬 nice-to-have)。

2. ~~**圖牆 virtual scroll(真窗格化)**~~ ✅ **已完成(2026-06-28)**:自製 windowing(非 CDK,masonry 不定高)。`Masonry` 加 `scrollEl`/`overscan` signal input,以 `layout` 的 box 幾何 + `masonry-layout.isBoxInWindow` 純函式過濾出視窗 ± overscan 的 tile;`scrollEl` 未設則 fallback 全渲染。gallery / browse 的 `.view` 傳 `[scrollEl]=viewEl` 接入,IntersectionObserver 無限捲不動。實測 60 張只渲染 ~24 張。

3. **小型前端體驗**
   - ~~tag 合併時讓使用者選保留方向~~ ✅(2026-06-28;新 `core/ui/merge-dialog.ts`)。
   - ~~`/tags` 排序狀態持久化到 localStorage~~ ✅(2026-06-28;`tags-sort-persist.ts`)。
   - ~~左側 tag facet 側欄:top-N / 側欄過濾 / 虛擬捲動~~ ✅(2026-06-28;top-N=12 + 過濾框 + 三平面清單。註:rootless 原用 CDK 虛擬捲動,因 overflow 蓋掉內捲被裁切,改成 top-N +「顯示更多」與屬性/年份一致 —— fix PR #11)。
   - ~~import-confirm:多 root 選擇器 / 自訂常用 tag preset~~ ✅(2026-06-28;另**修了 2 個既有 bug**:path-rule action `map`/`year` 與後端詞彙不符→不建 tag、kind 寫死 path。修法見 §下 + `PathTagService.NormalizeAction` / `ApplyExistingRulesAsync` 自我修復 + EF migration)。
   - ~~批次 requeue「依當前查詢 filter」scope~~ ✅(2026-06-28;`PhotoQueryService.GetAllPhotoIdsAsync` + `SearchQueryScopeDto`)。
   - ~~browse `/browse`:tile 點擊接 inspector~~ ✅(2026-06-28;browse-view 補第三欄 + tile 選取高亮)。
   - **尚未做**:
     - 「重標全部」(破壞性高)deferred,待使用者明示。
     - browse 大型資料夾樹折疊 / 排序。
     - **整頁手機版**(③g;抽屜式側欄 / 漢堡選單 / 375px mobile-first 重排)—— effort L,設計計畫已在 2026-06-27 規劃 workflow 產出(CDK BreakpointObserver + 共用 DrawerPanel)。桌面縮放韌性 + 手機 topbar/toolbar 折行已完成(見 README「前端 RWD」)。
     - **a11y 完整檢視**(③h;鍵盤操作、ARIA、焦點環)—— effort L,設計計畫已產出(可點 div→button、combobox ARIA、focus restore,分 critical/high/medium 三波)。

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
- **瀏覽器層 e2e infra**(Playwright):`src/Pm.Web/e2e/browse-smoke.mjs` + `npm run e2e`(真實 app serve + 瀏覽器層 mock `/api`;驗證捲動補頁、切夾無交叉);`rwd-resize-smoke.mjs` + `npm run e2e:rwd`(縮 viewport 斷言無破版 + 欄數遞減,含手機 480/375)。
- **單張重新處理 + 掃描自動痊癒**(2026-06-27):`POST /api/photos/{id}/reprocess`(重新解碼補 metadata + 強制重產縮圖 + refresh WD14)+ `ImageReprocessor`;重掃自動痊癒 `width=NULL` 半殘圖(AVIF 解碼上線前索引的舊檔);inspector「重新處理」鈕 + 完成後縮圖即 cache-bust 刷新。
- **前端 RWD**(2026-06-27):共用 `<app-masonry>`(JS 量測欄寬、aspect 驅動、最少 1 欄)+ facet/inspector/資料夾樹側欄可收合(手動 + 依斷點自動)+ 手機 topbar/toolbar(≤640px 折行 + 「⋯ 更多」溢出選單)。
- **backlog ②③ 一票(2026-06-28,6 PR + fix PR #11,全進 main)**:masonry virtual scroll(windowing)、tag 合併選保留方向(`merge-dialog`)、`/tags` 排序持久化、facet top-N + 過濾框、import 多 root + 常用 tag preset、requeue 依當前查詢(後端 Query scope)、browse tile→inspector。**並修了 import-confirm 兩個既有 bug**:① path-rule action `map`/`year` 與後端 `map_to_tag`/`meta_year` 不符 → map 規則靜默不建 tag;② 建 tag 時 kind 寫死 `path`,前端選的分類失效。修法:`PathTagService.NormalizeAction` + `kind` 參數 + `ApplyExistingRulesAsync` 自我修復(補建 `TagId=null` 的歷史規則)+ EF migration `NormalizePathRuleActions`(真實 dev DB 實證:15 條 `map` 規則 tag_id 全 NULL,已正規化)。
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
