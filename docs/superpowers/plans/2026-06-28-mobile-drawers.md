# ③g 完整手機版抽屜式側板 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 窄寬(< 768px stage)時把 facet/資料夾樹與 inspector 兩個側板改成共用一支 `DrawerPanel` 的覆蓋式抽屜,圖牆吃滿寬,根治窄寬擠爆與浮動關閉鈕疊住內容按鈕的打地鼠問題。

**Architecture:** 新增共用 `core/ui/drawer-panel.ts`(scrim + 滑入面板,header 自帶標題與關閉 X、CDK FocusTrap、Esc/scrim 關)。gallery-view / browse-view 依 `stageWidth < MOBILE` 的 `mobile()` computed 切換:桌面維持現有三欄完全不變;手機把兩側板塞進抽屜,grid topbar 露出「篩選 / 資料夾」鈕開左抽屜、點圖(含鍵盤選取)自動開右抽屜。⤢ 放大仍走既有 lightbox。

**Tech Stack:** Angular 22(standalone、signals input()/output()/computed/effect、@if 控制流)、@angular/cdk A11yModule(cdkTrapFocus/cdkTrapFocusAutoCapture)、Tailwind v4 + Angular 隔離編譯、Playwright e2e(.mjs)、vitest(ng test)。

## Global Constraints

- 溝通與註解用**繁體中文(台灣用語)**;程式碼識別子保留原文。
- **桌面(stage ≥ 768px)行為完全不變** —— 三欄 + edge 箭頭 + 手動覆寫照舊。不動後端、資料模型、查詢邏輯,不改 rail 導覽形式,不重寫 facet/inspector/folder-tree 內容本身。
- 元件 `.css` / `styles:[]`(component-scoped)**禁用** `@apply`/`@tailwind`/`@reference`;顏色/字體/圓角/陰影一律 `var(--token)`,不寫裸 hex。`::ng-deep` 不在此禁令內,允許用於收束投影進抽屜的子元件尺寸。
- 不可殺全域 `:focus-visible` cyan ring;新互動元件勿 `outline:none`。
- 觸控目標 ≥ 44px(rail、篩選鈕、抽屜 header X)。
- 抽屜 z-index ~600,**必須低於 lightbox(1200)**。
- 斷點單一真相源是 `core/layout-breakpoints.ts`(TS 常數;CSS @media 不能吃 var())。
- 編輯器對 `@core/*` import 報「Cannot find module」是 TS server 誤報;`ng build`/`ng test` 為準。
- 前端單次測試:`npx ng test --watch=false`(別用裸 `npx vitest`)。e2e 需先 `dotnet run` 起 app 在 :5180,再 `node e2e/<name>.mjs`。
- commit 訊息結尾固定兩行:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
  `Claude-Session: https://claude.ai/code/session_011eRUKVb3SSbr1Dtknj25WY`
- 分支 `feat/mobile-drawers`(spec 已 commit 於此);**不可直接 push main**,完工開 PR 由使用者在手機合併。
- **不可提交** `src/Pm.Api/Properties/launchSettings.json`(per-machine)與 repo 根的使用者截圖。

---

## File Structure

- **新增** `src/Pm.Web/src/app/core/ui/drawer-panel.ts` — 共用抽屜元件(scrim + 面板 + header,投影內容)。單一責任:覆蓋式側板殼。
- **新增** `src/Pm.Web/src/app/core/ui/drawer-panel.spec.ts` — DrawerPanel 單元測試。
- **新增** `src/Pm.Web/e2e/mobile-drawers-smoke.mjs` — 手機抽屜 e2e(viewport < 768)。
- **改** `src/Pm.Web/src/app/core/layout-breakpoints.ts` — 加 `MOBILE = 768`。
- **改** `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts` / `.html` / `.css` — 加 `mobile` input、`openFilter`/`opened` outputs、「篩選」鈕。
- **改** `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.ts` / `.html` / `.css` — 加 `mobile` input、`openFilter`/`opened` outputs、「資料夾」鈕。
- **改** `src/Pm.Web/src/app/features/gallery/gallery-view/gallery-view.ts` — mobile computed、抽屜 signals/handlers、template、::ng-deep 尺寸收束。
- **改** `src/Pm.Web/src/app/features/browse/browse-view/browse-view.ts` — 同上(folder-tree 版)。
- **改** `src/Pm.Web/package.json` — 加 `e2e:mobile` script。

---

## Task 1: DrawerPanel 共用抽屜元件

**Files:**
- Create: `src/Pm.Web/src/app/core/ui/drawer-panel.ts`
- Test: `src/Pm.Web/src/app/core/ui/drawer-panel.spec.ts`

**Interfaces:**
- Consumes: `@angular/cdk/a11y` 的 `A11yModule`(cdkTrapFocus / cdkTrapFocusAutoCapture)。
- Produces:
  - `class DrawerPanel`,selector `app-drawer-panel`。
  - inputs:`open: boolean`(預設 false)、`side: 'left' | 'right'`(預設 'left')、`title: string`(預設 '')。
  - output:`close: void`。
  - 投影:`<ng-content />`。
  - DOM 約定(供測試/e2e/view 取用):scrim `.dp-scrim`、面板 `.dp-panel`(+ `.left`/`.right`)、標題 `.dp-title`、關閉鈕 `.dp-close`、內容區 `.dp-body`。`open()` 為 false 時整個 `.dp-scrim` 不渲染。

- [ ] **Step 1: 寫失敗測試**

Create `src/Pm.Web/src/app/core/ui/drawer-panel.spec.ts`:

