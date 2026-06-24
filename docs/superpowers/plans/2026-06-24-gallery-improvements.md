# Gallery 改進(真總命中數 / WD14 佇列數 / 搜尋進 URL / 密度切換 / 無限捲)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 補齊 gallery 五個體驗缺口:① 顯示真實總命中數、② 顯示真實 WD14 待標佇列數、③ 搜尋狀態進 URL(可重整/分享/上一頁)、#2 把「密度切換」鈕真的接上版面、#3 把「查看更多」按鈕換成滑到底自動載入。

**Architecture:** ①② 後端各加一個唯讀 count 端點(不碰既有查詢/分頁),前端把寫死的顯示改成真實 signal。③ 以 URL query param 為單一真相:gallery-view 訂閱 `queryParams` → 套用到 store 並查詢;所有 token 操作改成「推進 URL」,訂閱回呼是唯一查詢入口(無同步迴圈)。#2 綁 `viewMode` signal 到 masonry `column-count`。#3 用 `IntersectionObserver` 哨兵在接近底部時自動載下一頁。

**Tech Stack:** .NET 10 Minimal API + EF Core(SQLite)+ xUnit;Angular 22(signals、Router `withComponentInputBinding` 已啟用)、vitest、Tailwind v4。

## Global Constraints

- 後端走 TDD(xUnit;每測試獨立 sqlite 檔 + `ctx.Database.Migrate()`,比照 `tests/Pm.Scanner.Tests/PhotoQueryTests.cs`)。前端純函式走 `npx ng test --watch=false`(vitest,globals,測試不 import vitest);UI 改動走 `npx ng build`(0 錯)+ 起 app 手測。
- 後端用 PowerShell 跑 `dotnet`;git 用 PowerShell(本機 Bash PATH 異常)。前端在 `src/Pm.Web` 跑 npx。
- 純加法/接線:**不改既有查詢語意、不動 SQLite canonical、不碰原圖**;不要動 `src/Pm.Api/Properties/launchSettings.json`。
- UI lib 僅 `@angular/cdk`;元件 scoped `.css` 不得 `@apply`(沿用 `var(--token)`)。
- 既有測試:後端 `dotnet test` 全綠、前端 52 測試全綠,不可弄壞。
- commit 訊息結尾兩行:`Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` 與 `Claude-Session: https://claude.ai/code/session_01WoZeq9KeCwCE7Qie7sKqjA`。

---

## File Structure

- **Modify** `src/Pm.Scanner/PhotoQueryService.cs` — 加 `CountAsync`(鏡像 `SearchAsync` 但回 `long`)。
- **Modify** `src/Pm.Api/Program.cs` — 加 `POST /api/search/count`、`GET /api/tagging/stats`。
- **Modify** `tests/Pm.Scanner.Tests/PhotoQueryTests.cs` — 加 `CountAsync` 測試。
- **Modify** `src/Pm.Web/src/app/core/api/pm-api.ts` — 加 `searchCount`、`taggingStats`。
- **Modify** `src/Pm.Web/src/app/core/tag-search.ts` (+ `.spec.ts`) — 加 `encodeTokens` / `decodeTokens`。
- **Modify** `src/Pm.Web/src/app/features/gallery/gallery.store.ts` — `hitCount`/`wd14Queue` 改 signal、輪詢、URL 同步。
- **Modify** `src/Pm.Web/src/app/features/gallery/gallery-view/gallery-view.ts` — 訂閱 `queryParams` 驅動查詢。
- **Modify** `src/Pm.Web/src/app/features/manage/saved-searches/saved-searches.ts` — `onPick` 配合新 `setTokens`。
- **Modify** `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts` (+ `.html` / `.css`) — 密度切換 + 無限捲。

---

### Task 1: 後端 `CountAsync` + `POST /api/search/count`

**Files:**
- Modify: `src/Pm.Scanner/PhotoQueryService.cs`
- Modify: `src/Pm.Api/Program.cs`
- Test: `tests/Pm.Scanner.Tests/PhotoQueryTests.cs`

