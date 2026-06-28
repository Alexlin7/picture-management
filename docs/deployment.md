# 部署 / 發版指南

> 操作型文件(會隨工具鏈演進更新)。設計理由見 `CLAUDE.md`「交付 / 安裝」與 spec §2。
> 主力交付:**win-x64 自包含單檔 exe**(免裝 .NET runtime,雙擊即跑)。實測於 2026-06-26。

---

## TL;DR

```powershell
# 1. 先 build 前端(產靜態檔到 src/Pm.Api/wwwroot)
cd src/Pm.Web ; npx ng build

# 2. 自包含單檔發版(win-x64)
cd ../.. ; dotnet publish src/Pm.Api -p:PublishProfile=win-x64

# 產物在:src/Pm.Api/bin/Release/net10.0/publish/
#   Pm.Api.exe(~75MB,含 runtime + 原生 DirectML/ONNX/SQLite)+ wwwroot/ + appsettings.json
```

雙擊 `Pm.Api.exe` → 自動 serve 前端 + 開 API → 瀏覽器進 `http://localhost:<port>`。

---

## 1. 前置需求

| 角色 | 需要什麼 |
|---|---|
| **終端使用者** | **無**。self-contained 已把 .NET runtime 包進 exe,不必裝任何東西 |
| 開發 / 出版者 | .NET SDK `10.0.301`、Node `24` / npm(`ng build`) |

> 沒有「執行期自動下載 runtime」這種事 —— runtime 是**編進 exe** 的。唯一執行期會下載的是 **WD14 模型**(見 §6)。

## 2. 發版步驟

1. **build 前端**:`cd src/Pm.Web && npx ng build` —— Angular 產靜態檔,輸出落點已設定為 `src/Pm.Api/wwwroot`(被 .NET serve)。
2. **publish 後端**:`dotnet publish src/Pm.Api -p:PublishProfile=win-x64`。
   - profile 在 `src/Pm.Api/Properties/PublishProfiles/win-x64.pubxml`,固定了:
     `SelfContained` / `PublishSingleFile` / `IncludeNativeLibrariesForSelfExtract` / `EnableCompressionInSingleFile` / `PublishReadyToRun`,**不開 trimming**(EF Core 反射相依,trim 易壞)。
   - RID/單檔設定只在此 profile 套用,**不影響** `dotnet build` / `dotnet test`。

## 3. 產物內容(交付物)

```
publish/
├─ Pm.Api.exe          # ~75MB:.NET runtime + 你的程式 + 原生 DirectML/onnxruntime/e_sqlite3(全嵌入)
├─ wwwroot/            # ~1.3MB:Angular 靜態檔(★ 不會被塞進 exe,務必一起交付)
├─ appsettings.json    # 設定(見 §4)
├─ appsettings.Development.json   # 可刪(release 用不到)
├─ web.config          # IIS 用,自 host 用不到,可刪
└─ *.pdb / Pm.Api.xml  # 除錯符號 / API 文件,可刪(見下)
```

- **總包 ~77MB**(2026-06-26 實測)。
- **真正要給人的最小集** = `Pm.Api.exe` + `wwwroot/` + `appsettings.json`。
- 想更乾淨(去掉 `.pdb`):csproj 或 profile 加 `<DebugType>none</DebugType><DebugSymbols>false</DebugSymbols>`(僅省約 120KB)。

> ⚠️ **single-file 的真相**:`wwwroot` 是 exe **旁邊的資料夾**,不在那一顆 exe 裡(ASP.NET 靜態資產的限制)。所以「一顆 exe 走天下」要打折 —— 實務上**整個 publish 夾打包成 zip** 才是交付單位。

## 4. 設定(appsettings.json / 環境變數)

執行期落點集中在 `StoragePaths.cs`。關鍵旋鈕:

| 設定 | 預設 | 說明 |
|---|---|---|
| `Storage:BaseDir` | (未設)→ Production 落 `%LOCALAPPDATA%\sus-picture-management` | **資料根目錄**(DB / 縮圖 / log / 模型)。設了就用它、無視環境。攜帶版填**絕對路徑**最穩 |
| `Urls`(或 `--urls` / `ASPNETCORE_URLS`) | ASP.NET 預設 | 監聽位址,例 `http://localhost:5180`。release 建議寫進 appsettings 或用啟動參數(launchSettings **只在 dev 生效**) |
| `Inference:Wd14:Enabled` | 見 appsettings | 是否載 WD14 自動標籤(關著就不下載模型、不吃 GPU) |
| `Logging:LogLevel:*` | Information / EF=Warning | log 級別(`None`=該類別靜默;見 `LogLevels.cs`) |