```ts
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DrawerPanel } from './drawer-panel';

@Component({
  standalone: true,
  imports: [DrawerPanel],
  template: `
    <app-drawer-panel [open]="open()" [side]="side()" title="測試抽屜" (close)="closed.set(closed() + 1)">
      <p class="proj">投影內容</p>
    </app-drawer-panel>
  `,
})
class Host {
  readonly open = signal(true);
  readonly side = signal<'left' | 'right'>('left');
  readonly closed = signal(0);
}

describe('DrawerPanel', () => {
  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root };
  }

  it('open=true 時渲染面板與投影內容', () => {
    const { root } = setup();
    expect(root.querySelector('.dp-panel')).toBeTruthy();
    expect(root.querySelector('.proj')?.textContent).toContain('投影內容');
    expect(root.querySelector('.dp-title')?.textContent).toContain('測試抽屜');
  });

  it('open=false 時不渲染', () => {
    const { fixture, root } = setup();
    fixture.componentInstance.open.set(false);
    fixture.detectChanges();
    expect(root.querySelector('.dp-scrim')).toBeNull();
  });

  it('side 對應 class(left / right)', () => {
    const { fixture, root } = setup();
    expect(root.querySelector('.dp-panel.left')).toBeTruthy();
    fixture.componentInstance.side.set('right');
    fixture.detectChanges();
    expect(root.querySelector('.dp-panel.right')).toBeTruthy();
  });

  it('點關閉 X 觸發一次 (close)', () => {
    const { fixture, root } = setup();
    (root.querySelector('.dp-close') as HTMLButtonElement).click();
    expect(fixture.componentInstance.closed()).toBe(1);
  });

  it('點 scrim(面板外)觸發 (close)', () => {
    const { fixture, root } = setup();
    (root.querySelector('.dp-scrim') as HTMLElement).click();
    expect(fixture.componentInstance.closed()).toBe(1);
  });

  it('點面板內部不觸發 (close)', () => {
    const { fixture, root } = setup();
    (root.querySelector('.dp-panel') as HTMLElement).click();
    expect(fixture.componentInstance.closed()).toBe(0);
  });

  it('Esc 觸發 (close)', () => {
    const { fixture, root } = setup();
    const scrim = root.querySelector('.dp-scrim') as HTMLElement;
    scrim.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(fixture.componentInstance.closed()).toBe(1);
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd src/Pm.Web && npx ng test --watch=false`
Expected: FAIL —— 找不到模組 `./drawer-panel`(尚未建立)。

- [ ] **Step 3: 寫最小實作**

Create `src/Pm.Web/src/app/core/ui/drawer-panel.ts`:

```ts
import { Component, input, output } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';

/**
 * 共用覆蓋式抽屜側板(防打地鼠核心):facet / 資料夾樹 / inspector 共用同一支。
 * scrim + 滑入面板;面板頂部 header(標題 + 關閉 X)永不蓋內容 → ⤢ 等內容按鈕不被疊。
 * open=false 不渲染;關閉後焦點還原由 cdkTrapFocusAutoCapture 負責。
 */
@Component({
  selector: 'app-drawer-panel',
  imports: [A11yModule],
  template: `
    @if (open()) {
      <div class="dp-scrim" (click)="onScrim($event)" (keydown.escape)="close.emit()">
        <div
          class="dp-panel"
          [class.left]="side() === 'left'"
          [class.right]="side() === 'right'"
          cdkTrapFocus
          cdkTrapFocusAutoCapture
          role="dialog"
          aria-modal="true"
          [attr.aria-label]="title()"
        >
          <header class="dp-head">
            <span class="dp-title">{{ title() }}</span>
            <button class="dp-close" type="button" (click)="close.emit()" aria-label="關閉" title="關閉">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true">
                <line x1="6" y1="6" x2="18" y2="18" /><line x1="18" y1="6" x2="6" y2="18" />
              </svg>
            </button>
          </header>
          <div class="dp-body"><ng-content /></div>
        </div>
      </div>
    }
  `,
  styles: [`
    .dp-scrim {
      position: fixed; inset: 0; z-index: 600; display: flex;
      background: rgba(8, 9, 12, 0.6); backdrop-filter: blur(2px);
      animation: dp-fade 0.18s var(--ease-out, ease-out);
    }
    .dp-panel {
      display: flex; flex-direction: column; height: 100%;
      background: var(--color-panel); box-shadow: 0 0 40px -8px rgba(0, 0, 0, 0.6);
    }
    .dp-panel.left {
      margin-right: auto; width: 86vw; max-width: 330px;
      border-right: 1px solid var(--color-hair);
      animation: dp-slide-l 0.2s var(--ease-out, ease-out);
    }
    .dp-panel.right {
      margin-left: auto; width: 92vw; max-width: 360px;
      border-left: 1px solid var(--color-hair);
      animation: dp-slide-r 0.2s var(--ease-out, ease-out);
    }
    .dp-head {
      display: flex; align-items: center; gap: 8px; flex: none;
      min-height: 48px; padding: 8px 10px 8px 14px;
      border-bottom: 1px solid var(--color-hair);
    }
    .dp-title { font-family: var(--font-display); font-weight: 600; font-size: 15px; }
    .dp-close {
      margin-left: auto; width: 44px; height: 44px; display: inline-grid; place-items: center;
      border: 1px solid var(--color-hair); border-radius: var(--radius-soft);
      background: var(--color-raised); color: var(--color-text); cursor: pointer;
      transition: border-color var(--dur-fast, 0.12s) var(--ease-out, ease-out),
                  color var(--dur-fast, 0.12s) var(--ease-out, ease-out);
    }
    .dp-close:hover { border-color: var(--color-danger); color: var(--color-danger); }
    .dp-close svg { width: 18px; height: 18px; }
    .dp-body { flex: 1 1 auto; min-height: 0; overflow: hidden; }
    @keyframes dp-fade { from { opacity: 0; } to { opacity: 1; } }
    @keyframes dp-slide-l { from { transform: translateX(-100%); } to { transform: none; } }
    @keyframes dp-slide-r { from { transform: translateX(100%); } to { transform: none; } }
    @media (prefers-reduced-motion: reduce) {
      .dp-scrim, .dp-panel.left, .dp-panel.right { animation: none; }
      .dp-close { transition: none; }
    }
  `],
})
export class DrawerPanel {
  readonly open = input(false);
  readonly side = input<'left' | 'right'>('left');
  readonly title = input('');
  readonly close = output<void>();

  // 只在點到最外層 scrim(非面板/內容)時關閉。
  onScrim(e: MouseEvent): void {
    if ((e.target as HTMLElement).classList.contains('dp-scrim')) this.close.emit();
  }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd src/Pm.Web && npx ng test --watch=false`
