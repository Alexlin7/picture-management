# Agent Guidance

本檔給 Codex / 其他 coding agent 使用。Claude Code 專用說明仍在 `CLAUDE.md`;兩者原則一致,但本檔只保留 agent 執行時最需要的資訊。

## 專案定位

單一使用者本機圖片管理系統(Windows 11)。核心原則是把「邏輯分類(tag)」與「檔案系統」脫鉤:原圖就地索引,以 tag 與布林查詢瀏覽;資料夾只是 tag 軸之一。

完整設計見 `docs/superpowers/specs/2026-06-21-picture-management-design.md`。現況、啟動方式與功能狀態見 `README.md`。

## 不可違反

1. 不修改、搬動、改名原始圖檔,不把 metadata 寫回圖檔。
2. `file_hash` 是身分,`file_path` 只是位置;搬移/副本/去重走 `photo_location`。
3. SQLite 是 tag 唯一真相;不寫 XMP。
4. 刪除預設軟刪,只有使用者明示才 purge。
5. tag 來源必須分清楚:`path` / `manual` / `wd14`,WD14 帶 confidence。
6. ML 推論在 .NET 程序內走 ONNX Runtime,EP 透過 `IInferenceSessionFactory` 抽象;不要硬綁 CUDA。
7. `tagging_job` 是程序內 DB-backed queue;不要引 broker 或第二個常駐程序。
8. 單機單人,API 只 bind `localhost`;若改為遠端/多人,認證變必要。
9. 路徑到 tag 是匯入後確認,不要改成全自動硬塞。

## 工作方式

- 全程使用繁體中文(台灣用語)溝通;程式碼識別子與技術名詞保留英文。
- 動 code 前先讀相關 spec/README,重構或新功能先提出計畫或更新設計文件。
- 小切片實作,每片 build/test 綠後再 commit。
- 多檔變更後至少跑 `dotnet build` + `dotnet test`;前端變更跑 `ng build` 並起 app 手測。
- 後端測試用獨立 SQLite 檔或 `:memory:`,不要假設 Java-style transaction rollback。
- 設計決策有變更時,同步更新 `docs/superpowers/specs/` 與必要的 README/CLAUDE/agent 文件。

## 常用指令

```powershell
dotnet build
dotnet test
dotnet run --project src/Pm.Api

cd src/Pm.Web
npm ci
npm run build
npm start
```

## 文件整理原則

- `README.md`:目前狀態、啟動方式、功能清單。
- `CLAUDE.md` / `agent.md`:大方向、鐵則、工作約定。
- `docs/superpowers/specs/`:尚需保留的設計決策與未完成工作;已完成的實作細節不要堆在這裡造成噪音。
- `docs/superpowers/plans/`:只保留仍有參考價值的切片計畫;已完成計畫應壓縮為短註或由 README/commit history 承接。
