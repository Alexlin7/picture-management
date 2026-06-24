# 縮圖佔位狀態 + 新增來源自動掃描 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 圖牆縮圖在掃描空窗/失敗時以 skeleton + 自動重試 + 靜態佔位取代瀏覽器破圖;新增圖庫來源後自動排掃描並回饋。

**Architecture:** 新增可重用單檔元件 `app-thumb`(`@core/ui/thumb`,自管 loading→loaded→broken 狀態機 + 指數退避重試),`photo-grid` 改用它。新增來源沿用既有 `onRescan` 流程:`ManageStore.createRoot` 改回傳建立的 `Root`,`roots.ts submitAdd` 拿到 id 後觸發掃描。

**Tech Stack:** Angular(standalone + signals)、Tailwind v4(全域 `@layer` primitive + 元件 scoped `var(--token)`)、vitest + `@angular/build:unit-test`(TestBed)、HttpTestingController。

## Global Constraints

- 元件 scoped 樣式(inline `styles:` 或元件 `.css`)**不得** `@apply`/`@tailwind`/`@reference`;只能手寫 + `var(--token)`。共用 `@apply` 只在全域 `styles.css`。(CLAUDE.md 前端樣式鐵則)
- 顏色一律走 token(`var(--color-*)`),不寫裸 hex。
- 新互動/視覺元件勿用 `outline:none` 蓋掉全域 `:focus-visible` ring。
- `@core/*` path alias → `src/app/core/*`(`tsconfig.json`)。
- `@core/ui` 慣例:單檔 component,inline `template` + `styles`(見 `core/ui/toast.ts`)。
- 縮圖端點:`PmApi.thumbUrl(id)` 回 `/api/photos/${id}/thumb`;絕不碰原圖。
- 測試單次執行指令:`npm test`(於 `src/Pm.Web`;`@angular/build:unit-test` 預設跑一次後結束)。

---

### Task 1: `Thumb` 元件(skeleton + 自動重試 + 失敗佔位)

**Files:**
- Create: `src/Pm.Web/src/app/core/ui/thumb.ts`
- Test: `src/Pm.Web/src/app/core/ui/thumb.spec.ts`

**Interfaces:**
- Consumes: `PmApi.thumbUrl(id: number): string`(既有)。
- Produces: 元件 `Thumb`,selector `app-thumb`,inputs `photoId: number`(required)、`aspectRatio: string`(預設 `'1/1'`)、`alt: string`(預設 `''`);public 方法 `onLoad(): void`、`onError(): void`;public signal `state(): 'loading' | 'loaded' | 'broken'`。

- [ ] **Step 1: Write the failing test**

建立 `src/Pm.Web/src/app/core/ui/thumb.spec.ts`:

```typescript
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { vi } from 'vitest';
import { Thumb } from './thumb';

describe('Thumb', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Thumb],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  function make(photoId = 1) {
    const fixture = TestBed.createComponent(Thumb);
    fixture.componentRef.setInput('photoId', photoId);
    fixture.detectChanges();
    return fixture;
  }

  it('starts in loading state with skeleton', () => {
    const fixture = make();
    expect(fixture.componentInstance.state()).toBe('loading');
    expect(fixture.nativeElement.querySelector('.skeleton')).toBeTruthy();
  });

  it('goes to loaded on img load and removes skeleton', () => {
    const fixture = make();
    fixture.componentInstance.onLoad();
    fixture.detectChanges();
    expect(fixture.componentInstance.state()).toBe('loaded');
    expect(fixture.nativeElement.querySelector('.skeleton')).toBeFalsy();
  });

  it('retries on error then falls back to broken after exhausting retries', () => {
    vi.useFakeTimers();
    const fixture = make();
    const cmp = fixture.componentInstance;
    // 初次 + 5 次重試 = 第 6 次 error 才轉 broken
    for (let i = 0; i < 6; i++) {
      cmp.onError();
      vi.runAllTimers();
    }
    fixture.detectChanges();
    expect(cmp.state()).toBe('broken');
    expect(fixture.nativeElement.querySelector('.broken')).toBeTruthy();
    vi.useRealTimers();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`(於 `src/Pm.Web`)