Expected: PASS —— DrawerPanel 7 個測試全綠,既有測試不受影響。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/ui/drawer-panel.ts src/Pm.Web/src/app/core/ui/drawer-panel.spec.ts
git commit -m "feat(web): 新增共用 DrawerPanel 覆蓋式抽屜側板(③g)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_011eRUKVb3SSbr1Dtknj25WY"
```

---

## Task 2: MOBILE 斷點 + grid 元件露出「篩選 / 資料夾」鈕與輸出

**Files:**
- Modify: `src/Pm.Web/src/app/core/layout-breakpoints.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html`
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.css`
- Modify: `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.ts`
- Modify: `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.html`
- Modify: `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.css`

**Interfaces:**
- Consumes: 無(純加 input/output)。
- Produces:
  - `layout-breakpoints.ts` 匯出 `MOBILE = 768`。
  - `PhotoGrid`:`mobile = input(false)`、`openFilter = output<void>()`、`opened = output<void>()`;`onActivate` 結尾 `this.opened.emit()`;topbar `@if (mobile())` 顯示「篩選」鈕 `(click)="openFilter.emit()"`,class `.filter-btn`。
  - `BrowseGrid`:同上,鈕文案「資料夾」、aria「開啟資料夾樹」。
- **設計取捨(與 spec 微調並記錄理由):** 鈕的顯示由 `mobile()` signal(來源 = stageWidth,不含 rail 58px)控制,**不用 CSS @media(viewport 寬)**。因為 stageWidth = viewport − rail,若用 viewport @media 會在 768 邊界與 `mobile()` 不同步(viewport 768 → stage ~710 已進手機模式,但 @media(max-width:767px) 仍隱藏鈕)。用同一 signal 保證單一真相、邊界一致。

- [ ] **Step 1: 加 MOBILE 斷點常數**

Modify `src/Pm.Web/src/app/core/layout-breakpoints.ts` —— 在 `FACET_COLLAPSE` 行下方加一行:

```ts
export const INSPECTOR_COLLAPSE = 1180; // stage 寬 < 此 → inspector 自動收
export const FACET_COLLAPSE = 940;      // stage 寬 < 此 → facet / 資料夾樹 自動收
export const MOBILE = 768;              // stage 寬 < 此 → 手機抽屜模式(兩側板改覆蓋式抽屜)
```

- [ ] **Step 2: PhotoGrid 加 input/output 與 emit**

Modify `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts`:

匯入加 `input, output`(第 1 行):

```ts
import { Component, computed, inject, signal, input, output, ElementRef, ViewChild, AfterViewInit, OnDestroy } from '@angular/core';
```

在 class 內(`viewMode` signal 附近)加:

```ts
  // ③g:由 gallery-view 傳入是否手機抽屜模式;true → 顯示「篩選」鈕。
  readonly mobile = input(false);
  // 點「篩選」鈕 → 請上層開左抽屜(facet)。
  readonly openFilter = output<void>();
  // 點圖 / 鍵盤選取 → 請上層開右抽屜(inspector)。同圖重點也會 emit,故能重開。
  readonly opened = output<void>();
```

把既有 `onActivate` 改成(結尾加 emit):

```ts
  // masonry roving 導航:click / Enter / Space 觸發 → 選取該圖,並請上層開右抽屜(手機)。
  onActivate(e: { item: unknown; index: number }): void {
    this.pick(e.item as PhotoListItem);
    this.opened.emit();
  }
```

- [ ] **Step 3: PhotoGrid template 加「篩選」鈕**

Modify `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html` —— 在 `<div class="topbar">` 之後、`<div class="search">` 之前插入:

```html
  <!-- ③g:手機抽屜模式才顯示;開左抽屜(facet 篩選)。桌面用 edge 箭頭故不顯示。 -->
  @if (mobile()) {
    <button type="button" class="filter-btn" (click)="openFilter.emit()" aria-label="開啟篩選" title="篩選">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <path d="M3 5h18M6 12h12M10 19h4" />
      </svg>篩選
    </button>
  }
```

- [ ] **Step 4: PhotoGrid 加 .filter-btn 樣式**

Modify `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.css` —— 檔案末尾加:

```css
/* ③g 手機抽屜模式:topbar「篩選」鈕(開 facet 左抽屜)。顯示與否由 mobile() 控制,故此處不設 @media。 */
.filter-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 44px;
  padding: 0 12px;
  font-size: 13px;
  border: 1px solid var(--color-hair);
  border-radius: var(--radius-soft);
  background: var(--color-raised);
  color: var(--color-text);
  cursor: pointer;
}
.filter-btn:hover { border-color: var(--color-accent); }
.filter-btn svg { width: 18px; height: 18px; }
```

- [ ] **Step 5: BrowseGrid 加 input/output 與 emit**

Modify `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.ts`:

匯入加 `input, output`(第 1 行):

```ts
import { Component, inject, ElementRef, ViewChild, AfterViewInit, OnDestroy, computed, input, output } from '@angular/core';
```

在 class 內(`hitText` 附近)加:

```ts
  // ③g:手機抽屜模式;true → 顯示「資料夾」鈕。
  readonly mobile = input(false);
  // 點「資料夾」鈕 → 請上層開左抽屜(folder tree)。
  readonly openFilter = output<void>();
  // 點圖 / 鍵盤選取 → 請上層開右抽屜(inspector)。
  readonly opened = output<void>();
```