**Interfaces:**
- Produces: `PhotoQueryService.CountAsync(IEnumerable<string> all, IEnumerable<string> none, CancellationToken ct = default): Task<long>`;端點 `POST /api/search/count` 回 `{ total: long }`。

- [ ] **Step 1: 寫失敗測試**(加到 `tests/Pm.Scanner.Tests/PhotoQueryTests.cs` 類別內,沿用既有 `NewContext/Svc/AddPhoto` helper）

```csharp
    [Fact]
    public async Task Count_matches_search_total_with_include_and_exclude()
    {
        long vspo, pekora, nsfw;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "t", AbsPath = @"D:\x" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync();

            var t_vspo = new Tag { Name = "vspo", Kind = "copyright" };
            var t_pekora = new Tag { Name = "pekora", Kind = "character" };
            var t_nsfw = new Tag { Name = "nsfw", Kind = "meta" };
            ctx.Tags.AddRange(t_vspo, t_pekora, t_nsfw); await ctx.SaveChangesAsync();
            ctx.TagRelations.Add(new TagRelation { ParentTagId = t_vspo.Id, ChildTagId = t_pekora.Id });
            await ctx.SaveChangesAsync();
            vspo = t_vspo.Id; pekora = t_pekora.Id; nsfw = t_nsfw.Id;

            await AddPhoto(ctx, r, "p1", pekora);
            await AddPhoto(ctx, r, "p2", pekora, nsfw);
            await AddPhoto(ctx, r, "p3");
        }

        await using var ctx2 = NewContext();
        var svc = Svc(ctx2);

        Assert.Equal(2, await svc.CountAsync(["vspo"], []));        // implication 命中 p1,p2
        Assert.Equal(1, await svc.CountAsync(["vspo"], ["nsfw"]));  // 排除 nsfw → p1
        Assert.Equal(3, await svc.CountAsync([], []));              // 全部 present
        Assert.Equal(0, await svc.CountAsync(["nonexistent"], [])); // 未知 tag → 0
    }
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Scanner.Tests --filter Count_matches_search_total_with_include_and_exclude`
Expected: 編譯失敗(`CountAsync` 不存在)。

- [ ] **Step 3: 實作 `CountAsync`**(加到 `src/Pm.Scanner/PhotoQueryService.cs` 的 `SearchAsync` 之後,鏡像其組查邏輯但 `.CountAsync()`)

```csharp
    public async Task<long> CountAsync(
        IEnumerable<string> all, IEnumerable<string> none,
        CancellationToken ct = default)
    {
        var includeGroups = new List<List<long>>();
        foreach (var name in all.Distinct())
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is null) return 0;   // 未知 tag → 無結果
            includeGroups.Add(await closure.DescendantsAsync(tag.Id, ct));
        }

        var excludeIds = new List<long>();
        foreach (var name in none.Distinct())
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is not null) excludeIds.AddRange(await closure.DescendantsAsync(tag.Id, ct));
        }

        var q = db.Photos.Where(p => p.Locations.Any(l => l.Status == "present"));
        foreach (var group in includeGroups)
            q = q.Where(p => p.Tags.Any(t => group.Contains(t.TagId)));
        if (excludeIds.Count > 0)
            q = q.Where(p => !p.Tags.Any(t => excludeIds.Contains(t.TagId)));

        return await q.LongCountAsync(ct);
    }
```

- [ ] **Step 4: 加端點**(`src/Pm.Api/Program.cs`,放在現有 `POST /api/search`(`app.MapPost("/api/search", ...)`)之後)

```csharp
app.MapPost("/api/search/count", async (SearchDto dto, PhotoQueryService svc) =>
    Results.Ok(new { total = await svc.CountAsync(dto.All ?? [], dto.None ?? []) }))
    .WithTags("Search");
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test tests/Pm.Scanner.Tests --filter Count_matches_search_total_with_include_and_exclude`
Expected: PASS。再跑 `dotnet build src/Pm.Api`(0 錯;若有殘留 server 持鎖 DLL 先停掉)。

- [ ] **Step 6: Commit**

```bash
git add src/Pm.Scanner/PhotoQueryService.cs src/Pm.Api/Program.cs tests/Pm.Scanner.Tests/PhotoQueryTests.cs
git commit -m "feat(api): 搜尋總數 CountAsync + POST /api/search/count(TDD)"
```

---

### Task 2: 前端真總命中數接線

**Files:**
- Modify: `src/Pm.Web/src/app/core/api/pm-api.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/gallery.store.ts`

**Interfaces:**
- Consumes: `POST /api/search/count`(Task 1)。
- Produces: `PmApi.searchCount(req: SearchReq): Promise<{ total: number }>`;`GalleryStore.hitCount` 改為真實總數 signal。

- [ ] **Step 1: pm-api 加 `searchCount`**(`pm-api.ts`,放 `search(...)` 之後)

```ts
  searchCount(req: SearchReq): Promise<{ total: number }> {
    return firstValueFrom(this.http.post<{ total: number }>('/api/search/count', req));
  }
```

- [ ] **Step 2: store `hitCount` 改 signal + search 時取總數**

在 `gallery.store.ts`,把
```ts
  // 命中數:API 無總數,先顯示已載入筆數(deferred 註明)。
  readonly hitCount = computed(() => this._photos().length);
```
換成
```ts
  // 命中數:真實總數(來自 /api/search/count)。
  private readonly _hitCount = signal(0);
  readonly hitCount = this._hitCount.asReadonly();
```
並把 `search()` 內取頁那段
```ts
      const page = await this.api.search({ all, none, afterId: null, pageSize: PAGE_SIZE });
      this._photos.set(page.items);
      this._nextCursor.set(page.nextCursor ?? null);
```
換成(總數與首頁並行)
```ts
      const [count, page] = await Promise.all([
        this.api.searchCount({ all, none }),
        this.api.search({ all, none, afterId: null, pageSize: PAGE_SIZE }),
      ]);
      this._hitCount.set(count.total);
      this._photos.set(page.items);
      this._nextCursor.set(page.nextCursor ?? null);
```
在 `search()` 的 `catch` 內(已 `_photos.set([])`)加一行 `this._hitCount.set(0);`。

- [ ] **Step 3: build + 既有測試**

Run: `cd src/Pm.Web ; npx ng build`(0 錯)、`npx ng test --watch=false`(52 綠)。

- [ ] **Step 4: 手測**

起 app,gallery「符合 N 張」應顯示**真實總數**(與已載入數不同;捲動載入更多時 N 不變);加 token 後 N 隨命中改變。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/api/pm-api.ts src/Pm.Web/src/app/features/gallery/gallery.store.ts
git commit -m "feat(web): gallery 顯示真實總命中數(接 /api/search/count)"
```

---

### Task 3: 後端 `GET /api/tagging/stats`

**Files:**
- Modify: `src/Pm.Api/Program.cs`

**Interfaces:**
- Produces: `GET /api/tagging/stats` 回 `{ pending: int, error: int, running: int }`(`TaggingJob.State` 計數)。

- [ ] **Step 1: 加端點**(`src/Pm.Api/Program.cs`,放在 `POST /api/tag/requeue` 之後)

```csharp
app.MapGet("/api/tagging/stats", async (PmDbContext db) =>
    Results.Ok(new
    {
        pending = await db.TaggingJobs.CountAsync(j => j.State == "pending"),
        error = await db.TaggingJobs.CountAsync(j => j.State == "error"),
        running = await db.TaggingJobs.CountAsync(j => j.State == "running"),
    }))
    .WithTags("Tagging");
```

- [ ] **Step 2: build + 冒煙**

Run: `dotnet build src/Pm.Api`(0 錯)。起 app 後 `Invoke-WebRequest http://localhost:5180/api/tagging/stats`,應回 `{ "pending": N, "error": N, "running": N }` JSON(無資料時皆 0)。

> 註:此端點為三個 `CountAsync` 直查,邏輯極簡,以 build + 冒煙驗證(不另寫單測)。

- [ ] **Step 3: Commit**

```bash
git add src/Pm.Api/Program.cs
git commit -m "feat(api): GET /api/tagging/stats(WD14 job 狀態計數)"
```

---

### Task 4: 前端 WD14 佇列數 + 輪詢

**Files:**
- Modify: `src/Pm.Web/src/app/core/api/pm-api.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/gallery.store.ts`

**Interfaces:**
- Consumes: `GET /api/tagging/stats`(Task 3)。
- Produces: `PmApi.taggingStats(): Promise<{ pending: number; error: number; running: number }>`;`GalleryStore.wd14Queue` 改真實 signal、每 4s 輪詢。

- [ ] **Step 1: pm-api 加 `taggingStats`**(`pm-api.ts`)

```ts
  taggingStats(): Promise<{ pending: number; error: number; running: number }> {
    return firstValueFrom(
      this.http.get<{ pending: number; error: number; running: number }>('/api/tagging/stats'),
    );
  }
```

- [ ] **Step 2: store `wd14Queue` 改 signal + 輪詢**

`gallery.store.ts` 檔首 import 補 `DestroyRef`:`import { Injectable, computed, inject, signal, DestroyRef } from '@angular/core';`

把
```ts
  // WD14 佇列:API 無來源 → 顯示 0(deferred 註明)。
  readonly wd14Queue = 0;
```
換成
```ts
  // WD14 待標佇列:真實 pending+error(每 4s 輪詢)。
  private readonly _wd14Queue = signal(0);
  readonly wd14Queue = this._wd14Queue.asReadonly();
```

> 注意:`photo-grid.ts` 用 `wd14QueueText = computed(() => this.wd14Queue.toLocaleString(...))`,原本 `wd14Queue` 是 number、現改成 signal,**`photo-grid.ts` 要把 `this.wd14Queue` 改成 `this.wd14Queue()`**(見 Step 3)。

在建構流程加輪詢。`GalleryStore` 目前無 constructor —— 新增:
```ts
  constructor() {
    void this.loadWd14Stats();
    const id = setInterval(() => void this.loadWd14Stats(), 4000);
    inject(DestroyRef).onDestroy(() => clearInterval(id));
  }

  private async loadWd14Stats(): Promise<void> {
    try {
      const s = await this.api.taggingStats();
      this._wd14Queue.set(s.pending + s.error);
    } catch {
      /* 靜默:佇列數非關鍵,失敗保留前值 */
    }
  }
```

- [ ] **Step 3: 修 photo-grid 的 signal 呼叫**

`photo-grid.ts` 把
```ts
  readonly wd14QueueText = computed(() => this.wd14Queue.toLocaleString('en-US'));
```
換成
```ts
  readonly wd14QueueText = computed(() => this.wd14Queue().toLocaleString('en-US'));
```
（`readonly wd14Queue = this.store.wd14Queue;` 那行不變;它現在指向 signal。)

