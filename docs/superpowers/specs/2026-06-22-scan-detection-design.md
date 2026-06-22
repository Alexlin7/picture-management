# 檔案偵測 / 掃描策略 — 設計 note(代辦 / backlog)

- 日期:2026-06-22
- 狀態:**代辦(尚未排程實作)** —— 決策方向已定,細節與時程待後續
- 關聯:`CLAUDE.md` 鐵則 #1/#2(絕不搬動/改名原檔、就地索引、hash 是身分);
  現有實作 `src/Pm.Scanner/LibraryScanner.cs`

## 問題

硬碟新增/搬移檔案後,系統要怎麼偵測並索引?目前**只有一種**方式:
手動打 `POST /api/roots/{id}/scan` → **全局掃描**(走訪 root 下全部檔,快路徑跳過沒變的)。
缺點:十萬量級時,每個檔都單獨查一次 DB(`LibraryScanner.cs:45`),全掃偏慢;
且沒有開機自動掃、沒有即時偵測、沒有「只掃某子資料夾」。

## 調研:主流工具怎麼做(2026-06-22)

關鍵發現 —— 工具分兩個陣營,**本專案屬「就地索引」陣營**:

| | 管理庫陣營 | **就地索引陣營(本專案)** |
|---|---|---|
| 代表 | Eagle、Hydrus | digiKam、Lightroom Classic |
| 檔案 | 複製/搬進 app 自有庫,**原檔常被吃掉/刪除** | **就地引用,原檔不動** |
| 偵測 | 監看「投放資料夾」吞檔 / 排程匯入 | **掃描比對(磁碟 vs 資料庫)** |

- **Eagle auto-import**:設一個專用資料夾,丟檔進去會被**搬進 `.library` 並從原處消失**。
- **Hydrus import folders**:排程定時掃指定資料夾,匯入後**可選刪除來源**,複製進自有 hash 檔庫。
- **→ 這兩種「投放夾吞檔」模式直接違反鐵則 #1/#2,不可照抄。**
- **digiKam**(與本專案最像:就地 + **hash+size 當身分**):「Scan for New Items」手動 +
  可選「開機掃描」+ 背景掃 + **「Fast scan」只找新增/刪除/改名**;官方建議檔案操作盡量在 app 內做。
- **Lightroom**:純手動「Synchronize Folder」比對磁碟 vs 目錄 → 匯入新 / 移除遺失 / 檢查 metadata。

**結論:就地陣營主流都靠「掃描比對」(手動 + 開機 + 快掃),幾乎沒人拿即時檔案監看當主力** ——
因為 watcher 抓不到「程式沒開時」的變動,掃描才是 source of truth。本專案照 digiKam 劇本走。

## 決策方向

**抄 digiKam 模型,不抄 Eagle/Hydrus 的投放夾(會搬原檔)。** 分兩條路線,建議先 A:

### 路線 A(輕量、優先)— 把掃描做快 + 補觸發點
1. **批次載入消除 N+1**:開掃前一次把該 root 的所有 `photo_location` 載進記憶體 dict,
   迴圈內查 dict(O(1)),取代每檔一次 DB query。預期十萬檔全掃從「分鐘級」降到「秒~十幾秒」。
2. **開機自動掃描**(可選設定,類比 digiKam「Scan at startup」),預設可關,避免拖慢啟動。
3. **手動「掃這個子資料夾」**:拷一大批新檔到某子目錄時,只掃該目錄,免走全庫。
4. (可考慮)digiKam 式 fast-scan:只比對「新增/刪除/改名」,進一步省事。

### 路線 B(加分、次要)— 即時偵測
- `FileSystemWatcher` 監看 root,程式開著時自動偵測新檔 → 排掃描/標籤。
- 要點:**debounce**(拷一批檔時等安靜 1~2 秒再處理)、**buffer 溢位退回觸發全掃**(別漏)、
  網路碟/特殊檔系統不穩需容錯。
- **定位為「程式開著時的加分糖」,非主力**;全掃永遠是保險與 source of truth。

### 明確不做
- Eagle/Hydrus 式「投放資料夾吞檔 / 匯入後刪來源」—— 違反鐵則,永不採用。
- NTFS USN 變更日誌:威力大但綁 NTFS、API 複雜、需權限 —— 僅「路線 A 後全掃仍慢到受不了」才考慮。

## 對齊鐵則

- 不搬動/改名/修改原檔;掃描純讀取 + 就地索引。
- hash 是身分(已實作);偵測只決定「哪些檔要(重)算 hash」,不改變身分模型。

## 後續(實作時再細化)

- 路線 A 各項的介面與設定鍵(如 `Scan:OnStartup`)、`ScanResult` 既有 `jobsQueued`/`skipped` 可沿用觀測。
- 批次載入後的記憶體用量(十萬 location 的 dict)評估。
- 是否提供「掃描進度」回報(目前 `/scan` 同步回 `ScanResult`,大庫可能要改非同步 + 進度查詢)。

## 參考

- Eagle 匯入/匯出:https://en.eagle.cool/blog/post/how-to-import-export-in-eagle
- Hydrus Importing and Exporting:https://hydrusnetwork.github.io/hydrus/getting_started_importing.html
- digiKam Scan for New Items:https://docs.digikam.org/en/maintenance_tools/maintenance_newitems.html
- Lightroom Synchronize Folder:https://www.lightroomqueen.com/community/threads/the-logic-of-the-sync-folder-in-lightroom-classic.50726/