把既有 `onActivate` 改成:

```ts
  // masonry roving 導航:click / Enter / Space 觸發 → 選取該圖,並請上層開右抽屜(手機)。
  onActivate(e: { item: unknown; index: number }): void { this.pick(e.item as PhotoListItem); this.opened.emit(); }
```

- [ ] **Step 6: BrowseGrid template 加「資料夾」鈕**

Modify `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.html` —— 在 `<div class="topbar">` 之後、`<div class="crumbs">` 之前插入:

```html
    <!-- ③g:手機抽屜模式才顯示;開左抽屜(資料夾樹)。 -->
    @if (mobile()) {
      <button type="button" class="filter-btn" (click)="openFilter.emit()" aria-label="開啟資料夾樹" title="資料夾">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
          <path d="M4 5a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z" />
        </svg>資料夾
      </button>
    }
```

- [ ] **Step 7: BrowseGrid 加 .filter-btn 樣式**

Modify `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.css` —— 檔案末尾加(與 photo-grid 同一份 primitive;此處重複是因元件隔離編譯選不到對方 scope):

```css
/* ③g 手機抽屜模式:topbar「資料夾」鈕(開 folder tree 左抽屜)。 */
.filter-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 44px;
  padding: 0 12px;
  font-size: 13px;
  border: 1px solid var(--color-hair);
  border-radius: var(--radius-soft);
  background: var(--color-raised);
  color: var(--color-text);
  cursor: pointer;
}
.filter-btn:hover { border-color: var(--color-accent); }
.filter-btn svg { width: 18px; height: 18px; }
```

- [ ] **Step 8: build + test 確認無回歸**

Run: `cd src/Pm.Web && npx ng build && npx ng test --watch=false`
Expected: build 成功;測試全綠。桌面行為不變(`mobile` 預設 false,兩鈕不顯示;`opened` 雖在桌面也 emit,但目前無人訂閱故無副作用)。

- [ ] **Step 9: Commit**

```bash
git add src/Pm.Web/src/app/core/layout-breakpoints.ts \
  src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts \
  src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html \
  src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.css \
  src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.ts \
  src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.html \
  src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.css
git commit -m "feat(web): MOBILE 斷點 + grid topbar 篩選/資料夾鈕與抽屜輸出(③g)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_011eRUKVb3SSbr1Dtknj25WY"
```

---

## Task 3: gallery-view 抽屜整合

**Files:**
- Modify: `src/Pm.Web/src/app/features/gallery/gallery-view/gallery-view.ts`

**Interfaces:**
- Consumes: Task 1 `DrawerPanel`(`app-drawer-panel`,inputs open/side/title、output close);Task 2 `MOBILE` 常數、photo-grid 的 `mobile` input + `openFilter`/`opened` outputs。
- Produces:
  - `mobile = computed<boolean>()`(stageWidth > 0 && < MOBILE)。
  - `facetDrawerOpen = signal(false)`、`inspectorDrawerOpen = signal(false)`。
  - handlers:`onOpenFilter()`、`onImageOpened()`。
  - 桌面切換時自動關抽屜的 effect(防 resize 卡死)。

- [ ] **Step 1: 改 gallery-view.ts(imports / 注入 / signals / handlers)**

Modify `src/Pm.Web/src/app/features/gallery/gallery-view/gallery-view.ts`:

(a) 第 1 行 import 加 `effect`:

```ts
import { Component, OnInit, inject, DestroyRef, ElementRef, computed, signal, effect } from '@angular/core';
```

(b) 元件層 import 加 DrawerPanel 與 MOBILE:

```ts
import { Inspector } from '@features/inspector/inspector/inspector';
import { DrawerPanel } from '@core/ui/drawer-panel';
import { LightboxService } from '@core/ui/lightbox';
import { useStageWidth } from '../../../core/use-stage-width';
import { shouldAutoCollapse, INSPECTOR_COLLAPSE, FACET_COLLAPSE, MOBILE } from '../../../core/layout-breakpoints';
```

(c) `imports` 陣列加 `DrawerPanel`:

```ts
  imports: [FacetSidebar, PhotoGrid, Inspector, DrawerPanel],
```

(d) class 內,`inspectorCollapsed` computed 之後加:

```ts
  // ③g 手機抽屜模式:stage 寬 < MOBILE。桌面(false)行為一切照舊。
  readonly mobile = computed(() => {
    const w = this.stageWidth();
    return w > 0 && w < MOBILE;
  });
  readonly facetDrawerOpen = signal(false);
  readonly inspectorDrawerOpen = signal(false);

  // 篩選鈕 → 開左抽屜。
  onOpenFilter(): void { this.facetDrawerOpen.set(true); }
  // 點圖(含同圖重點、鍵盤選取)→ 手機才開右抽屜。
  onImageOpened(): void { if (this.mobile()) this.inspectorDrawerOpen.set(true); }
```

(e) constructor 加(class 目前無 constructor,新增一個於 `openLightbox()` 之前):

```ts
  constructor() {
    // 由手機切回桌面(resize / 旋轉)時關掉抽屜,避免殘留覆蓋層卡住桌面三欄。
    effect(() => {
      if (!this.mobile()) {
        this.facetDrawerOpen.set(false);
        this.inspectorDrawerOpen.set(false);
      }
    });
  }
```

- [ ] **Step 2: 改 gallery-view template(條件式三欄 + 抽屜)**

把 `template:` 整段 backtick 內容換成:

```ts
  template: `
    <div class="gview" [style.grid-template-columns]="gridCols()">
      @if (!mobile()) {
        <app-facet-sidebar [sidebarCollapsed]="facetCollapsed()" />
      }
      <div class="center-stage">
        @if (!mobile()) {
          <button
            class="edge-toggle et-left"
            (click)="toggleFacet()"
            [attr.aria-label]="facetCollapsed() ? '展開篩選側欄' : '收合篩選側欄'"
            [title]="facetCollapsed() ? '展開篩選' : '收合篩選'">
            <span aria-hidden="true">{{ facetCollapsed() ? '›' : '‹' }}</span>
          </button>
        }
        <app-photo-grid [mobile]="mobile()" (openFilter)="onOpenFilter()" (opened)="onImageOpened()" />
        @if (!mobile()) {
          <button
            class="edge-toggle et-right"
            (click)="toggleInspector()"
            [attr.aria-label]="inspectorCollapsed() ? '展開檢視器' : '收合檢視器'"
            [title]="inspectorCollapsed() ? '展開檢視器' : '收合檢視器'">
            <span aria-hidden="true">{{ inspectorCollapsed() ? '‹' : '›' }}</span>
          </button>
        }
      </div>
      @if (!mobile()) {
        <app-inspector [class.collapsed]="inspectorCollapsed()" [photoId]="store.selectedId()" (expand)="openLightbox()" />
      }

      @if (mobile()) {
        <app-drawer-panel side="left" [open]="facetDrawerOpen()" title="篩選" (close)="facetDrawerOpen.set(false)">
          <app-facet-sidebar />
        </app-drawer-panel>
        <app-drawer-panel side="right" [open]="inspectorDrawerOpen()" title="圖片詳情" (close)="inspectorDrawerOpen.set(false)">
          <app-inspector [photoId]="store.selectedId()" (expand)="openLightbox()" />
        </app-drawer-panel>
      }
    </div>
  `,
```

- [ ] **Step 3: 改 gridCols + 加 ::ng-deep 尺寸收束**

(a) `gridCols` computed 改成手機回單欄:

```ts
  readonly gridCols = computed(() => {
    if (this.mobile()) return '1fr';
    const f = this.facetCollapsed() ? '0' : '252px';
    const i = this.inspectorCollapsed() ? '0' : '350px';
    return `${f} 1fr ${i}`;
  });
```

(b) `styles:` 陣列那段 CSS 末尾(`.et-right { ... }` 之後、結尾 backtick 之前)加:

```css
      /* ③g:把原側板元件投影進抽屜時,收束其內建固定尺寸,改填滿抽屜 body 並讓其自身 overflow 捲動。
         ::ng-deep 必要 —— 隔離編譯下父層選不到子元件內部 .sidebar/:host;限定在本 view scope 下。 */
      :host ::ng-deep app-drawer-panel app-facet-sidebar .sidebar {
        width: 100%;
        height: 100%;
      }
      :host ::ng-deep app-drawer-panel app-inspector {
        width: 100%;
        height: 100%;
        border-left: none;
      }
```

- [ ] **Step 4: build 確認**

Run: `cd src/Pm.Web && npx ng build`
Expected: build 成功(`@core/*` 紅字若出現為 TS server 誤報,以 build 為準)。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/features/gallery/gallery-view/gallery-view.ts
git commit -m "feat(web): gallery-view 手機抽屜整合(facet 左/inspector 右)(③g)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_011eRUKVb3SSbr1Dtknj25WY"
```

---

## Task 4: browse-view 抽屜整合

**Files:**
- Modify: `src/Pm.Web/src/app/features/browse/browse-view/browse-view.ts`

**Interfaces:**
- Consumes: Task 1 `DrawerPanel`、Task 2 `MOBILE` + browse-grid 的 `mobile`/`openFilter`/`opened`。
- Produces: 與 gallery-view 對稱的 `mobile`/`facetDrawerOpen`(此處裝資料夾樹)/`inspectorDrawerOpen`/handlers/effect。左抽屜標題「資料夾」,裝 `<app-folder-tree-sidebar />`。

- [ ] **Step 1: 改 browse-view.ts(imports / signals / handlers)**

Modify `src/Pm.Web/src/app/features/browse/browse-view/browse-view.ts`:

(a) 第 1 行 import 加 `effect`:

```ts
import { Component, OnInit, inject, DestroyRef, ElementRef, computed, signal, effect } from '@angular/core';
```

(b) 元件層 import 加 DrawerPanel 與 MOBILE:

```ts
import { Inspector } from '@features/inspector/inspector/inspector';
import { DrawerPanel } from '@core/ui/drawer-panel';
import { LightboxService } from '@core/ui/lightbox';
import { useStageWidth } from '../../../core/use-stage-width';
import { shouldAutoCollapse, FACET_COLLAPSE, INSPECTOR_COLLAPSE, MOBILE } from '../../../core/layout-breakpoints';
```

(c) `imports` 陣列加 `DrawerPanel`:

```ts
  imports: [FolderTreeSidebar, BrowseGrid, Inspector, DrawerPanel],
```

(d) class 內,`gridCols` computed 之後加:

```ts
  // ③g 手機抽屜模式:stage 寬 < MOBILE。
  readonly mobile = computed(() => {
    const w = this.stageWidth();
    return w > 0 && w < MOBILE;
  });
  readonly facetDrawerOpen = signal(false);
  readonly inspectorDrawerOpen = signal(false);

  onOpenFilter(): void { this.facetDrawerOpen.set(true); }
  onImageOpened(): void { if (this.mobile()) this.inspectorDrawerOpen.set(true); }

  constructor() {
    effect(() => {
      if (!this.mobile()) {
        this.facetDrawerOpen.set(false);
        this.inspectorDrawerOpen.set(false);
      }
    });
  }
```

(e) `gridCols` computed 改成手機回單欄:

```ts
  readonly gridCols = computed(() => {
    if (this.mobile()) return '1fr';
    const t = this.treeCollapsed() ? '0' : '252px';
    const i = this.inspectorCollapsed() ? '0' : '350px';
    return `${t} 1fr ${i}`;
  });