- [ ] **Step 4: build + 測試 + 手測**

Run: `cd src/Pm.Web ; npx ng build`(0 錯)、`npx ng test --watch=false`(52 綠)。手測:toolbar「WD14 佇列 N 待標」顯示真實 pending+error;開 WD14 跑圖時數字會隨輪詢變動。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/api/pm-api.ts src/Pm.Web/src/app/features/gallery/gallery.store.ts src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts
git commit -m "feat(web): WD14 佇列數真實顯示 + 4s 輪詢(DestroyRef 清理)"
```

---

### Task 5: `encodeTokens` / `decodeTokens` 純函式

**Files:**
- Modify: `src/Pm.Web/src/app/core/tag-search.ts`
- Test: `src/Pm.Web/src/app/core/tag-search.spec.ts`

**Interfaces:**
- Consumes: `SearchToken`(`@features/gallery/gallery.store` 的 `{ text: string; kind: TagKind }`)—— 但為避免 core→feature 反向相依,**參數型別只用 `{ text: string }[]` / 回 `{ text: string; kind: 'general' }[]`**(kind 不進 URL,還原一律 'general')。
- Produces:
  - `encodeTokens(tokens: readonly { text: string }[]): string` — token 串成 `tag1+tag2+-tag3`(內部空白→`_`);空陣列→`''`。
  - `decodeTokens(q: string): { text: string; kind: 'general' }[]` — 反向;空/壞值→`[]`。

- [ ] **Step 1: 寫失敗測試**(加到 `tag-search.spec.ts` 末尾)

```ts
import { encodeTokens, decodeTokens } from './tag-search';