Expected: FAIL —— 無法解析 `./thumb`(模組不存在)。

- [ ] **Step 3: Write minimal implementation**

建立 `src/Pm.Web/src/app/core/ui/thumb.ts`:

```typescript
import { Component, Input, OnChanges, OnDestroy, computed, inject, signal } from '@angular/core';
import { PmApi } from '@core/api/pm-api';

type ThumbState = 'loading' | 'loaded' | 'broken';

// 縮圖載入狀態機:撐過掃描中縮圖還沒產生的空窗(skeleton + 指數退避重試),
// 縮圖一生出來就自動補上;真的失敗(壞檔/重試耗盡)才落到靜態「無縮圖」佔位。
// 絕不碰原圖:src 一律走 PmApi.thumbUrl。
@Component({
  selector: 'app-thumb',
  imports: [],
  template: `
    <div class="thumb" [style.aspect-ratio]="aspectRatio">
      @if (state() !== 'broken') {
        <img
          class="img"
          [class.ready]="state() === 'loaded'"
          [src]="src()"
          [alt]="alt"
          loading="lazy"
          (load)="onLoad()"
          (error)="onError()"
        />
      }
      @if (state() === 'loading') {
        <div class="skeleton ph" aria-hidden="true"></div>
      }
      @if (state() === 'broken') {
        <div class="ph broken" aria-hidden="true">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6">
            <rect x="3" y="3" width="18" height="18" rx="2" />
            <circle cx="8.5" cy="8.5" r="1.5" />
            <path d="m21 15-5-5L5 21" />
          </svg>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; width: 100%; }
    .thumb {
      position: relative;
      width: 100%;
      overflow: hidden;
      background: var(--color-raised);
      border-radius: var(--radius-card);
    }
    .img {
      width: 100%;
      height: 100%;
      display: block;
      object-fit: cover;
      opacity: 0;
      transition: opacity 0.2s ease;
    }
    .img.ready { opacity: 1; }
    .ph { position: absolute; inset: 0; }
    .broken {
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--color-faint);
      background: var(--color-raised);
    }
    .broken svg { width: 30%; max-width: 48px; height: auto; }
  `],
})
export class Thumb implements OnChanges, OnDestroy {
  private readonly api = inject(PmApi);

  @Input({ required: true }) photoId!: number;
  @Input() aspectRatio = '1/1';
  @Input() alt = '';

  // 退避序列(ms);耗盡即 broken。初次 + 這 5 次 = 約 10s,覆蓋掃描中縮圖空窗。
  private static readonly RETRY_DELAYS = [400, 800, 1600, 3000, 5000];

  readonly state = signal<ThumbState>('loading');
  private readonly attempt = signal(0);
  private timer: ReturnType<typeof setTimeout> | null = null;

  // 目前 src:第一次無 query;重試帶遞增 cache-bust(?r=n)強制 img 重新載入。
  readonly src = computed(() => {
    const base = this.api.thumbUrl(this.photoId);
    const a = this.attempt();
    return a === 0 ? base : `${base}?r=${a}`;
  });

  ngOnChanges(): void {
    // photoId 變動(同 tile 被重用)→ 重置狀態機。
    this.clearTimer();
    this.attempt.set(0);
    this.state.set('loading');
  }

  onLoad(): void {
    this.clearTimer();
    this.state.set('loaded');
  }

  onError(): void {
    const next = this.attempt();
    if (next >= Thumb.RETRY_DELAYS.length) {
      this.state.set('broken');
      return;
    }
    // 維持 loading(skeleton 持續),退避後遞增 attempt → src 改變 → img 重載。
    this.timer = setTimeout(() => this.attempt.set(next + 1), Thumb.RETRY_DELAYS[next]);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  ngOnDestroy(): void {
    this.clearTimer();
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`(於 `src/Pm.Web`)
Expected: PASS —— Thumb 三個 case 全綠(其餘既有 spec 不受影響)。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/ui/thumb.ts src/Pm.Web/src/app/core/ui/thumb.spec.ts
git commit -m "feat(web): Thumb 元件(skeleton + 退避重試 + 失敗佔位)"
```

---

### Task 2: `photo-grid` 改用 `<app-thumb>`

**Files:**
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html:96`

