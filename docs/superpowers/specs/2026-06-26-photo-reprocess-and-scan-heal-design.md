# 單張重新處理 + 掃描自動痊癒設計

**日期**:2026-06-26
**狀態**:設計待實作
**前置**:`feat/avif-decode`(PR #7,已 merge)—— `Pm.Imaging.ImageLoader` facade 讓 metadata reader / 縮圖 / WD14 前處理都能解 AVIF/HEIC/HEIF。

## 1. 背景與問題

AVIF 解碼上線後,**新掃**的 `.avif` 一切正常。但在解碼器上線**之前**就已索引的圖會留下「半殘」狀態:`photo.width / height / mime` 皆 NULL、無縮圖、無 WD14 tag(當初解碼失敗所致,實例 photo 176)。

關鍵問題:**單純重掃修不回這些半殘圖**。`LibraryScanner` 的快路徑(`src/Pm.Scanner/LibraryScanner.cs:66`)在「size+mtime 沒變」時跳過該檔,且補縮圖只在 `PhotoWidth is not null` 才做:

```csharp
if (locInfo.PhotoWidth is not null)
    thumbsGen += await GenerateThumbIfMissingAsync(file, locInfo.PhotoFileHash, ct);
skipped++;
continue;
```

`width=NULL` 的半殘圖剛好落在這道閘之外 —— 不補縮圖、不重抽 metadata、不排 tagging,永遠卡住。

更廣義地,「圖有了但縮圖缺/壞」會在四種情境發生:

| 情境 | photo 有 width? | 現在重掃會自動補? |
|---|---|---|
| A. 縮圖產生當下臨時失敗(IO/檔鎖/批次中斷) | ✅ | ✅ 會 |
| B. 縮圖快取被清掉/壞掉 | ✅ | ✅ 會 |
| C. 索引時解碼器不支援(舊 AVIF 半殘) | ❌ NULL | ❌ **不會** |
| D. 縮圖規格改了(MaxEdge 變、想重產) | ✅ | ❌ 不會(檔在就跳過) |

A/B 已被現有重掃 cover。本設計補上 **C(自動)** 與 **C/B/D(單張手動)**。

## 2. 目標 / 非目標

### 目標
- 重掃時自動痊癒 `width=NULL` 的已索引圖(情境 C),不破壞「十萬量級快速跳過正常圖」的快路徑哲學。
- 提供單張「重新處理」的就地手動入口(情境 B/D,以及 C 的漏網),長在使用者發現問題的地方(Inspector)。
- 自動與手動共用同一套「把這張圖重處理到好」的核心邏輯,行為一致、邏輯只有一份。

### 非目標(YAGNI)
- **不做 per-root /全域「批次重建縮圖」按鈕或維護頁。** 單一使用者本機 app,縮圖快取是實作細節,不該是頂層功能;批次需求由重掃(自動)+ 單張(手動)覆蓋。
- **不做瀏覽時 lazy 補縮圖**(thumb 端點 on-miss 即時產)。本次靠重掃 + 單張手動就夠;lazy heal 是日後可選增強。
- **不引入會重試「真壞檔」過多的精密狀態**(見 §4.2 的取捨:接受每次掃描重試極少數 width=NULL 圖)。

## 3. 架構:一個核心 + 兩個觸發點

```
            ┌──────────────────────────────────────────┐
            │  ImageReprocessor(Pm.Scanner)            │
            │  重新解碼原圖 → 更新 width/height/mime/exif │
            │  → 強制重產縮圖(覆蓋)→ 回報 decoded?     │
            │  不動 hash 身分、不碰原圖、不動 tag         │
            └──────────────────────────────────────────┘
                  ▲                              ▲
   (自動)         │                              │   (手動)
 LibraryScanner 快路徑                  POST /api/photos/{id}/reprocess
 width=NULL → 呼叫 + enqueue job         呼叫 + TaggingScheduler.refresh
```

**設計重點:re-tag 不進核心單元。** 核心只管「影像層」(解碼/metadata/縮圖)。re-tag 各觸發點各自接既有設施,避免 `Pm.Scanner` 反向依賴 `Pm.Api` 的 `TaggingScheduler`:

- 手動端點(`Pm.Api`)= `ImageReprocessor` + `TaggingScheduler.ScheduleAsync("refresh", …)`(清 wd14 + 重排,涵蓋已有 tag 的圖)。
- 掃描痊癒(`Pm.Scanner`)= `ImageReprocessor` + 直接 enqueue 一筆 `tagging_job`(width=NULL 圖本來就沒 wd14 tag,無須先清)。

## 4. 元件設計

### 4.1 `ImageReprocessor`(新增,`Pm.Scanner`)

單一職責:把某張既有 photo 的影像衍生資料重建到好。

介面(示意):
```csharp
public sealed record ReprocessResult(bool Decoded, bool ThumbGenerated);

public interface IImageReprocessor
{
    // photo:已存在的身分;absPath:該 photo 任一 present location 的絕對路徑。
    Task<ReprocessResult> ReprocessAsync(Photo photo, string absPath, CancellationToken ct);
}
```

行為:
1. 走 `Pm.Imaging.ImageLoader`(經現有 `IImageMetadataReader`)重讀 `width/height/mime/exif`,寫回 `photo`。
2. **強制重產縮圖**(覆蓋既有檔,非 `GenerateThumbIfMissing`)—— 情境 D(規格變)、B(壞檔)要能蓋過舊的。
3. 解碼失敗(真壞檔)→ 不寫尺寸、不產縮圖,回 `Decoded=false`,由呼叫端決定回饋。
4. 全程**不動 hash 身分、不碰原圖、不動任何 tag**(鐵則 1/2)。

依賴:現有 `IImageMetadataReader`、`IThumbnailService`、`PmDbContext`。`ThumbnailService.GenerateAsync` 已是「寫 tmp → move」,需確認 move 會覆蓋既有檔(force 語意)。

### 4.2 掃描自動痊癒(`LibraryScanner` 快路徑)

把快路徑那道補縮圖閘改寫:`width is not null` 維持原樣(只補缺縮圖);**`width is null` → 呼叫 `ImageReprocessor`**,並在 `enqueueTagging` 時 enqueue 一筆 `tagging_job`。

```
快路徑(size+mtime 沒變):
  if width != null:   GenerateThumbIfMissing            # 情境 A/B,維持
  else:               ImageReprocessor.Reprocess
                      if Decoded && enqueueTagging: 加 tagging_job   # 情境 C 自動痊癒
  skipped++ (或計入新的 healed 計數)
```

**取捨(已與使用者確認採此案):** 每次掃描都對 `width=NULL` 的圖重試解碼。
- 保留:快路徑照樣跳過 99.99% 正常圖(省的是 hash/縮圖),完全不破壞「快速跳過」哲學。
- 代價:極少數「真壞檔/真不支援」每次掃描被重試一次解碼。十萬圖庫裡這類通常個位數,成本可忽略。零狀態、零版本邏輯。日後若壞檔暴增再引入 `decode_attempted` 標記(本次 YAGNI)。

`ScanResult` 新增一個計數欄位(如 `Healed`)以利觀測;`LibraryScanner` 既有便利建構子需能取得 `IImageReprocessor`(或在 slow-path 同樣注入)。

### 4.3 手動端點 `POST /api/photos/{id}/reprocess`(新增,`Pm.Api`)

orchestration:
1. 取 photo + 一個可讀的 present location 絕對路徑;查無 → 404。
2. `ImageReprocessor.ReprocessAsync`(無條件強制,即使 width 已存在 → 涵蓋 B/D)。
3. `TaggingScheduler.ScheduleAsync("refresh", new RequeueScopeDto(PhotoIds: [id]))`(清舊 wd14 + 重排,語意對齊原「重標」)。
4. 回傳更新後的 photo + `{ decoded, thumbGenerated }`,前端據此刷新 + 顯示失敗回饋。

無 present location(全 missing/archived)時回 409 或帶訊息的結果(前端提示「找不到可讀檔案」)。

### 4.4 前端 Inspector 動作列

**砍掉「重標」**(`retag('refresh')`)—— 它被「重新處理」完全涵蓋(reprocess 第 3 步就是 refresh)。新增 photo 層級動作列,放在**檔名下方**(語意:對「這張圖」而非對 tag),取代目前塞在「標籤」分區標題的兩顆 `.mini`:

```
┌ Inspector ──────────────────┐
│  [ 預覽圖 ]                  │
│  filename.avif              │
│  ┌ 動作 ───────────────────┐│
│  │ [↻ 重新處理] [清除自動標] ││
│  └─────────────────────────┘│
│  身分 → 位置 ───────────     │
│  標籤 ──────────────────     │
└─────────────────────────────┘
```

- **重新處理**(主):呼叫 `POST /api/photos/{id}/reprocess`,async 期間 disable + spinner(沿用既有 `retagging()` pattern);完成後刷新預覽/尺寸/tag。
- **清除自動標**(次/destructive):沿用既有 `retag('clear')`,視覺次級;可復原(重新處理會重加),故不需確認對話框,但樣式上與主動作區隔。
- **失敗回饋**:`decoded=false` → 動作列下方一行 inline 訊息(如「無法解碼這張圖 —— 可能損毀或格式不支援」),不用 alert/confirm(專案 a11y + 不觸發 modal 慣例)。
- 樣式遵循前端慣例:`.btn` 共用 primitive / token,不裸 hex;SVG icon 不用 emoji;`:focus-visible` ring 不蓋。
- 依據 UX 通則:`primary-action`(一主一次)、`overflow-menu`(兩鍵不需 overflow)、destructive 視覺分離。

## 5. 資料流

```
手動:Inspector「重新處理」
  → POST /api/photos/{id}/reprocess
  → ImageReprocessor(解碼+metadata+force thumb)
  → TaggingScheduler.refresh(清 wd14 + upsert job)
  → 回 {photo, decoded, thumbGenerated}
  → 前端刷新;TaggingWorker 之後消化 job 補回 wd14 tag

自動:重掃 root
  → LibraryScanner 快路徑遇 width=NULL
  → ImageReprocessor + (enqueueTagging ? 加 tagging_job)
  → ScanResult.Healed++
```

## 6. 失敗處理

- 解碼仍失敗(真壞檔):核心回 `Decoded=false`;手動 → inline 訊息;自動 → 不 enqueue、計數不增、不拋例外(掃描續跑,沿用既有 `errors` 計法或靜默)。
- 縮圖寫入失敗:沿用 `GenerateAsync` 既有錯誤處理(tmp + move,失敗下次補)。
- 端點找不到可讀 location:回明確錯誤,前端提示。

## 7. 測試

- `ImageReprocessor`:給一張可解碼圖 → 尺寸/mime 寫回 + 縮圖檔產生;給壞檔 → `Decoded=false` 不產縮圖。force 覆蓋:既有縮圖被改寫。
- `LibraryScanner`:既有 width=NULL photo + 檔未變 → 重掃後 width/mime 補上、縮圖生成、(enqueueTagging 時)有 job;`Healed` 計數正確。既有 width!=NULL 正常圖 → 仍走快路徑跳過,行為不變(回歸)。
- 端點 `POST /reprocess`:200 帶 decoded/thumbGenerated;404 不存在;無可讀 location 的錯誤路徑。
- 不動 manual/path tag:reprocess 後手動/路徑 tag 不變(只 wd14 被 refresh)。
- 前端:Inspector 動作列渲染、disable/spinner、失敗 inline 訊息(覆蓋有限,以手測為主)。

## 8. 鐵則對照

- **1 原圖唯讀**:只讀解碼,不改/不搬/不寫回 metadata;不靠改 mtime 逼慢路徑。
- **2 hash 即身分**:reprocess 不動 hash,photo id 不變。
- **4 軟刪**:本設計不涉刪除。
- **5 tag 來源分**:只 refresh `wd14`;`manual`/`path` 不動。
- **10 FK cascade**:不涉硬刪;無新增懸空路徑。

## 9. 開放點(實作時定)

- `清除自動標` 究竟放新動作列(本設計)還是留標籤分區 —— 採動作列(兩個 photo 維護動作集中)。
- `ScanResult.Healed` 命名 / 是否併入既有計數欄。
- `ImageReprocessor` 注入進 `LibraryScanner` 的方式(建構子 vs 便利建構子預設)。