describe('encodeTokens / decodeTokens', () => {
  it('多 token 以 + 串、內部空白轉底線', () => {
    expect(encodeTokens([{ text: 'blue archive' }, { text: '-smile' }])).toBe('blue_archive+-smile');
  });
  it('空陣列 → 空字串', () => {
    expect(encodeTokens([])).toBe('');
  });
  it('decode 還原為 text + general kind', () => {
    expect(decodeTokens('blue_archive+-smile')).toEqual([
      { text: 'blue archive', kind: 'general' },
      { text: '-smile', kind: 'general' },
    ]);
  });
  it('空/壞值 → 空陣列', () => {
    expect(decodeTokens('')).toEqual([]);
    expect(decodeTokens('+++')).toEqual([]);
  });
});
```

> 註:encode 把 token text 內的空白轉 `_`、用 `+` 分隔;decode 反之(`_`→空白)。tag canonical 內本就用 `_`,顯示時才轉空白,故 URL 用底線形式即可。

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd src/Pm.Web ; npx ng test --watch=false`
Expected: FAIL(`encodeTokens`/`decodeTokens` 未定義)。

- [ ] **Step 3: 實作**(加到 `tag-search.ts`)

```ts
// token → URL query 片段:text 內部空白轉 '_',多個以 '+' 串。空 → ''。
export function encodeTokens(tokens: readonly { text: string }[]): string {
  return tokens
    .map((t) => t.text.trim())
    .filter(Boolean)
    .map((t) => t.replace(/\s+/g, '_'))
    .join('+');
}

// URL query 片段 → token(kind 不進 URL,一律 general;空/壞 → [])。
export function decodeTokens(q: string): { text: string; kind: 'general' }[] {
  if (!q) return [];
  return q
    .split('+')
    .map((s) => s.replace(/_/g, ' ').trim())
    .filter(Boolean)
    .map((text) => ({ text, kind: 'general' as const }));
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd src/Pm.Web ; npx ng test --watch=false`
Expected: PASS（既有 + 新增全綠）。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/tag-search.ts src/Pm.Web/src/app/core/tag-search.spec.ts
git commit -m "feat(web): tag-search encode/decodeTokens(URL 同步用,TDD)"
```

---

### Task 6: 搜尋狀態進 URL(store 單一真相 + gallery-view 訂閱)

把「token 操作」改成推進 URL;`queryParams` 訂閱是唯一查詢入口(無迴圈)。

**Files:**
- Modify: `src/Pm.Web/src/app/features/gallery/gallery.store.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/gallery-view/gallery-view.ts`
- Modify: `src/Pm.Web/src/app/features/manage/saved-searches/saved-searches.ts`

**Interfaces:**
- Consumes: `encodeTokens`/`decodeTokens`(Task 5);`Router`/`ActivatedRoute`(`@angular/router`)。
- Produces: `GalleryStore.applyQuery(q: string): void`(URL→store+查詢,唯一查詢入口);`setTokens/addToken/removeToken/toggleToken` 改為「推進 `/gallery?q=`」。

- [ ] **Step 1: store 改為 URL 驅動**

`gallery.store.ts`:
import 補:`import { Router } from '@angular/router';` 與 `import { encodeTokens, decodeTokens } from '@core/tag-search';`(`toggleExclude` 已 import)。

class 內加 `private readonly router = inject(Router);`。

新增「URL→store」唯一查詢入口,並把 token 操作改成「推進 URL」:
```ts
  // URL query 'q' → 設 token 並查詢。gallery-view 的 queryParams 訂閱是唯一呼叫處。
  applyQuery(q: string): void {
    this._tokens.set(decodeTokens(q) as SearchToken[]);
    void this.search();
  }

  // 推進 /gallery?q=...(空則移除 q);實際 setTokens+search 由訂閱回呼完成 → 無迴圈。
  private pushTokens(tokens: SearchToken[]): void {
    const q = encodeTokens(tokens);
    void this.router.navigate(['/gallery'], { queryParams: q ? { q } : {} });
  }