```

- [ ] **Step 2: 改 browse-view template(條件式三欄 + 抽屜)**

把 `template:` 整段 backtick 內容換成:

```ts
  template: `
    <div class="bview" [style.grid-template-columns]="gridCols()">
      @if (!mobile()) {
        <app-folder-tree-sidebar [collapsed]="treeCollapsed()" />
      }
      <div class="center-stage">
        @if (!mobile()) {
          <button
            class="edge-toggle et-left"
            (click)="toggleTree()"
            [attr.aria-label]="treeCollapsed() ? '展開資料夾樹' : '收合資料夾樹'"
            [title]="treeCollapsed() ? '展開資料夾樹' : '收合資料夾樹'">
            <span aria-hidden="true">{{ treeCollapsed() ? '›' : '‹' }}</span>
          </button>
        }
        <app-browse-grid [mobile]="mobile()" (openFilter)="onOpenFilter()" (opened)="onImageOpened()" />
        @if (!mobile()) {
          <button
            class="edge-toggle et-right"
            (click)="toggleInspector()"
            [attr.aria-label]="inspectorCollapsed() ? '展開檢視器' : '收合檢視器'"
            [title]="inspectorCollapsed() ? '展開檢視器' : '收合檢視器'">
            <span aria-hidden="true">{{ inspectorCollapsed() ? '‹' : '›' }}</span>
          </button>
        }
      </div>
      @if (!mobile()) {
        <app-inspector [class.collapsed]="inspectorCollapsed()" [photoId]="store.selectedId()" (expand)="openLightbox()" />
      }

      @if (mobile()) {
        <app-drawer-panel side="left" [open]="facetDrawerOpen()" title="資料夾" (close)="facetDrawerOpen.set(false)">
          <app-folder-tree-sidebar />
        </app-drawer-panel>
        <app-drawer-panel side="right" [open]="inspectorDrawerOpen()" title="圖片詳情" (close)="inspectorDrawerOpen.set(false)">
          <app-inspector [photoId]="store.selectedId()" (expand)="openLightbox()" />
        </app-drawer-panel>
      }
    </div>
  `,
```

- [ ] **Step 3: 加 ::ng-deep 尺寸收束**

`styles:` 陣列那段 CSS 末尾(`.et-right { ... }` 之後、結尾 backtick 之前)加:

```css
      /* ③g:投影進抽屜的子元件改填滿並自捲(理由同 gallery-view)。 */
      :host ::ng-deep app-drawer-panel app-folder-tree-sidebar .sidebar {
        width: 100%;
        height: 100%;
      }
      :host ::ng-deep app-drawer-panel app-inspector {
        width: 100%;
        height: 100%;
        border-left: none;
      }
```

- [ ] **Step 4: build 確認**

Run: `cd src/Pm.Web && npx ng build`
Expected: build 成功。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/features/browse/browse-view/browse-view.ts
git commit -m "feat(web): browse-view 手機抽屜整合(資料夾樹 左/inspector 右)(③g)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_011eRUKVb3SSbr1Dtknj25WY"
```

---

## Task 5: e2e 手機抽屜煙霧測試

**Files:**
- Create: `src/Pm.Web/e2e/mobile-drawers-smoke.mjs`
- Modify: `src/Pm.Web/package.json`

**Interfaces:**
- Consumes: 跑起來的 app(`dotnet run` 在 :5180,serve 已 build 的前端靜態檔)。Task 1–4 的 DOM 約定:`.filter-btn`、`.dp-scrim`/`.dp-panel`/`.dp-close`、`.m-item.roving`、`.zoom-btn`。
- Produces: `npm run e2e:mobile`。
- **覆蓋範圍說明:** e2e 主測 `/browse`(資料夾樹 + inspector 抽屜),mock 端點沿用 `lightbox-smoke.mjs` 已驗證的一組。`/gallery` 用同一支 `DrawerPanel` 與對稱 wiring,結構相同,由 ng build + DrawerPanel 單元測試 + 手測覆蓋(facet 樹端點與 browse 不同,不在此 mock)。此限制在腳本註解中明記,不靜默。

- [ ] **Step 1: 寫 e2e 腳本**

Create `src/Pm.Web/e2e/mobile-drawers-smoke.mjs`:

```js
// ③g 手機抽屜 e2e(viewport < 768):
//  1. topbar「資料夾」鈕開左抽屜、header X 關。
//  2. 點圖自動開右抽屜(inspector)、詳情顯示。
//  3. 右抽屜內 ⤢ 放大鈕可點、且座標不與 header 關閉 X 重疊(根治原疊鈕 bug)。
//  4. 圖牆滿寬(無 102px 擠爛)。
//  5. 桌面寬(≥768)不出現抽屜、維持三欄(回歸保護)。
// 覆蓋範圍:主測 /browse(資料夾樹+inspector 抽屜)。/gallery 用同一支 DrawerPanel 與對稱 wiring,
//   結構相同,由 ng build + DrawerPanel 單元測試 + 手測覆蓋(facet 樹端點與 browse 不同,不在此 mock)。
// 跑法:先 `dotnet run` 起 app,再 `node e2e/mobile-drawers-smoke.mjs`。
import { chromium } from 'playwright';
import { mkdirSync } from 'fs';

const BASE = process.env.BASE ?? 'http://localhost:5180';
const OUT = process.env.OUT ?? 'e2e/shots';
mkdirSync(OUT, { recursive: true });

const TREE = { name: '圖庫', relPath: '', photoCount: 320, children: [
  { name: 'Pixiv', relPath: 'Pixiv', photoCount: 210, children: [{ name: '2024', relPath: 'Pixiv/2024', photoCount: 120, children: null }] },
  { name: 'Twitter', relPath: 'Twitter', photoCount: 80, children: null }] };
const ROOTS = [{ id: 1, name: '圖庫', photoCount: 320 }];

function searchPage(afterId) {
  const top = afterId == null ? 230 : afterId - 1;
  const ids = [];
  for (let i = top; i > top - 40 && i > 100; i--) ids.push(i);
  const last = ids.length ? ids[ids.length - 1] : 100;
  return { items: ids.map((id) => ({ id, fileHash: String(id), width: 1200, height: 800 })), nextCursor: last > 101 ? last - 1 : null };
}
function svgImg(id, label) {
  const hue = (id * 47) % 360;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="800"><rect width="1200" height="800" fill="hsl(${hue} 55% 40%)"/><text x="600" y="430" font-size="120" fill="white" text-anchor="middle" font-family="monospace">${label} ${id}</text></svg>`;
}
function detail(id) {
  return { id, fileHash: String(id).padStart(8, '0'), width: 1200, height: 800, mime: 'image/svg+xml',
    takenAt: null, cameraModel: null,
    locations: [{ libraryRootId: 1, relPath: `Pixiv/pic_${id}.png`, status: 'present' }], tags: [] };
}

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 480, height: 900 } });
await page.route('**/api/**', async (route) => {
  const p = new URL(route.request().url()).pathname;
  const json = (b) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(b) });
  if (p === '/api/folder-roots') return json(ROOTS);
  if (/^\/api\/roots\/\d+\/folder-tree$/.test(p)) return json(TREE);
  if (p === '/api/browse/folder-tags') return json([]);
  if (p === '/api/search/count') return json({ total: 130 });
  if (p === '/api/search') return json(searchPage(route.request().postDataJSON()?.afterId ?? null));
  if (/^\/api\/photos\/\d+\/thumb$/.test(p)) return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: svgImg(Number(p.match(/photos\/(\d+)/)[1]), '縮圖') });
  if (/^\/api\/photos\/\d+\/file$/.test(p)) return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: svgImg(Number(p.match(/photos\/(\d+)/)[1]), '原圖') });
  if (/^\/api\/photos\/\d+$/.test(p)) return json(detail(Number(p.match(/photos\/(\d+)/)[1])));
  if (p === '/api/tagging/stats') return json({ pending: 0, error: 0, running: 0 });
  return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
});

const fail = (m) => { console.error('ASSERT FAIL:', m); process.exitCode = 1; };

try {
  // ---- 手機寬(480):抽屜模式 ----
  await page.goto(`${BASE}/browse?root=1&path=Pixiv`, { waitUntil: 'networkidle' });
  await page.waitForSelector('.m-item.roving', { timeout: 15000 });

  // 圖牆滿寬:中央 grid 欄寬應接近視窗寬(無被 350px 側欄擠成 ~102px)。
  const stageW = await page.$eval('.center-stage', (e) => e.getBoundingClientRect().width);
  if (stageW < 360) fail(`圖牆被擠爛(center-stage 寬 ${stageW},期望 ≥ 360)`);
  else console.log(`OK:手機圖牆滿寬 center-stage=${Math.round(stageW)}`);

  // 「資料夾」鈕存在 → 開左抽屜。
  await page.waitForSelector('.filter-btn', { timeout: 5000 });
  await page.click('.filter-btn');
  await page.waitForSelector('.dp-panel.left[role="dialog"]', { timeout: 5000 });
  console.log('OK:資料夾鈕開左抽屜');
  await page.screenshot({ path: `${OUT}/mobile-left-drawer.png` });

  // header X 關左抽屜。
  await page.click('.dp-panel.left .dp-close');
  await page.waitForTimeout(250);
  if (await page.$('.dp-panel.left')) fail('header X 未關左抽屜');
  else console.log('OK:header X 關左抽屜');

  // 點圖 → 自動開右抽屜(inspector)。
  await page.click('.m-item.roving');
  await page.waitForSelector('.dp-panel.right[role="dialog"]', { timeout: 5000 });
  console.log('OK:點圖自動開右抽屜');

  // 右抽屜內 ⤢ 放大鈕存在,且與 header 關閉 X 座標不重疊(根治疊鈕 bug)。
  await page.waitForSelector('.dp-panel.right .zoom-btn', { timeout: 5000 });
  const zoom = await page.$eval('.dp-panel.right .zoom-btn', (e) => e.getBoundingClientRect());
  const x = await page.$eval('.dp-panel.right .dp-close', (e) => e.getBoundingClientRect());
  const overlap = !(zoom.right < x.left || zoom.left > x.right || zoom.bottom < x.top || zoom.top > x.bottom);
  if (overlap) fail(`⤢ 放大鈕與 header X 重疊(zoom ${JSON.stringify(zoom)} / x ${JSON.stringify(x)})`);
  else console.log('OK:⤢ 放大鈕與 header X 不重疊');
  await page.screenshot({ path: `${OUT}/mobile-right-drawer.png` });

  // ⤢ 可點 → 開 lightbox。
  await page.click('.dp-panel.right .zoom-btn');
  await page.waitForSelector('.lb[role="dialog"]', { timeout: 5000 });
  console.log('OK:⤢ 可點,開 lightbox');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(150);

  // ---- 桌面寬(1440):無抽屜、維持三欄(回歸保護)----
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.waitForTimeout(300);
  if (await page.$('.dp-scrim')) fail('桌面寬仍殘留抽屜');
  else console.log('OK:桌面寬無抽屜');
  if (!(await page.$('.filter-btn'))) console.log('OK:桌面寬「資料夾」鈕隱藏');
  else fail('桌面寬仍顯示「資料夾」鈕');
} catch (e) {
  fail(e.message);
} finally {
  await browser.close();
}
console.log(process.exitCode ? 'MOBILE-DRAWERS E2E: 有失敗' : 'MOBILE-DRAWERS E2E: 全部通過');
```

- [ ] **Step 2: 加 package.json script**

Modify `src/Pm.Web/package.json` —— `scripts` 內 `e2e:lightbox` 之後加一行:

```json
    "e2e:lightbox": "node e2e/lightbox-smoke.mjs",
    "e2e:mobile": "node e2e/mobile-drawers-smoke.mjs"
