# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 專案

單一使用者本機**圖片管理系統**(Windows 11)。圖庫十萬量級、以動漫圖為主、少量個人照片。核心:把「邏輯分類(tag)」跟「檔案系統」徹底脫鉤 —— 就地索引、用 tag 與布林查詢看圖,資料夾只是眾多 tag 軸之一。

**完整設計與所有決策理由在 `docs/superpowers/specs/2026-06-21-picture-management-design.md` —— 動手前先讀它。** 本檔只摘大方向與不可違反的鐵則。UI/UX 見該文件 §6 與可點 mockup `docs/mockups/ui-preview.html`(瀏覽器開,`?view=` / `?only=inspector` 可切換截圖)。

**狀態:設計已定案,尚未開始實作(repo 目前只有設計文件)。** 下方「指令」是已議定的工具鏈,實際 build/test 指令待 Phase 1 骨架建好後補上。

## 架構大方向

```
Angular SPA ──REST──> ASP.NET Core API ──┬── Scanner(.NET 背景服務:hash/EXIF/路徑→tag/搬移偵測/縮圖)
                                          ├── Postgres(單一真相)
                                          └── tagging_job 表(DB-as-queue)──> Python worker(WD14 自動標籤,DirectML)
```

- **三元件**:Angular 前端、C#/.NET 後端(含內建掃描器)、Python ML worker。彼此經 Postgres 溝通。
- **單顆 Postgres** 扛布林多軸查詢 + JSONB(EXIF)+ 日後 pgvector(CLIP 語意搜尋,Phase 2)。
- 資料模型七表,身分/位置兩層拆開(`photo` ↔ `photo_location`)。ER 與 DDL 見設計文件 §4。

## 不可違反的鐵則(改動前必讀)

1. **絕不修改、搬動、改名原始圖檔,絕不把 metadata 寫回圖檔(不寫 XMP)。** 原檔一律唯讀 —— PNG 可能藏惡意內容,改檔有風險。衍生資料(縮圖)放 app 自有快取目錄。
2. **`file_hash`(SHA-256)是身分,`file_path` 只是位置。** 搬移/換碟/副本/去重一律靠 `photo_location` 處理,`photo` 身分不動。不要用路徑當主鍵或身分。
3. **DB 是 tag 的唯一真相(無 XMP)。** 因此備份(pg_dump)與可選 manifest 匯出很重要,不要弱化。
4. **刪除是軟刪**(位置標 `archived`,保留 photo+tags;同 hash 回來自動復原)。只有使用者明示才硬刪 purge。
5. **tag 來源要分**:`photo_tag.source` ∈ path/manual/wd14,WD14 帶 `confidence`。不要把自動標籤跟手動策展混為一談。
6. **ML 推論走 ONNX Runtime DirectML**(跨 NVIDIA/AMD,使用者兩台機器不同顯卡),**不要綁 CUDA**。無 GPU 自動退 CPU。
7. **.NET ↔ Python 經 `tagging_job` 表(DB-as-queue),不引入 RabbitMQ/Redis 等 broker。**
8. **單機單人:API 只 bind `localhost`,不做帳號/認證系統。**
9. **路徑→tag 是「匯入後確認」**,確認結果存 `path_tag_rule`(每段只確認一次)。不要改成全自動硬塞。

## 工具鏈(已確認可用,2026-06-21)

| 工具 | 版本 | 用途 |
|---|---|---|
| .NET SDK | `10.0.301` | ASP.NET Core 後端 + 掃描器 |
| Node / npm | `24.15.0` / `11.12.1` | Angular 前端 |
| Docker / compose | `29.5.3` / `v5.1.4` | 跑 Postgres(`pgvector/pgvector` image) |
| Python | `3.14.6` | ML worker(cp314 wheel 全可用) |

ML worker 套件(cp314 已驗):`onnxruntime-directml 1.24.4`、`numpy 2.4.6`、`pillow 12.2.0`、`huggingface_hub 1.20.1`、`psycopg[binary] 3.3.4`。WD14 模型抓 SmilingWolf 的 `wd-vit/swinv2-tagger-v3`(HF ONNX)。

## 交付 / 安裝(方案 C 混合)

- **Postgres 走 Docker**(`pgvector/pgvector`,好裝 pgvector)。
- **.NET API + Python worker 原生跑**(直接讀十萬本機檔、啟動快;避開 Windows bind-mount 慢)。
- 最終一鍵:`.NET publish` 自包含單檔 exe(serve Angular 靜態檔 + 拉起 Python worker)+ PowerShell 啟動腳本。

## 指令(議定的工具鏈;實際指令待骨架建好後補)

```powershell
# DB(Docker)
docker run -d --name pm-pg -e POSTGRES_PASSWORD=... -p 5432:5432 pgvector/pgvector:pg17   # 或 docker compose up -d postgres

# 後端(.NET 原生)
dotnet build ; dotnet test ; dotnet run --project <Api 專案>

# 前端(Angular)
npm ci ; ng serve              # 開發;ng build 產靜態檔給 .NET serve

# ML worker(Python 原生,自有 venv)
py -m venv .venv ; .\.venv\Scripts\Activate.ps1
pip install onnxruntime-directml numpy pillow huggingface_hub "psycopg[binary]"
python -m worker               # 輪詢 tagging_job
```

## 分階段

- **Phase 1(核心)**:schema + 掃描/對帳 + 路徑→tag 確認 + 布林查詢 + Angular 相簿 + 縮圖 + WD14 worker。**不含** embedding/pgvector。
- **Phase 2(語意搜尋)**:CLIP image embedding → pgvector hybrid query。

## 開發約定

- 全程以**繁體中文(台灣用語)** 溝通;程式碼識別子與技術名詞保留原文。
- schema/設計可演進;改動牽涉設計決策時,同步更新 `docs/superpowers/specs/` 設計文件。