```

把既有 `setTokens/addToken/removeToken/toggleToken` 四個方法**整段換成**(都改成只 `pushTokens`,不再直接 mutate / search):
```ts
  // 設定 token(saved 套用用):推進 URL。
  setTokens(tokens: SearchToken[]): void {
    this.pushTokens(tokens);
  }

  // 加一個 token(已有同 text 略過):推進 URL。
  addToken(token: SearchToken): void {
    const text = token.text.trim();
    if (!text) return;
    if (this._tokens().some((x) => x.text === text)) return;
    this.pushTokens([...this._tokens(), { ...token, text }]);
  }

  // 移除 token:推進 URL。
  removeToken(idx: number): void {
    this.pushTokens(this._tokens().filter((_, i) => i !== idx));
  }

  // 切換排除/包含:推進 URL。
  toggleToken(idx: number): void {
    this.pushTokens(this._tokens().map((t, i) => (i === idx ? { ...t, text: toggleExclude(t.text) } : t)));
  }
```

把既有 `load()` 內的 `this.search()` 移除(查詢改由 gallery-view 訂閱觸發),只留 facets:
```ts
  // 初次載入:只載 facet 樹(圖片查詢改由 gallery-view 的 URL 訂閱驅動)。
  async load(): Promise<void> {
    await this.loadFacets();
  }
