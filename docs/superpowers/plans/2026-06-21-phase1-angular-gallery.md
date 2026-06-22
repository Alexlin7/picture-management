# Phase 1 Angular 相簿 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **前置:** 需先完成布林查詢 API 計畫(本前端消費 `/api/search`、`/api/photos/{id}`、`/api/photos/{id}/thumb`、`/api/roots`、`/api/reconcile/missing`、`/api/roots/{id}/pending-segments`、`/api/path-rules`)。
>
> **前端測試現實:** 前端不像後端能逐行 TDD。本計畫的把關 = ① `ng build` 成功(型別/模板無誤)② 關鍵 service 的 jasmine spec ③ 對照 `docs/mockups/ui-preview.html` 的人工視覺檢視。標 `[手動]` 的步驟由使用者看畫面確認。

**Goal:** 做出 spec §6 的暗色三欄工作台:活動列 → 情境側欄 → 主區(CDK virtual scroll 相簿)→ 檢視器;布林 tag 搜尋列、Saved Search、library root 管理 + 觸發掃描、待確認匣、路徑→tag 確認步驟。`ng build` 產靜態檔由 .NET serve(同一顆程序、同源、免 CORS)。

**Architecture:** Angular(`ng new`,standalone components + signals + 新控制流 `@if`/`@for`)放 `src/Pm.Web`。`PmApi` service 包所有 REST。`ng build` 輸出到 `src/Pm.Api/wwwroot`,`Pm.Api` 加靜態檔服務 + SPA fallback。開發時 `ng serve` 用 proxy 打 `localhost:5180`。視覺照 §6.1 分色(`tag.kind`→顏色)。

**Tech Stack:** Angular(最新,`ng new` 實抓版本)、`@angular/cdk`(virtual scroll)、TypeScript、Jasmine/Karma(ChromeHeadless,用先前裝的 chromium)。

## Global Constraints

- **暗色三欄**、**booru 分色**(character 綠/copyright 紫/general 藍/meta 琥珀/path 灰/manual 粉),強調色青、破壞色紅(§6.1 表)。
- **虛擬滾動**處理十萬量級;keyset 無限捲動(用 `nextCursor`)。
- **同源**:前端由 .NET serve,API 同 origin,不需 CORS。
- 識別子/技術名詞保留原文;UI 文案繁中台灣用語。

---

## File Structure

```
src/
├─ Pm.Web/                          # ng new 產生
│  ├─ angular.json                  # outputPath → ../Pm.Api/wwwroot
│  ├─ proxy.conf.json               # /api → localhost:5180
│  └─ src/app/
│     ├─ api/pm-api.ts              # REST client + 型別
│     ├─ api/pm-api.spec.ts
│     ├─ tag-color.ts               # kind → 顏色
│     ├─ shell/workbench.ts         # 三欄外殼 + 活動列
│     ├─ gallery/gallery.ts         # virtual scroll 相簿 + 搜尋列
│     ├─ inspector/inspector.ts     # 檢視器
│     └─ manage/{roots,reconcile,pending}.ts
└─ Pm.Api/
   ├─ Program.cs                    # +UseStaticFiles + SPA fallback
   └─ wwwroot/                      # ng build 輸出(gitignore 或保留 .gitkeep)
```

---

## Task 1: Angular 骨架 + .NET serve 靜態檔

**Files:**
- Create: `src/Pm.Web/*`(`ng new`)
- Create: `src/Pm.Web/proxy.conf.json`
- Modify: `src/Pm.Web/angular.json`(outputPath)
- Modify: `src/Pm.Api/Program.cs`(靜態檔 + fallback)
- Modify: `.gitignore`(node_modules、wwwroot 產出)

**Interfaces:**
- Produces:`ng build` 輸出至 `src/Pm.Api/wwwroot`;.NET 於 `/` serve SPA、`/api/*` 仍走 API。

- [ ] **Step 1: 建 Angular 專案**

Run:

```bash
cd /d/picture-management/src
npx -y @angular/cli@latest new Pm.Web --style=css --routing=false --ssr=false --skip-git
cd Pm.Web
npx ng add @angular/cdk --skip-confirmation
```

