---
status: active
last-reviewed: 2026-06-29
supersedes: []
superseded-by: []
related: [2026-06-24-async-scan-design]
---

# 孤兒 photo 清理維護端點 — 設計

- 日期:2026-06-25
- 狀態:**已實作(2026-06-29 複查確認)** —— `GET` / `DELETE /api/maintenance/orphan-photos` + 啟動孤兒數 log 已落地。
- 範圍:後端維護端點,清掉「零 location」的孤兒 photo —— async scan 舊 bug 殘留(防新孤兒已修)。純後端,本輪無前端 UI。
- 來源:孤兒 photo 清理(防新孤兒已做,清舊孤兒走手動維護端點)、`2026-06-24-async-scan-design.md`(事故後殘留如 id 4911)。
- 鐵則對照:守 #2(`file_hash` 是身分)、#4(刪除是軟刪,**只有使用者明示才硬刪 purge**)。孤兒清理是 purge,以「GET 預覽 → 明示 DELETE」作為明示閘門。

---

## 一、問題與定義

- **孤兒 photo 的精確定義**:`photo` 有身分(hash + 可能有 tag)但**零筆 `photo_location`**(`db.Photos.Where(p => !p.Locations.Any())`)。
- 與「失聯 photo」不同:失聯 = 有 location 但全部非 `present`(`p.Locations.Any() && p.Locations.All(l => l.Status != "present")`,由 `GET /api/reconcile/missing` 處理)。孤兒是**完全沒有 location 記錄**。
- **根因(已修)**:async scan 曾在 photo 已寫、location 未寫時中斷 → 孤兒。`LibraryScanner.ProcessPendingAsync` 已用同 transaction 包 photo+location+job 寫入,中斷一起 rollback,不再產新孤兒。**舊孤兒仍殘留 DB,需手動清。**

## 二、設計

### 2.1 端點(`src/Pm.Api/Program.cs`,新 `.WithTags("Maintenance")`)

**(a) 預覽(先看再刪)**
```
GET /api/maintenance/orphan-photos
→ 200 { count: int, ids: long[] }
```
查 `db.Photos.Where(p => !p.Locations.Any())`,回數量 + id 清單。唯讀。

**(b) 清除(硬刪 purge)**
```
DELETE /api/maintenance/orphan-photos
→ 200 { purged: int, thumbsDeleted: int }
```
- 撈孤兒 → `db.Photos.RemoveRange(orphans)` → `SaveChangesAsync`。EF cascade(FK `OnDelete=Cascade`)**自動連帶刪** `photo_location` / `photo_tag` / `tagging_job`。
- 縮圖**不在 cascade 內**,需逐筆刪:對每個孤兒 `var p = thumbs.PathFor(hash); if (File.Exists(p)) File.Delete(p);`(`thumbsDeleted` 累計實際刪除數;不存在不算、不拋)。**順序:先刪縮圖檔再刪 DB 列**(或先收集 hash 再刪),避免刪了 DB 拿不到 hash。
- 回 `{ purged = 刪除的 photo 數, thumbsDeleted }`。

### 2.2 (可選)啟動時孤兒數 log

啟動時(`Program.cs` Migrate 後)以 `ILogger` log 一次孤兒數量(`logger.LogInformation("孤兒 photo 數:{Count}", n)`),**只 log、永不自動刪**。剛好接上新做的 Serilog,出事看 log 即知有無孤兒堆積。失敗(查詢例外)吞掉不擋啟動。

### 2.3 鐵則對齊(硬刪的明示閘門)

- 鐵則 #4:刪除是軟刪,只有明示才硬刪 purge。孤兒**無任何 live location**,是 bug 殘留、非使用者策展內容;「GET 預覽 → 明示打 DELETE」即為明示動作。
- **取捨(spec 明記)**:purge 是硬刪,若該檔之後回到圖庫,scan 會以 hash 重新索引成**乾淨新 photo**(孤兒原本那點 tag 會失去)。孤兒本就是失敗掃描的殘留、`source` 多為空或半套,可接受。

## 三、測試(後端 TDD,temp SQLite)

參照 `PhotoMutationApiTests`(purge cascade)與 `ReconcileApiTests`(維護端點)模式,`WebApplicationFactory<Program>` + temp SQLite seed:

1. **預覽**:seed 1 筆無 location 的 photo + 1 筆有 present location 的 photo → `GET` 回 `count=1`、`ids` 只含孤兒。
2. **清除 cascade**:孤兒帶 photo_tag + tagging_job + 縮圖檔 → `DELETE` 後該 photo 與其 tag/job 全消(`Photos.Count()` 不含孤兒)、縮圖檔不存在、`{purged:1, thumbsDeleted:1}`。
3. **不誤刪**:有 present location 的 photo **不受影響**(仍在)。
4. **冪等**:無孤兒時 `DELETE` 回 `{purged:0, thumbsDeleted:0}`、不拋。
5. （可選）啟動 log:若做 2.2,以日誌斷言或手測確認。

> seed 模式:`_factory.Services.CreateScope()` 直接寫 DB;縮圖檔用 `ThumbnailOptions.Dir` 下建一個假 webp 驗刪除。

## 四、不在範圍

- **前端 UI 入口**:本輪純後端端點(經 Scalar / 手動呼叫)。前端維護頁/按鈕屬後續 nice-to-have。
- **失聯(missing)photo 清理**:已有 `reconcile/missing`,非本案。
- **自動清除**:永不自動硬刪;一律使用者明示。

## 五、決策日誌

- **走手動維護端點(非啟動自動清)**:守鐵則 #4;啟動最多 log 數量。
- **GET 預覽 + DELETE 執行兩端點**:硬刪前可先看,作為「明示」閘門。
- **縮圖另刪**:cascade 不含檔案系統;先取 hash 再刪 DB,避免拿不到路徑。
- **purge 不可復原的取捨**:孤兒是 bug 殘留,接受「檔案回來則重新乾淨索引」。