```

- [ ] **Step 2: gallery-view 訂閱 queryParams**

`gallery-view/gallery-view.ts` 改成:
```ts
import { Component, OnInit, inject, DestroyRef } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FacetSidebar } from '../facet-sidebar/facet-sidebar';
import { PhotoGrid } from '../photo-grid/photo-grid';
import { Inspector } from '@features/inspector/inspector/inspector';
import { GalleryStore } from '../gallery.store';

// 相簿三欄:facet 側欄(252)· 圖牆(1fr)· 檢視器(350)。
@Component({
  selector: 'app-gallery-view',
  imports: [FacetSidebar, PhotoGrid, Inspector],
  template: `
    <div class="gview">
      <app-facet-sidebar />
      <app-photo-grid />
      <app-inspector [photoId]="store.selectedId()" />
    </div>
  `,
  styles: [
    `
      .gview {
        display: grid;
        grid-template-columns: 252px 1fr 350px;
        height: 100vh;
        min-width: 0;
      }
      @media (max-width: 1180px) {
        .gview {
          grid-template-columns: 230px 1fr;
        }
        app-inspector {
          display: none;
        }
      }
    `,
  ],
})
export class GalleryView implements OnInit {
  readonly store = inject(GalleryStore);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    void this.store.load(); // facet 樹
    // URL 'q' 是搜尋的單一真相:初次 + 每次變動(含上一頁)都套用並查詢。
    this.route.queryParams
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((p) => this.store.applyQuery((p['q'] as string) ?? ''));
  }
}
```

- [ ] **Step 3: saved-searches `onPick` 配合**

`saved-searches.ts` 的 `onPick` 改成(`setTokens` 現在會自己導到 `/gallery?q=`,不需再 navigate;解析失敗則導空 gallery):
```ts
  // 點卡片:解析 queryJson → 推進 /gallery?q=(由 GalleryStore.setTokens 導頁)。
  onPick(id: number): void {
    this.active.set(id);
    const row = this.saved().find((s) => s.id === id);
    try {
      const tokens = JSON.parse(row?.query ?? '[]') as SearchToken[];
      if (Array.isArray(tokens) && tokens.length) {
        this.gallery.setTokens(tokens);
        return;
      }
    } catch {
      /* 舊/壞資料:落到下方導空 gallery */
    }
    void this.router.navigate(['/gallery']);
  }