(`ng new` 實際版本以 CLI 抓到為準;若互動詢問,選 CSS、不啟用 SSR。)

- [ ] **Step 2: 設定 build 輸出到 wwwroot + proxy**

修改 `src/Pm.Web/angular.json`,把 production build 的 `outputPath` 改為:

```json
"outputPath": "../Pm.Api/wwwroot"
```

Create `src/Pm.Web/proxy.conf.json`:

```json
{
  "/api": { "target": "http://localhost:5180", "secure": false }
}
```

並在 `angular.json` 的 `serve` options 加 `"proxyConfig": "proxy.conf.json"`。

- [ ] **Step 3: .NET serve 靜態檔 + SPA fallback**

在 `src/Pm.Api/Program.cs`,`var app = builder.Build();` 之後、API 端點之前加:

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
```

並在所有 `app.Map...` 之後、`app.Run();` 之前加 SPA fallback(讓前端路由不被 404):

```csharp
app.MapFallbackToFile("index.html");
```

- [ ] **Step 4: .gitignore**

Append to `.gitignore`:

```gitignore
# Angular
node_modules/
src/Pm.Api/wwwroot/
```

- [ ] **Step 5: 驗證 build 串接**

Run:

```bash
cd /d/picture-management/src/Pm.Web
npm run build
ls ../Pm.Api/wwwroot/index.html
```

Expected: build 成功、`index.html` 出現在 `Pm.Api/wwwroot`。

- [ ] **Step 6: [手動] 整合煙霧測試**

Run:

```bash
cd /d/picture-management
dotnet run --project src/Pm.Api &
sleep 4
curl -s http://localhost:5180/ | grep -i "<app-root" && echo "SPA served"
kill %1
```

Expected: 首頁回傳含 `<app-root>` 的 HTML(.NET 正在 serve Angular)。

- [ ] **Step 7: Commit**

```bash
cd /d/picture-management
git add src/Pm.Web src/Pm.Api/Program.cs .gitignore
git commit -m "feat: Angular 骨架 + .NET serve 靜態檔/SPA fallback + proxy"
```

---

## Task 2: `PmApi` client + 型別 + spec

**Files:**
- Create: `src/Pm.Web/src/app/api/pm-api.ts`
- Create: `src/Pm.Web/src/app/api/pm-api.spec.ts`
- Create: `src/Pm.Web/src/app/tag-color.ts`
- Modify: `src/Pm.Web/src/app/app.config.ts`(provideHttpClient)

**Interfaces:**
- Produces:`PmApi` 服務,方法 `search(req)`, `photo(id)`, `thumbUrl(id)`, `roots()`, `createRoot(name, absPath)`, `scan(id)`, `missing()`, `pendingSegments(rootId)`, `applyRule(dto)`。型別 `PhotoListItem`, `PhotoPage`, `PhotoDetail`, `Root`, `PendingSegment`。

- [ ] **Step 1: 啟用 HttpClient**

在 `src/Pm.Web/src/app/app.config.ts` 的 `providers` 陣列加 `provideHttpClient()`(從 `@angular/common/http` import)。

- [ ] **Step 2: 寫型別與 tag 顏色**

Create `src/Pm.Web/src/app/tag-color.ts`:

```typescript
// §6.1 booru 分色:tag.kind → 顏色
export const TAG_COLOR: Record<string, string> = {
  character: '#4ADE80',
  copyright: '#C084FC',
  general:   '#818CF8',
  meta:      '#FBBF24',
  path:      '#94A3B8',
  manual:    '#F472B6',
};
export const ACCENT = '#22D3EE';
export const DANGER = '#F0616D';
export const tagColor = (kind: string) => TAG_COLOR[kind] ?? TAG_COLOR['general'];
```

Create `src/Pm.Web/src/app/api/pm-api.ts`:

```typescript
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface PhotoListItem { id: number; fileHash: string; width?: number; height?: number; mime?: string; }
export interface PhotoPage { items: PhotoListItem[]; nextCursor?: number | null; }
export interface TagView { id: number; name: string; kind: string; source: string; confidence?: number | null; }
export interface LocationView { libraryRootId: number; relPath: string; status: string; }
export interface PhotoDetail {
  id: number; fileHash: string; width?: number; height?: number; mime?: string;
  takenAt?: string | null; cameraModel?: string | null;
  locations: LocationView[]; tags: TagView[];
}
export interface Root { id: number; name: string; absPath: string; }
export interface PendingSegment { segment: string; count: number; samplePath: string; suggestedAction: string; }
export interface SearchReq { all?: string[]; none?: string[]; afterId?: number | null; pageSize?: number; }