**Interfaces:**
- Consumes: Task 1 的 `Thumb`(selector `app-thumb`,inputs `photoId` / `aspectRatio`)。
- Produces: 無新公開介面(純接線)。

- [ ] **Step 1: 匯入 Thumb 並移除死碼**

於 `photo-grid.ts` 頂部 import 區加入:

```typescript
import { Thumb } from '@core/ui/thumb';
```

把 `@Component` 的 `imports: []` 改為:

```typescript
  imports: [Thumb],
```

移除不再使用的 `thumb(id)` 方法(`photo-grid.ts:45-48`,template 改用後即無引用):

```typescript
  // (刪除整段)
  // 縮圖 URL(依 hash,絕不碰原圖)
  // thumb(id: number): string {
  //   return this.store.thumbUrl(id);
  // }
```

保留 `aspect(p)` 不動(Task 仍用它傳 `aspectRatio`)。

- [ ] **Step 2: 換掉 template 的 `<img>`**

`photo-grid.html:96`,把:

```html
          <img class="art" [src]="thumb(p.id)" [style.aspect-ratio]="aspect(p)" loading="lazy" alt="" />
```

改成(不加 `class="art"`：避免觸發 `.tile .art::after` 漸層;`app-thumb` host 已 `display:block; width:100%` 自填欄寬):

```html
          <app-thumb [photoId]="p.id" [aspectRatio]="aspect(p)" />
```

- [ ] **Step 3: build 驗證**

Run: `npm run build`(於 `src/Pm.Web`)
Expected: `Application bundle generation complete.`,無編譯錯誤(尤其無「`thumb` is not defined」「`Thumb` 未匯入」類錯)。

- [ ] **Step 4: 手測(起 app 看圖牆)**

於 repo 根:`dotnet run --project src/Pm.Api`,瀏覽器開 `http://localhost:5180`:
- 圖牆縮圖正常顯示、`.tile` 版面與比例不跑掉(無變形、無破圖)。
- (若手邊有掃描中情境)新掃描期間縮圖先 skeleton、之後自動補上。
Expected: 縮圖正常;無瀏覽器預設破圖 icon。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html
git commit -m "feat(web): 圖牆改用 app-thumb(skeleton/重試/佔位)取代裸 img"
```

---

### Task 3: `ManageStore.createRoot` 改回傳建立的 `Root`

**Files:**
- Modify: `src/Pm.Web/src/app/features/manage/manage.store.ts:108-111`
- Test: `src/Pm.Web/src/app/features/manage/manage.store.spec.ts`

**Interfaces:**
- Consumes: `PmApi.createRoot(name, absPath): Promise<Root>`(既有)、`PmApi.roots(): Promise<Root[]>`(既有)。
- Produces: `ManageStore.createRoot(name: string, absPath: string): Promise<Root>`(回傳值由 `void` 變 `Root`)。

- [ ] **Step 1: Write the failing test**

建立 `src/Pm.Web/src/app/features/manage/manage.store.spec.ts`:

```typescript
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ManageStore } from './manage.store';

