# 檔案偵測 / 掃描策略

- 日期:2026-06-22
- 狀態:背景決策保留;路線 A 已併入 `2026-06-23-scanner-tagging-refactor-design.md`,本檔只保留偵測策略邊界。
- 關聯:`CLAUDE.md` 鐵則 #1/#2。

## 決策

本專案是「就地索引」,不是「投放資料夾吞檔」。

- 原圖不搬、不改名、不刪除。
- 掃描比對磁碟與 SQLite 才是 source of truth。
- `FileSystemWatcher` 只能當程式開著時的輔助觸發,不能取代掃描。

## 已吸收到新 spec 的內容

掃描效能與觸發點已移到 `2026-06-23-scanner-tagging-refactor-design.md`:

- 批次載入 `photo_location` 消除 read N+1。
- 批次 commit,避免快路徑也逐檔 `SaveChanges`。
- 掃描與 tagging job 排程解耦。
- 後續可加開機掃描、掃子資料夾、requeue 指定範圍。

## 保留的後續方向

### `FileSystemWatcher`

定位:加分功能,不是主力。

- 監看 library root,偵測新增/異動/刪除後排入掃描。
- debounce 1-2 秒,避免大量複製時逐檔觸發。
- buffer overflow 或不可信事件時,退回排一次全掃。
- 網路磁碟與特殊檔案系統要容錯;漏事件不能造成資料庫永久錯誤。

### 不採用

- Eagle/Hydrus 式「投放資料夾匯入後搬走或刪來源」。
- NTFS USN journal 暫不做;只有在批次掃描仍不足時再評估。