@Injectable({ providedIn: 'root' })
export class PmApi {
  private http = inject(HttpClient);

  search(req: SearchReq): Promise<PhotoPage> {
    return firstValueFrom(this.http.post<PhotoPage>('/api/search', req));
  }
  photo(id: number): Promise<PhotoDetail> {
    return firstValueFrom(this.http.get<PhotoDetail>(`/api/photos/${id}`));
  }
  thumbUrl(id: number): string { return `/api/photos/${id}/thumb`; }

  roots(): Promise<Root[]> { return firstValueFrom(this.http.get<Root[]>('/api/roots')); }
  createRoot(name: string, absPath: string): Promise<Root> {
    return firstValueFrom(this.http.post<Root>('/api/roots', { name, absPath }));
  }
  scan(id: number): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/roots/${id}/scan`, {}));
  }
  missing(): Promise<{ id: number; fileHash: string; paths: string[] }[]> {
    return firstValueFrom(this.http.get<{ id: number; fileHash: string; paths: string[] }[]>('/api/reconcile/missing'));
  }
  pendingSegments(rootId: number): Promise<PendingSegment[]> {
    return firstValueFrom(this.http.get<PendingSegment[]>(`/api/roots/${rootId}/pending-segments`));
  }
  applyRule(dto: { rootId?: number; segment: string; action: string; tagName?: string }): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/path-rules', dto));
  }
}
```

> 註:`GET /api/roots` 端點尚未存在(前面只建了 POST)。本 task Step 4 補它。

- [ ] **Step 3: 補後端 `GET /api/roots`**

在 `src/Pm.Api/Program.cs` 端點區加:

```csharp
app.MapGet("/api/roots", async (PmDbContext db) =>
    Results.Ok(await db.LibraryRoots.Select(r => new { r.Id, r.Name, r.AbsPath }).ToListAsync()));
```

- [ ] **Step 4: 寫 service spec**

Create `src/Pm.Web/src/app/api/pm-api.spec.ts`:

```typescript
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PmApi } from './pm-api';

describe('PmApi', () => {
  let api: PmApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(PmApi);
    http = TestBed.inject(HttpTestingController);
  });

  it('posts a search and returns a page', async () => {
    const p = api.search({ all: ['vspo'] });
    const req = http.expectOne('/api/search');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.all).toEqual(['vspo']);
    req.flush({ items: [{ id: 1, fileHash: 'a' }], nextCursor: null });
    expect((await p).items.length).toBe(1);
  });

  it('builds thumb url', () => {
    expect(api.thumbUrl(7)).toBe('/api/photos/7/thumb');
  });
});
```

- [ ] **Step 5: 跑前端測試**

Run:

```bash
cd /d/picture-management/src/Pm.Web
npx ng test --watch=false --browsers=ChromeHeadless
```

Expected: PASS。**環境註:** 若找不到瀏覽器,設 `CHROME_BIN` 指向先前 `npx playwright install` 的 chromium(`~/.cache/ms-playwright/chromium-*/chrome-win/chrome.exe`),或 `npm i -D puppeteer` 讓 karma-chrome-launcher 找到。

- [ ] **Step 6: Commit**

```bash
cd /d/picture-management
git add src/Pm.Web src/Pm.Api/Program.cs
git commit -m "feat: PmApi client + 型別 + tag 分色 + GET /api/roots"
```

---

## Task 3: 三欄外殼 + 相簿(virtual scroll + 搜尋列)

**Files:**
- Create: `src/Pm.Web/src/app/shell/workbench.ts`(+ template/style)
- Create: `src/Pm.Web/src/app/gallery/gallery.ts`
- Modify: `src/Pm.Web/src/app/app.ts`(掛載 workbench)
- Modify: `src/Pm.Web/src/styles.css`(暗色主題變數)

**Interfaces:**
- Consumes: `PmApi.search`、`thumbUrl`。
- Produces:`Gallery` 元件:搜尋列(空白=AND、`-`=排除)→ `PmApi.search` → CDK virtual scroll 縮圖瀑布;捲到底用 `nextCursor` 載下一頁;點縮圖 emit `select(id)`。

- [ ] **Step 1: 暗色主題變數**

在 `src/Pm.Web/src/styles.css` 開頭加:

```css
:root {
  --bg: #0f1115; --panel: #161922; --panel-2: #1c2030; --line: #2a3040;
  --text: #e6e8ee; --muted: #8b93a7; --accent: #22D3EE; --danger: #F0616D;
}
html, body { margin: 0; height: 100%; background: var(--bg); color: var(--text);
  font-family: Inter, "Noto Sans TC", system-ui, sans-serif; }
```

- [ ] **Step 2: 三欄外殼**

Create `src/Pm.Web/src/app/shell/workbench.ts`:

```typescript
import { Component, signal } from '@angular/core';
import { Gallery } from '../gallery/gallery';
import { Inspector } from '../inspector/inspector';

@Component({
  selector: 'app-workbench',
  standalone: true,
  imports: [Gallery, Inspector],
  styles: [`
    .wrap { display: grid; grid-template-columns: 58px 1fr 360px; height: 100vh; }
    .activity { background: var(--panel-2); border-right: 1px solid var(--line); }
    .main { overflow: hidden; }
    .insp { border-left: 1px solid var(--line); background: var(--panel); }
  `],
  template: `
    <div class="wrap">
      <nav class="activity"></nav>
      <section class="main"><app-gallery (select)="sel.set($event)" /></section>
      <aside class="insp"><app-inspector [photoId]="sel()" /></aside>
    </div>
  `,
})
export class Workbench {
  sel = signal<number | null>(null);
}
```

- [ ] **Step 3: 相簿(virtual scroll + 搜尋)**

Create `src/Pm.Web/src/app/gallery/gallery.ts`:

```typescript
import { Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { PmApi, PhotoListItem } from '../api/pm-api';

@Component({
  selector: 'app-gallery',
  standalone: true,
  imports: [ScrollingModule],
  styles: [`
    .bar { padding: 10px; border-bottom: 1px solid var(--line); }
    .bar input { width: 100%; padding: 8px 10px; background: var(--panel-2);
      border: 1px solid var(--line); border-radius: 8px; color: var(--text); }
    cdk-virtual-scroll-viewport { height: calc(100vh - 54px); }
    .row { display: grid; grid-template-columns: repeat(5, 1fr); gap: 6px; padding: 6px; }
    .tile { aspect-ratio: 1; background: var(--panel-2); border-radius: 6px;
      object-fit: cover; width: 100%; height: 100%; cursor: pointer; }
  `],
  template: `
    <div class="bar">
      <input placeholder="tag 搜尋(空白=AND,前綴 - =排除)"
             (keyup.enter)="run($any($event.target).value)" />
    </div>
    <cdk-virtual-scroll-viewport itemSize="140" (scrolledIndexChange)="onScroll($event)">
      <div class="row" *cdkVirtualFor="let r of rows()">
        @for (p of r; track p.id) {
          <img class="tile" [src]="api.thumbUrl(p.id)" (click)="select.emit(p.id)" loading="lazy" />
        }
      </div>
    </cdk-virtual-scroll-viewport>
  `,
})
export class Gallery {
  api = inject(PmApi);
  @Output() select = new EventEmitter<number>();

  private items = signal<PhotoListItem[]>([]);
  private cursor: number | null = null;
  private all: string[] = [];
  private none: string[] = [];
  private loading = false;

  // 把一維 items 切成每列 5 張給 virtual scroll
  rows = signal<PhotoListItem[][]>([]);

  async run(query: string) {
    this.all = []; this.none = [];
    for (const tok of query.split(/\s+/).filter(Boolean))
      (tok.startsWith('-') ? this.none : this.all).push(tok.replace(/^-/, ''));
    this.items.set([]); this.cursor = null;
    await this.loadMore();
  }

  async onScroll(index: number) {
    if (index > this.rows().length - 4 && this.cursor !== null) await this.loadMore();
  }

  private async loadMore() {
    if (this.loading) return;
    this.loading = true;
    try {
      const page = await this.api.search({ all: this.all, none: this.none, afterId: this.cursor, pageSize: 200 });
      this.items.update(x => [...x, ...page.items]);
      this.cursor = page.nextCursor ?? null;
      const flat = this.items();
      const rows: PhotoListItem[][] = [];
      for (let i = 0; i < flat.length; i += 5) rows.push(flat.slice(i, i + 5));
      this.rows.set(rows);
    } finally { this.loading = false; }
  }

  ngOnInit() { this.run(''); }
}
```

- [ ] **Step 4: 掛載 + build 驗證**

把 `src/Pm.Web/src/app/app.ts`(或 `app.component.ts`)的 template 改為只放 `<app-workbench />` 並 import `Workbench`。

Run:

```bash
cd /d/picture-management/src/Pm.Web
npm run build
```

Expected: build 成功(型別/模板無誤)。

- [ ] **Step 5: Commit**

```bash
cd /d/picture-management
git add src/Pm.Web
git commit -m "feat: 三欄工作台 + 相簿(CDK virtual scroll + 布林搜尋列 + keyset 無限捲)"
```

---

## Task 4: 檢視器(身分→位置 + 分色 tags)

**Files:**
- Create: `src/Pm.Web/src/app/inspector/inspector.ts`

**Interfaces:**
- Consumes: `PmApi.photo`、`tagColor`。
- Produces:`Inspector` 元件,`@Input() photoId: number | null`;載入明細,顯示大圖 + 「身分→位置」+ 分色 tags + EXIF。

- [ ] **Step 1: 寫檢視器**

Create `src/Pm.Web/src/app/inspector/inspector.ts`:

```typescript
import { Component, Input, inject, signal, effect } from '@angular/core';
import { PmApi, PhotoDetail } from '../api/pm-api';
import { tagColor } from '../tag-color';

@Component({
  selector: 'app-inspector',
  standalone: true,
  styles: [`
    .pad { padding: 12px; overflow: auto; height: 100vh; box-sizing: border-box; }
    .hash { font-family: "JetBrains Mono", monospace; font-size: 11px; color: var(--muted); word-break: break-all; }
    .loc { font-size: 12px; padding: 4px 8px; background: var(--panel-2); border-radius: 6px; margin: 4px 0; }
    .chip { display: inline-block; font-size: 12px; padding: 2px 8px; border-radius: 999px;
      margin: 2px; border: 1px solid; }
    .empty { color: var(--muted); padding: 24px; text-align: center; }
  `],
  template: `
    @if (photo(); as p) {
      <div class="pad">
        <img [src]="api.thumbUrl(p.id)" style="width:100%;border-radius:8px" />
        <h4>身分</h4>
        <div class="hash">{{ p.fileHash }}</div>
        <h4>位置</h4>
        @for (l of p.locations; track l.relPath) {
          <div class="loc">{{ l.relPath }} <span style="color:var(--muted)">· {{ l.status }}</span></div>
        }
        <h4>標籤</h4>
        @for (t of p.tags; track t.id) {
          <span class="chip" [style.color]="color(t.kind)" [style.borderColor]="color(t.kind)"
                [style.borderStyle]="t.source === 'wd14' ? 'dashed' : 'solid'">
            {{ t.name }}@if (t.confidence != null) { <span> {{ (t.confidence * 100) | number:'1.0-0' }}%</span> }
          </span>
        }
        @if (p.cameraModel) { <p class="hash">📷 {{ p.cameraModel }}</p> }
      </div>
    } @else {
      <div class="empty">選一張圖看細節</div>
    }
  `,
})
export class Inspector {
  api = inject(PmApi);
  photo = signal<PhotoDetail | null>(null);
  color = tagColor;

  private _id: number | null = null;
  @Input() set photoId(v: number | null) {
    this._id = v;
    if (v == null) { this.photo.set(null); return; }
    this.api.photo(v).then(d => this.photo.set(d));
  }
}
```

(若模板用到 `number` pipe,記得在 `imports` 加 `CommonModule` 或改用 standalone 的 `DecimalPipe`。)

- [ ] **Step 2: build 驗證 + Commit**

Run:

```bash
cd /d/picture-management/src/Pm.Web
npm run build
git add src/Pm.Web
git commit -m "feat: 檢視器(身分→位置簽名 + booru 分色 tags + WD14 虛線 + EXIF)"
```

Expected: build 成功。

- [ ] **Step 3: [手動] 視覺檢視**

Run(需先有資料:用前面計畫的 API 建 root + 掃描真圖):

```bash
cd /d/picture-management
dotnet run --project src/Pm.Api
# 瀏覽器開 http://localhost:5180,確認三欄、縮圖瀑布、點圖出檢視器、分色 chip
```

對照 `docs/mockups/ui-preview.html`。

---

## Task 5: 管理畫面(來源/掃描、待確認匣、路徑→tag 確認)

**Files:**
- Create: `src/Pm.Web/src/app/manage/roots.ts`、`reconcile.ts`、`pending.ts`
- Modify: `src/Pm.Web/src/app/shell/workbench.ts`(活動列切換視圖)

**Interfaces:**
- Consumes: `PmApi.roots/createRoot/scan/missing/pendingSegments/applyRule`。
- Produces:三個視圖元件,經活動列切換進主區。

- [ ] **Step 1: 來源/掃描**

Create `src/Pm.Web/src/app/manage/roots.ts`:

```typescript
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PmApi, Root } from '../api/pm-api';

@Component({
  selector: 'app-roots', standalone: true, imports: [FormsModule],
  styles: [`.pad{padding:16px} .root{padding:8px;border:1px solid var(--line);border-radius:8px;margin:6px 0}
    input,button{padding:8px;margin-right:6px;background:var(--panel-2);color:var(--text);border:1px solid var(--line);border-radius:6px}`],
  template: `
    <div class="pad">
      <h3>圖庫來源</h3>
      <input [(ngModel)]="name" placeholder="名稱" />
      <input [(ngModel)]="path" placeholder="絕對路徑" style="width:320px" />
      <button (click)="add()">新增</button>
      @for (r of roots(); track r.id) {
        <div class="root">
          <b>{{ r.name }}</b> <span style="color:var(--muted)">{{ r.absPath }}</span>
          <button (click)="scan(r.id)">重新掃描</button>
        </div>
      }
    </div>`,
})
export class Roots {
  api = inject(PmApi);
  roots = signal<Root[]>([]);
  name = ''; path = '';
  async ngOnInit() { this.roots.set(await this.api.roots()); }
  async add() { if (this.name && this.path) { await this.api.createRoot(this.name, this.path); this.roots.set(await this.api.roots()); } }
  async scan(id: number) { await this.api.scan(id); }
}
```

- [ ] **Step 2: 待確認匣**

Create `src/Pm.Web/src/app/manage/reconcile.ts`:

```typescript
import { Component, inject, signal } from '@angular/core';
import { PmApi } from '../api/pm-api';

@Component({
  selector: 'app-reconcile', standalone: true,
  styles: [`.pad{padding:16px} .row{padding:8px;border:1px solid var(--line);border-radius:8px;margin:6px 0;font-size:13px}`],
  template: `
    <div class="pad">
      <h3>待確認:可能失蹤的圖</h3>
      <p style="color:var(--muted)">只列出所有位置都已不存在的圖(搬移的會自動續接,不在此)。</p>
      @for (m of missing(); track m.id) {
        <div class="row"><span class="">{{ m.paths.join(', ') }}</span></div>
      } @empty { <p style="color:var(--muted)">沒有待確認項目 🎉</p> }
    </div>`,
})
export class Reconcile {
  api = inject(PmApi);
  missing = signal<{ id: number; fileHash: string; paths: string[] }[]>([]);
  async ngOnInit() { this.missing.set(await this.api.missing()); }
}
```

- [ ] **Step 3: 路徑→tag 確認**

Create `src/Pm.Web/src/app/manage/pending.ts`:

```typescript
import { Component, inject, signal, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PmApi, PendingSegment } from '../api/pm-api';

@Component({
  selector: 'app-pending', standalone: true, imports: [FormsModule],
  styles: [`.pad{padding:16px} .seg{display:grid;grid-template-columns:1fr auto auto auto;gap:8px;align-items:center;
    padding:8px;border:1px solid var(--line);border-radius:8px;margin:6px 0}
    select,button{padding:6px;background:var(--panel-2);color:var(--text);border:1px solid var(--line);border-radius:6px}`],
  template: `
    <div class="pad">
      <h3>匯入確認:路徑 → 標籤</h3>
      @for (s of segs(); track s.segment) {
        <div class="seg">
          <span><b>{{ s.segment }}</b> <span style="color:var(--muted)">×{{ s.count }} · {{ s.samplePath }}</span></span>
          <select #act [value]="s.suggestedAction">
            <option value="map_to_tag">對應標籤</option>
            <option value="ignore">略過</option>
            <option value="meta_year">年份</option>
          </select>
          <button (click)="apply(s, act.value)">套用</button>
        </div>
      } @empty { <p style="color:var(--muted)">沒有新的路徑段要確認。</p> }
    </div>`,
})
export class Pending {
  api = inject(PmApi);
  @Input() rootId = 0;
  segs = signal<PendingSegment[]>([]);
  async ngOnInit() { if (this.rootId) this.segs.set(await this.api.pendingSegments(this.rootId)); }
  async apply(s: PendingSegment, action: string) {
    await this.api.applyRule({ rootId: this.rootId, segment: s.segment, action, tagName: s.segment });
    this.segs.update(x => x.filter(p => p.segment !== s.segment));
  }
}
```

- [ ] **Step 4: 活動列切換**

把 `workbench.ts` 的活動列加幾個按鈕,用 signal 切換主區顯示 `Gallery` / `Roots` / `Reconcile` / `Pending`(以 `@if` 切換)。import 對應元件。

- [ ] **Step 5: build 驗證 + 全前端測試 + Commit**

Run:

```bash
cd /d/picture-management/src/Pm.Web
npm run build
npx ng test --watch=false --browsers=ChromeHeadless
cd /d/picture-management
git add src/Pm.Web
git commit -m "feat: 管理畫面(來源/掃描、待確認匣、路徑→tag 確認)+ 活動列切換"
```

Expected: build 成功、spec 綠。

- [ ] **Step 6: [手動] 端到端視覺驗收**

`dotnet run` 後在瀏覽器:新增 root → 掃描 → 相簿出現縮圖 → 待確認段 → 套規則 → 搜尋該 tag → 點圖看檢視器。對照 mockup。

---

## 完成定義(Angular 相簿)

- `ng build` 成功並由 .NET serve(同源);`/` 出 SPA、`/api/*` 走 API。
- 相簿:CDK virtual scroll + 布林搜尋 + keyset 無限捲 + 縮圖。
- 檢視器:身分→位置 + booru 分色 tags(WD14 虛線 + 信心 %)+ EXIF。
- 管理:來源/掃描、待確認匣、路徑→tag 確認。
- `PmApi` spec 綠;人工視覺對照 mockup 通過。

**明確不在本計畫:** 階層樹的完整展開 UI(可後續增強;查詢端 implication 已支援)、WD14 建議的接受/拒絕互動(待計畫 7 有 tag 資料後)、Saved Search UI(端點已具,UI 後續)。

---

## Self-Review 註記

- **Spec 覆蓋:** §6.1 暗色三欄 + 分色、§6.2 圖庫/檢視器/匯入確認/失蹤匣/來源五畫面(核心版)、§5.3 keyset 瀏覽。
- **誠實標註:** 前端以 build + service spec + 人工視覺把關;`[手動]` 步驟需使用者看畫面。
- **環境風險:** `npm install` / headless 測試需 Node(已裝 v24)與瀏覽器(用先前 chromium,必要時設 `CHROME_BIN`)。
```