describe('ManageStore.createRoot', () => {
  let store: ManageStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    store = TestBed.inject(ManageStore);
    http = TestBed.inject(HttpTestingController);
  });

  it('returns the created root', async () => {
    const p = store.createRoot('My Lib', 'D:\\pics');

    const createReq = http.expectOne('/api/roots');           // POST 建立
    expect(createReq.request.method).toBe('POST');
    createReq.flush({ id: 42, name: 'My Lib', absPath: 'D:\\pics' });

    await new Promise((r) => setTimeout(r, 0));                // 讓 createRoot 續跑到 loadRoots
    const listReq = http.expectOne('/api/roots');             // GET 刷新清單
    expect(listReq.request.method).toBe('GET');
    listReq.flush([{ id: 42, name: 'My Lib', absPath: 'D:\\pics' }]);

    const root = await p;
    expect(root.id).toBe(42);
    expect(root.name).toBe('My Lib');
    http.verify();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`(於 `src/Pm.Web`)
Expected: FAIL —— `createRoot` 目前回 `Promise<void>`,`root.id` 為 TS 型別錯誤/執行期 `undefined`。

- [ ] **Step 3: Write minimal implementation**

`manage.store.ts:108-111`,把:

```typescript
  async createRoot(name: string, absPath: string): Promise<void> {
    await this.api.createRoot(name, absPath);
    await this.loadRoots();
  }
```

改成:

```typescript
  async createRoot(name: string, absPath: string): Promise<Root> {
    const root = await this.api.createRoot(name, absPath);
    await this.loadRoots();
    return root;
  }
```

(`Root` 型別已於檔首 `import { PmApi, type Root, ... }` 匯入,無需新增 import。)

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`(於 `src/Pm.Web`)
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/features/manage/manage.store.ts src/Pm.Web/src/app/features/manage/manage.store.spec.ts
git commit -m "feat(web): ManageStore.createRoot 回傳建立的 Root(供新增後自動掃描)"
```

---

### Task 4: 新增來源後自動掃描(`roots.ts submitAdd`)

**Files:**
- Modify: `src/Pm.Web/src/app/features/manage/roots/roots.ts:51-62`

**Interfaces:**
- Consumes: Task 3 的 `ManageStore.createRoot(...): Promise<Root>`;既有 `Roots.onRescan(id: number): Promise<void>`(已含 scanning 狀態標記 + 輪詢 + 完成/失敗 toast)。
- Produces: 無新公開介面。

- [ ] **Step 1: 改 `submitAdd` 接上自動掃描**

`roots.ts:51-62`,把:

```typescript
  async submitAdd(absPath: string, name: string, pathInput: HTMLInputElement): Promise<void> {
    const p = absPath.trim();
    if (!p) {
      this.toast.error('請輸入來源資料夾的絕對路徑');
      pathInput.focus();
      return;
    }
    const n = name.trim() || p;
    await this.store.createRoot(n, p);
    this.toast.success(`已新增來源「${n}」`);
    this.adding.set(false);
  }
```

改成:

```typescript
  async submitAdd(absPath: string, name: string, pathInput: HTMLInputElement): Promise<void> {
    const p = absPath.trim();
    if (!p) {
      this.toast.error('請輸入來源資料夾的絕對路徑');
      pathInput.focus();
      return;
    }
    const n = name.trim() || p;
    const root = await this.store.createRoot(n, p);
    this.toast.success(`已新增來源「${n}」,開始掃描…`);
    this.adding.set(false);
    // 新增即自動掃描:非破壞性、且是新增來源天經地義的下一步(不跳 confirm)。
    // onRescan 已處理 scanning 狀態 + 輪詢 + 完成/失敗 toast。
    await this.onRescan(root.id);
  }
```

- [ ] **Step 2: build 驗證**

Run: `npm run build`(於 `src/Pm.Web`)
Expected: `Application bundle generation complete.`,無編譯錯誤。

- [ ] **Step 3: 手測(起 app 新增來源)**

於 repo 根:`dotnet run --project src/Pm.Api`,瀏覽器開 `http://localhost:5180` →「圖庫來源」頁:
- 新增一個來源(填一個真實存在的資料夾絕對路徑)。
- Expected:toast「已新增來源「…」,開始掃描…」→ 該列進入掃描中狀態 → 完成後 toast「掃描完成:新增 N 張…」。
- 失敗情境(填不存在路徑或掃描出錯):toast 顯示掃描啟動/失敗訊息,來源仍已建立(不 rollback)。

- [ ] **Step 4: Commit**

```bash
git add src/Pm.Web/src/app/features/manage/roots/roots.ts
git commit -m "feat(web): 新增圖庫來源後自動排掃描 + toast(不跳 confirm)"
```

---

## 驗收(全部完成後)

- [ ] `npm test`(`src/Pm.Web`)全綠;`npm run build` 成功。
- [ ] 起 app 手測兩情境:掃描中縮圖 skeleton→自動補上、壞檔落佔位;新增來源自動掃描 + toast。
- [ ] 工作樹乾淨(`launchSettings.json` 仍為本機設定、不提交)。

## 不在本計畫範圍(另開)

- reconcile / inspector 改用 `<app-thumb>`(可重用收尾)。
- 孤兒 photo 清理(維護端點)。
- per-root「重產縮圖」維護入口。