```
（`row?.query` 欄位名沿用既有;`Router`/`GalleryStore` 已注入。）

- [ ] **Step 4: build + 測試**

Run: `cd src/Pm.Web ; npx ng build`(0 錯)、`npx ng test --watch=false`(52 綠)。

- [ ] **Step 5: 手測(逐項)**

起 app:
1. 進 `/gallery` 無參數 → 載入全部、URL 無 `q`。
2. 從下拉挑 tag → URL 變 `/gallery?q=xxx`,圖牆更新。
3. 再挑一個 → `?q=xxx+yyy`,AND 收窄。
4. 點 token chip 切排除 → `q` 內該標前綴 `-` 變動。
5. **Ctrl+R 重整** → 維持同一搜尋(從 URL 還原)。
6. 瀏覽器 **上一頁** → 回到前一個搜尋。
7. 複製 URL 開新分頁 → 同一搜尋結果。
8. 「收藏的搜尋」點卡 → 導到 `/gallery?q=...` 且套用。
   - 註:還原的 token chip 顏色暫為 general 色(kind 不進 URL,已知取捨),不影響查詢。

- [ ] **Step 6: Commit**

```bash
git add src/Pm.Web/src/app/features/gallery/gallery.store.ts \
  src/Pm.Web/src/app/features/gallery/gallery-view/gallery-view.ts \
  src/Pm.Web/src/app/features/manage/saved-searches/saved-searches.ts
git commit -m "feat(web): 搜尋狀態進 URL(query param 單一真相,可重整/分享/上一頁)"
```

---

### Task 7: #2 密度切換接上版面

**Files:**
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html`
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.css`

**Interfaces:**
- Consumes: 既有 `viewMode` signal(`'dense' | 'large'`)。

- [ ] **Step 1: 綁 class 到 masonry**

`photo-grid.html` 把
```html
    <div class="masonry">
```
換成
```html
    <div class="masonry" [class.dense]="viewMode() === 'dense'" [class.large]="viewMode() === 'large'">
```

- [ ] **Step 2: CSS 依密度改 column-count**

`photo-grid.css` 在 `.masonry { ... }` / 既有 media query 之後加(dense 比現況密、large 大圖少欄;數字可日後微調):
```css
.masonry.dense {
  column-count: 5;
}
.masonry.large {
  column-count: 2;
}
@media (max-width: 1500px) {
  .masonry.dense {
    column-count: 4;
  }
  .masonry.large {
    column-count: 2;
  }
}
```

- [ ] **Step 3: build + 手測**

Run: `cd src/Pm.Web ; npx ng build`(0 錯)。手測:點「大圖」→ 欄數變少、圖變大;點「小圖密集」→ 欄數變多、圖變小;切換有明顯差異。

- [ ] **Step 4: Commit**

```bash
git add src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.css
git commit -m "feat(web): gallery 密度切換接上 masonry column-count(dense/large)"
```

---

### Task 8: #3 無限捲取代「查看更多」

**Files:**
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html`
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.css`

**Interfaces:**
- Consumes: 既有 `hasMore()` / `loading()` signal、`store.loadMore()`。

- [ ] **Step 1: photo-grid.ts 加 IntersectionObserver 哨兵**

`photo-grid.ts` 類別宣告改成 implements `AfterViewInit, OnDestroy`,import 補:
```ts
import { Component, computed, inject, signal, ElementRef, ViewChild, AfterViewInit, OnDestroy } from '@angular/core';
```
class 簽名:`export class PhotoGrid implements AfterViewInit, OnDestroy {`

加成員與生命週期(放在 `loadMore()` 附近),並**移除既有的 `loadMore()` 方法**(改由 observer 直接呼叫 store):
```ts
  // 無限捲哨兵:接近底部時自動載下一頁(rootMargin 提前預抓)。
  @ViewChild('sentinel') private sentinel?: ElementRef<HTMLElement>;
  private io?: IntersectionObserver;