範例(攜帶版,資料跟 exe 放一起):
```jsonc
{
  "Urls": "http://localhost:5180",
  "Storage": { "BaseDir": "D:\\PmApp\\data" },
  "Inference": { "Wd14": { "Enabled": true } }
}
```

> 兩個「目錄」別混:**程式放哪**(你解壓到哪 / installer 裝哪)與**資料放哪**(`Storage:BaseDir`)無關。預設程式與資料分離(資料在 `%LOCALAPPDATA%`),這是 Windows 正規做法;攜帶版才讓資料貼著 exe。

## 5. 兩種散布形狀

| | zip(攜帶版,xcopy 風格) | installer(方便版) |
|---|---|---|
| 內容 | 整個 publish 夾打包 | 同樣那包,用 Inno / Velopack 包起來 |
| 安裝 | 解壓即用 | 安裝精靈(捷徑、自動更新、可選資料夾) |
| 改參數 | 自己編 `appsettings.json` | 精靈寫入 `appsettings.json` / 環境變數 |
| 適用 | 你自己兩台機、隨身碟 | 給別人、要自動更新時 |

## 6. WD14 模型(執行期下載,不打包)

WD14 ONNX 模型(數百 MB)**不進 exe**,首次啟用時以 HTTPS 下載到 `<BaseDir>/models/wd14`。所以:
- exe 不會因模型爆大。
- 第一次開「自動標籤」需連網下載一次,之後離線可用。
- 關掉 `Inference:Wd14:Enabled` 則完全不碰模型。

## 7. 跨環境

| 場景 | RID / 做法 | 備註 |
|---|---|---|
| **你的兩台 Windows** | `win-x64` 一份 | **DirectML 同時吃 NVIDIA / AMD**,一份 build 兩台都能跑(鐵則 #6) |
| 無 GPU 機器 | 同上 | DirectML 無 GPU 自動退 CPU |
| 未來 NAS / Linux | `linux-x64` | **DirectML 是 Windows-only → Linux 推論退 CPU**(spec 已註) |

## 8. single-file 已驗證 / 注意事項

- ✅ 原生 dll(DirectML / onnxruntime / e_sqlite3)經 `IncludeNativeLibrariesForSelfExtract` **嵌入 exe**,啟動時解壓到 `%TEMP%\.net`(首次啟動略慢)。
- ✅ 路徑解析用 `Environment.GetFolderPath` / `Path`,**未用** single-file 會壞的 `Assembly.Location`,已確認安全。
- ✅ 實跑驗證:serve 前端(200)、API(`/api/folder-roots`→`[]`)、SQLite 自動建 `pm.sqlite`、資料落 `%LOCALAPPDATA%`。
- ⚠️ 不開 trimming(EF Core）。

---

## 9. 用 GitHub Release 自動散布

> ✅ **已設定**(workflow:`.github/workflows/release.yml`)。打版本 tag 即自動建 Release 並附上 zip。

**發版步驟(就這兩行):**
```bash
git tag v0.1.0
git push origin v0.1.0
```
之後到 repo 的 **Releases 頁**會出現 `sus-picture-management-v0.1.0-win-x64.zip`,使用者下載、解壓、跑 `Pm.Api.exe` 即可。

**workflow 做了什麼**(`windows-latest` runner —— win-x64 自包含 + 原生 DirectML 需 Windows):
1. **觸發**:push 符合 `v*` 的 tag。
2. **驗證**:`npm ci` → `npx ng test --watch=false`(前端)+ `dotnet test`(後端)—— 確保 tag 指向的 commit 是綠的才出版。
3. **build / publish**:`npx ng build`(前端靜態檔 → `src/Pm.Api/wwwroot`)→ `dotnet publish src/Pm.Api -p:PublishProfile=win-x64`(自包含單檔)。
4. **打包**:`Compress-Archive` 把整個 publish 夾(`Pm.Api.exe` + `wwwroot/` + `appsettings.json`)壓成 `sus-picture-management-<tag>-win-x64.zip`。
5. **發布**:`softprops/action-gh-release@v2` 建立該 tag 的 Release、附上 zip,Release notes 由 commit 自動生成(`generate_release_notes`)。

**注意 / 待決:**
- **版本號走 SemVer**,以 git tag 為單一真相(`package.json` 的 `version` 目前 `0.0.0`,artifact 命名不依賴它)。
- **未做 code signing** —— 使用者首次執行會跳 Windows SmartScreen 警告(點「仍要執行」)。要消除需簽章憑證(自簽不夠,需 OV/EV CA),屬日後。
- **未接自動更新**(Velopack)—— 目前是手動下載新版 zip。
- workflow 預設**含測試 gate**;若想加速、只出 build 不跑測試,刪掉「前端測試 / 後端測試」兩步即可(不建議)。