```

- [ ] **Step 3: build 前端、起 app、跑 e2e**

```bash
cd src/Pm.Web && npx ng build
cd ../.. && dotnet run --project src/Pm.Api &   # 背景起 app(:5180);或另開終端機
# 等 app ready 後:
cd src/Pm.Web && npm run e2e:mobile
```

Expected: 輸出 `MOBILE-DRAWERS E2E: 全部通過`,各 OK 行齊全;`e2e/shots/` 有 `mobile-left-drawer.png`、`mobile-right-drawer.png`。

- [ ] **Step 4: Commit**

```bash
git add src/Pm.Web/e2e/mobile-drawers-smoke.mjs src/Pm.Web/package.json
git commit -m "test(web): 加手機抽屜 e2e 煙霧測試(③g)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_011eRUKVb3SSbr1Dtknj25WY"
```

---

## Task 6: 全量驗證 + 手測 + 既有 e2e 回歸 + 開 PR

**Files:** 無(驗證與交付)。

- [ ] **Step 1: 全量 build + 前端測試**

Run: `cd src/Pm.Web && npx ng build && npx ng test --watch=false`
Expected: build 成功;全部前端測試綠(含新 DrawerPanel 7 測)。

- [ ] **Step 2: 後端測試(確認未受影響)**

Run: `dotnet test`
Expected: 全綠(本次未動後端,作 sanity)。

- [ ] **Step 3: 既有前端 e2e 回歸**

Run(app 已起於 :5180):
```bash
cd src/Pm.Web && npm run e2e && npm run e2e:rwd && npm run e2e:a11y && npm run e2e:lightbox
```
Expected: 四支皆「全部通過」(桌面行為不變,`mobile` 預設 false)。

- [ ] **Step 4: 手測(窄/寬切換)**

起 app(可帶真實 DB:`Storage__BaseDir="D:\project\sus-picture-management\src\Pm.Api"` + Production),瀏覽器開 `/gallery` 與 `/browse`,DevTools 縮到 < 768:
- 「篩選/資料夾」鈕出現、開左抽屜;header X / Esc / 點 scrim 皆可關。
- 點圖右抽屜自動滑出;詳情可捲到底(無被 header 切掉);⤢ 開 lightbox(在抽屜之上)。
- 抽屜內焦點受困(Tab 不跑出);關閉後焦點回到觸發處。
- 拉回 ≥ 768:抽屜消失、恢復三欄 + edge 箭頭,無殘留覆蓋層。

- [ ] **Step 5: 更新 spec 狀態 + push + 開 PR**

(a) 把 `docs/superpowers/specs/2026-06-28-mobile-drawers-design.md` 開頭狀態由「設計定案,待實作」改為「已實作(2026-06-28),見 plans/2026-06-28-mobile-drawers.md」。

(b) commit + push:
```bash
git add docs/superpowers/specs/2026-06-28-mobile-drawers-design.md docs/superpowers/plans/2026-06-28-mobile-drawers.md
git commit -m "docs: ③g 手機抽屜 spec 標已實作 + 加實作計畫

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_011eRUKVb3SSbr1Dtknj25WY"
git push -u origin feat/mobile-drawers
gh pr create --base main --head feat/mobile-drawers --title "feat(web): ③g 完整手機版抽屜式側板" --body "..."
```
(PR body 結尾加 `🤖 Generated with [Claude Code](https://claude.com/claude-code)`。)由使用者在手機 GitHub app 合併。

---

## Self-Review

**1. Spec coverage(逐條對 `2026-06-28-mobile-drawers-design.md`):**
- rail 保留、僅補 44px:rail 本身不動(非目標明示不改 rail);觸控 44px 體現在 .filter-btn / .dp-close ≥ 44px。✓(rail 既有 .act 尺寸不在本次範圍,spec「僅補滿」屬 a11y 既有基礎)
- MOBILE = 768 寫進 layout-breakpoints:Task 2 Step 1。✓
- ≥768 桌面完全不變:Task 3/4 以 `@if(!mobile())` 保留原三欄 + edge 箭頭;gridCols 桌面分支原樣。✓
- < 768 圖牆滿寬 + 兩側板改抽屜:gridCols 回 `1fr`;facet/tree/inspector 進 DrawerPanel。✓ e2e 驗 center-stage 寬。
- 觸發:篩選鈕開左、點圖自動開右:Task 2 鈕 + Task 3/4 `onOpenFilter`/`onImageOpened`。✓
- 關閉鈕固定 header、永不蓋內容:DrawerPanel `.dp-head` flow 佈局;e2e 驗 ⤢ 與 X 不重疊。✓
- DrawerPanel inputs/outputs/role=dialog/aria-modal/focus trap/Esc/scrim/動畫/reduced-motion/z-index<lightbox/左右寬:Task 1 全覆蓋。✓
- 自動開右抽屜 + 同圖重開:用 grid `opened` output(每次 activate emit,含同圖)而非 effect 監看 selectedId —— 比 spec 原案更直接且解決「同圖重點不重開」。已記錄理由(Task 2 Interfaces)。✓
- 焦點還原:cdkTrapFocusAutoCapture 銷毀時還原。✓
- 測試:drawer-panel.spec(Task 1)+ mobile-drawers-smoke.mjs(Task 5)+ 既有 e2e 維持綠(Task 6)。✓
- 受影響檔案清單:與 spec §受影響檔案一致(drawer-panel、layout-breakpoints、兩 view、兩 grid、package.json、e2e)。✓

**2. Placeholder scan:** 無 TBD/TODO;每個 code step 皆含完整程式碼;PR body 的 `"..."` 為交付當下填寫(非程式碼佔位)。✓

**3. Type consistency:** `mobile`/`openFilter`/`opened`(grid)↔ view 綁定一致;`DrawerPanel` open/side/title/close 在 Task 1 定義、Task 3/4 一致使用;`MOBILE` 命名一致;`onActivate` 簽名沿用既有 `{ item: unknown; index: number }`。✓
```