  ngAfterViewInit(): void {
    if (!this.sentinel) return;
    this.io = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting && this.hasMore() && !this.loading()) {
          void this.store.loadMore();
        }
      },
      { rootMargin: '600px' },
    );
    this.io.observe(this.sentinel.nativeElement);
  }

  ngOnDestroy(): void {
    this.io?.disconnect();
  }
```
（刪掉原本的 `loadMore(): void { void this.store.loadMore(); }`。）

> 註:`store` 在 photo-grid 是 `private readonly store`,observer 內可直接用 `this.store.loadMore()`。

- [ ] **Step 2: photo-grid.html 換掉 load-more 按鈕為哨兵**

把
```html
    @if (loading()) {
      <div class="empty">載入中…</div>
    }
    @if (hasMore() && !loading()) {
      <div class="loadmore">
        <button class="btn ghost" (click)="loadMore()">載入更多</button>
      </div>
    }
```
換成
```html
    @if (loading()) {
      <div class="empty">載入中…</div>
    }
    <!-- 無限捲哨兵:進入視野(含 rootMargin 提前)即自動載下一頁 -->
    <div #sentinel class="scroll-sentinel" aria-hidden="true"></div>
```

- [ ] **Step 3: CSS 哨兵高度**

`photo-grid.css` 末尾加:
```css
.scroll-sentinel {
  height: 1px;
}
```

- [ ] **Step 4: build + 手測**

Run: `cd src/Pm.Web ; npx ng build`(0 錯)、`npx ng test --watch=false`(52 綠)。手測:往下捲**不需點按鈕**就自動接續載入(接近底部即預抓);載入時底部顯示「載入中…」;載到最後一頁後停止(無更多)。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.css
git commit -m "feat(web): 圖牆改無限捲(IntersectionObserver 哨兵),移除查看更多按鈕"
```

---

## Self-Review

**Spec coverage:**
- ① 真總命中數 → Task 1(後端 CountAsync + 端點)+ Task 2(前端接線)。✓
- ② WD14 佇列數 → Task 3(端點)+ Task 4(前端 signal + 輪詢)。✓
- ③ 搜尋進 URL → Task 5(encode/decode 純函式)+ Task 6(store URL 驅動 + gallery-view 訂閱 + saved 配合)。✓
- #2 密度切換 → Task 7。✓
- #3 無限捲 → Task 8。✓

**Placeholder scan:** 無 TODO/TBD;每步附完整碼。✓

**Type consistency:**
- `CountAsync(all, none, ct)` Task 1 定義、端點與前端 `searchCount` 對齊。✓
- `hitCount` Task 2 改 signal;`wd14Queue` Task 4 改 signal(同步把 photo-grid `wd14QueueText` 改成 `wd14Queue()` 呼叫)。✓
- `encodeTokens`/`decodeTokens` Task 5 定義、Task 6 消費,簽名一致(URL 用 `+` 分隔、`_` 表空白、kind 還原 general)。✓
- Task 6 把 `setTokens/addToken/removeToken/toggleToken` 改為 `pushTokens` 推 URL;`applyQuery` 為唯一查詢入口;`load()` 移除 `search()` 改由訂閱驅動 —— 與 gallery-view、saved-searches 一致。✓
- Task 8 移除 `loadMore()` 方法、改 observer 呼叫 `store.loadMore()`;`hasMore()`/`loading()` 沿用既有。✓

**已知取捨(已在計畫標明):** ③ kind 不進 URL → 還原 token chip 暫顯 general 色(使用者已採方案 a)。#2 column-count 數字(dense 5/4、large 2)可微調。④ combobox 抽共用不在本計畫。
