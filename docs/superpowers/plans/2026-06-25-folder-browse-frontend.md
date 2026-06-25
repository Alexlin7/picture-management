# 資料夾瀏覽維度 — 前端實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增 `/browse` 頁面 —— 與搜尋並列的第二入口,照資料夾分類瀏覽:真實資料夾樹側欄、麵包屑、主區子資料夾可點下鑽、遞迴圖牆、夾內疊 tag 自動完成。

**Architecture:** 新 `features/browse/`,平行於 `features/gallery/`。`BrowseStore`(signals + URL query 單一真相 `?root=&path=&q=`)接後端三端點(`/api/folder-roots`、`/api/roots/{id}/folder-tree`、`/api/browse/folder-tags`)+ 既有 `/api/search`(已加 `rootId`/`pathPrefix`)。UI 沿用既有 `<app-thumb>`、IntersectionObserver 無限捲、樣式 primitive,視覺對齊 mockup。

**Tech Stack:** Angular 22(standalone components + signals)、Tailwind v4、Vitest、RxJS(`firstValueFrom`)。

## Global Constraints

- Angular 22 standalone components + signals;store 用 `@Injectable({ providedIn: 'root' })`,私有 signal + 公開 `.asReadonly()`;新輸入用 `input<T>()`。
- 資料一律經 `PmApi`(`@core/api/pm-api`);元件只讀 store 的 signal,不直接打 http。
- **URL query 是狀態單一真相**:`/browse?root=<id>&path=<relPath>&q=<夾內tag>`;`browse-view` 訂閱 `ActivatedRoute.queryParams` 套用(仿 `gallery-view`),狀態變動一律經 `router.navigate` 推進 URL,不在元件本地另存一份。
- **樣式落點(CLAUDE.md `2026-06-24-ui-style-system-design.md`)**:能用 Tailwind utility 表達就寫 template `class`;跨元件共用樣式進 `src/Pm.Web/src/styles.css` 的 `@layer components`;元件專屬複雜樣式進元件 `.css`,**一律 `var(--token)`**。**鐵則:元件 `.css` 不得 `@apply`/`@tailwind`/`@reference`**(Angular 隔離編譯選不到全域 token);共用 `@apply` 只能寫在全域 `styles.css`。顏色/圓角/字體一律走 token,不寫裸 hex。
- **masonry 容器 class 不可命名 `.grid`**(會撞 Tailwind `.grid` utility = `display:grid`,蓋掉 `column-count`);沿用 `.masonry`(見 photo-grid.css 註解)。
- 圖牆用 `<app-thumb [photoId]="..." [aspectRatio]="..." />`(`@core/ui/thumb`,依 hash 縮圖快取,**絕不拉原圖**);無限捲用 `IntersectionObserver` 哨兵 + `rootMargin: '600px'`(仿 `photo-grid.ts`)。
- **a11y**:互動元素勿用 `outline:none` 蓋掉全域 `:focus-visible` ring;全域已有 `prefers-reduced-motion` 降載,新動畫沿用 token `--dur-fast`/`--ease-out`。
- 視覺對齊既有深色工作台 token(`@theme`:`--color-canvas/panel/raised/hair/text/muted/faint/accent`、`--color-t-*`、`--font-display/body/mono`)與 mockup **`docs/mockups/folder-dimension-design.html`**(分頁 2「資料夾瀏覽完整互動」是視覺真相:側欄樹、麵包屑、子資料夾晶片列、夾內篩 tag 帶)。
- 測試:純函式用 **Vitest**(`npm test`,在 `src/Pm.Web/`);UI 接線靠 `npm run build`(`ng build`)綠 + plan 內手測檢查點(前端自動測試覆蓋有限,本專案慣例)。
- 全程繁體中文(台灣)註解;識別子原文。

---

### Task 3: API client 擴充 + browse 純函式 + BrowseStore

**Files:**
- Modify: `src/Pm.Web/src/app/core/api/pm-api.ts`(加型別 + `SearchReq` 欄位 + 三方法)
- Create: `src/Pm.Web/src/app/features/browse/browse-tree.ts`(純函式)
- Create: `src/Pm.Web/src/app/features/browse/browse-tree.spec.ts`(Vitest)
- Create: `src/Pm.Web/src/app/features/browse/browse.store.ts`

**Interfaces:**
- Produces(供 Task 4/5):
  - `pm-api.ts`:`interface FolderNode { name: string; relPath: string; photoCount: number; children?: FolderNode[] | null }`、`interface FolderRoot { id: number; name: string; photoCount: number }`、`interface FolderTag { name: string; kind: string; count: number }`;`SearchReq` 末加 `rootId?: number; pathPrefix?: string`;方法 `folderRoots(): Promise<FolderRoot[]>`、`folderTree(rootId): Promise<FolderNode>`、`folderTags(rootId, path): Promise<FolderTag[]>`。
  - `browse-tree.ts`:`breadcrumbFromPath(rootName: string, relPath: string): { name: string; relPath: string }[]`、`findNode(tree: FolderNode, relPath: string): FolderNode | null`、`subfoldersOf(tree: FolderNode, relPath: string): FolderNode[]`。
  - `browse.store.ts`:`BrowseStore` —— signals `folderRoots/currentRootId/tree/currentPath/breadcrumb/subfolders/photos/hitCount/innerTokens/loading/error/hasMore`;方法 `loadRoots()`、`applyUrl(root, path, q)`、`enterFolder(relPath)`、`selectRoot(id)`、`addInnerTag(name,kind)`、`removeInnerTag(i)`、`loadMore()`、`select(id)`。

- [ ] **Step 1: pm-api.ts 加型別與方法**

Modify `src/Pm.Web/src/app/core/api/pm-api.ts`。在 `SearchReq` interface 改為(末加兩欄):

```typescript
export interface SearchReq { all?: string[]; none?: string[]; afterId?: number | null; pageSize?: number; rootId?: number; pathPrefix?: string; }
```

在檔案的 interface 區(靠近 `TagListRow`)加:

```typescript
export interface FolderNode { name: string; relPath: string; photoCount: number; children?: FolderNode[] | null; }
export interface FolderRoot { id: number; name: string; photoCount: number; }
export interface FolderTag { name: string; kind: string; count: number; }
```

在 `PmApi` class 內(`tagTree()` 之後)加三方法:

```typescript
  // ---- 資料夾瀏覽維度 ----
  folderRoots(): Promise<FolderRoot[]> {
    return firstValueFrom(this.http.get<FolderRoot[]>('/api/folder-roots'));
  }
  folderTree(rootId: number): Promise<FolderNode> {
    return firstValueFrom(this.http.get<FolderNode>(`/api/roots/${rootId}/folder-tree`));
  }
  folderTags(rootId: number, path: string): Promise<FolderTag[]> {
    let params = new HttpParams().set('rootId', String(rootId));
    if (path) params = params.set('path', path);
    return firstValueFrom(this.http.get<FolderTag[]>('/api/browse/folder-tags', { params }));
  }
```

(`HttpParams` 已在檔案頂部 import。)

- [ ] **Step 2: 寫 browse-tree 純函式失敗測試**

Create `src/Pm.Web/src/app/features/browse/browse-tree.spec.ts`:

```typescript
import { describe, it, expect } from 'vitest';
import { breadcrumbFromPath, findNode, subfoldersOf } from './browse-tree';
import type { FolderNode } from '@core/api/pm-api';

const tree: FolderNode = {
  name: '圖庫', relPath: '', photoCount: 6,
  children: [
    { name: 'Pixiv', relPath: 'Pixiv', photoCount: 4, children: [
      { name: '2023', relPath: 'Pixiv/2023', photoCount: 1, children: null },
      { name: '2024', relPath: 'Pixiv/2024', photoCount: 3, children: [
        { name: 'sub', relPath: 'Pixiv/2024/sub', photoCount: 1, children: null },
      ] },
    ] },
    { name: 'Twitter', relPath: 'Twitter', photoCount: 1, children: null },
  ],
};

describe('breadcrumbFromPath', () => {
  it('根層只有 root 名', () => {
    expect(breadcrumbFromPath('圖庫', '')).toEqual([{ name: '圖庫', relPath: '' }]);
  });
  it('累積每層 relPath 前綴', () => {
    expect(breadcrumbFromPath('圖庫', 'Pixiv/2024')).toEqual([
      { name: '圖庫', relPath: '' },
      { name: 'Pixiv', relPath: 'Pixiv' },
      { name: '2024', relPath: 'Pixiv/2024' },
    ]);
  });
});

describe('findNode', () => {
  it('空 relPath 回 root', () => {
    expect(findNode(tree, '')?.name).toBe('圖庫');
  });
  it('深層節點', () => {
    expect(findNode(tree, 'Pixiv/2024')?.photoCount).toBe(3);
    expect(findNode(tree, 'Pixiv/2024/sub')?.photoCount).toBe(1);
  });
  it('不存在回 null', () => {
    expect(findNode(tree, 'Nope/x')).toBeNull();
  });
});

describe('subfoldersOf', () => {
  it('回該節點的直接子資料夾', () => {
    expect(subfoldersOf(tree, 'Pixiv').map((c) => c.name)).toEqual(['2023', '2024']);
  });
  it('葉節點回空陣列', () => {
    expect(subfoldersOf(tree, 'Pixiv/2024/sub')).toEqual([]);
  });
  it('找不到節點回空陣列', () => {
    expect(subfoldersOf(tree, 'Nope')).toEqual([]);
  });
});
```

- [ ] **Step 3: 跑測試確認失敗**

Run: `cd src/Pm.Web && npm test -- browse-tree`
Expected: FAIL — `browse-tree.ts` 不存在(找不到模組)。

- [ ] **Step 4: 寫 browse-tree.ts**

Create `src/Pm.Web/src/app/features/browse/browse-tree.ts`:

```typescript
import type { FolderNode } from '@core/api/pm-api';

// 資料夾瀏覽用純函式(無副作用,易測)。

// relPath → 麵包屑:root 名起頭,逐層累積前綴。
// "Pixiv/2024" → [{圖庫,""},{Pixiv,"Pixiv"},{2024,"Pixiv/2024"}]
export function breadcrumbFromPath(rootName: string, relPath: string): { name: string; relPath: string }[] {
  const crumbs = [{ name: rootName, relPath: '' }];
  if (!relPath) return crumbs;
  const parts = relPath.split('/').filter(Boolean);
  let acc = '';
  for (const p of parts) {
    acc = acc ? `${acc}/${p}` : p;
    crumbs.push({ name: p, relPath: acc });
  }
  return crumbs;
}

// 在樹中依 relPath 找節點(relPath==="" → root);找不到 → null。
export function findNode(tree: FolderNode, relPath: string): FolderNode | null {
  if (!relPath) return tree;
  const parts = relPath.split('/').filter(Boolean);
  let node: FolderNode | null = tree;
  for (const p of parts) {
    node = node.children?.find((c) => c.name === p) ?? null;
    if (!node) return null;
  }
  return node;
}

// 某 relPath 節點的直接子資料夾(找不到或葉節點 → [])。
export function subfoldersOf(tree: FolderNode, relPath: string): FolderNode[] {
  return findNode(tree, relPath)?.children ?? [];
}
```

- [ ] **Step 5: 跑測試確認通過**

Run: `cd src/Pm.Web && npm test -- browse-tree`
Expected: PASS(8 個斷言)。

- [ ] **Step 6: 寫 BrowseStore**

Create `src/Pm.Web/src/app/features/browse/browse.store.ts`:

```typescript
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { PmApi, type PhotoListItem, type FolderNode, type FolderRoot } from '@core/api/pm-api';
import { type TagKind } from '@core/tag-color';
import { breadcrumbFromPath, findNode, subfoldersOf } from './browse-tree';

// 夾內疊 tag token(沿用 gallery 的 text/kind 形狀;text 無 '-' 前綴 = include)。
export interface InnerToken { text: string; kind: TagKind; }

const PAGE_SIZE = 60;

// 資料夾瀏覽 store:資料只經 PmApi;狀態(root/path/夾內tag)以 URL query 為單一真相,
// 變動一律經 router.navigate 推進,browse-view 訂閱 queryParams 回呼套用 → 無迴圈。
@Injectable({ providedIn: 'root' })
export class BrowseStore {
  private readonly api = inject(PmApi);
  private readonly router = inject(Router);

  // ---- 來源與樹 ----
  private readonly _roots = signal<FolderRoot[]>([]);
  readonly roots = this._roots.asReadonly();
  private readonly _rootId = signal<number | null>(null);
  readonly currentRootId = this._rootId.asReadonly();
  private readonly _tree = signal<FolderNode | null>(null);
  readonly tree = this._tree.asReadonly();
  private readonly _path = signal<string>('');
  readonly currentPath = this._path.asReadonly();

  // 麵包屑與子資料夾由 tree + path 衍生。
  readonly breadcrumb = computed(() => {
    const t = this._tree();
    return t ? breadcrumbFromPath(t.name, this._path()) : [];
  });
  readonly subfolders = computed(() => {
    const t = this._tree();
    return t ? subfoldersOf(t, this._path()) : [];
  });
  readonly currentCount = computed(() => {
    const t = this._tree();
    const n = t ? findNode(t, this._path()) : null;
    return n?.photoCount ?? 0;
  });

  // ---- 夾內疊 tag ----
  private readonly _innerTokens = signal<InnerToken[]>([]);
  readonly innerTokens = this._innerTokens.asReadonly();

  // ---- 圖牆(keyset 無限捲累積)----
  private readonly _photos = signal<PhotoListItem[]>([]);
  readonly photos = this._photos.asReadonly();
  private readonly _nextCursor = signal<number | null>(null);
  readonly hasMore = computed(() => this._nextCursor() !== null);
  private readonly _hitCount = signal(0);
  readonly hitCount = this._hitCount.asReadonly();
  private readonly _loading = signal(false);
  readonly loading = this._loading.asReadonly();
  private readonly _error = signal<string | null>(null);
  readonly error = this._error.asReadonly();

  // ---- 選取(供日後 inspector;先存著)----
  private readonly _selectedId = signal<number | null>(null);
  readonly selectedId = this._selectedId.asReadonly();
  select(id: number | null): void { this._selectedId.set(id); }

  // 載入所有 root 摘要(頂層並列);若尚無 currentRootId 預設選第一個有圖的(或第一個)。
  async loadRoots(): Promise<void> {
    try {
      const rs = await this.api.folderRoots();
      this._roots.set(rs);
    } catch (e) { this._error.set(this.msg(e)); }
  }

  // URL → 狀態套用(browse-view 的 queryParams 訂閱唯一呼叫處)。
  // root 變了才重載樹;path/q 變了重查圖。
  async applyUrl(rootId: number | null, path: string, q: string): Promise<void> {
    // 沒指定 root → 自動挑第一個(有圖優先)
    if (rootId === null) {
      if (!this._roots().length) await this.loadRoots();
      const pick = this._roots().find((r) => r.photoCount > 0) ?? this._roots()[0];
      if (pick) { this.pushUrl(pick.id, '', []); return; }
    }
    if (rootId !== null && rootId !== this._rootId()) {
      this._rootId.set(rootId);
      await this.loadTree(rootId);
    }
    this._path.set(path);
    this._innerTokens.set(decodeInner(q));
    await this.search();
  }

  private async loadTree(rootId: number): Promise<void> {
    try { this._tree.set(await this.api.folderTree(rootId)); }
    catch (e) { this._error.set(this.msg(e)); this._tree.set(null); }
  }

  // 重查圖(path / 夾內 tag 變動):重置游標與累積。
  private async search(): Promise<void> {
    const rootId = this._rootId();
    if (rootId === null) return;
    this._loading.set(true); this._error.set(null);
    const { all, none } = splitInner(this._innerTokens());
    const pathPrefix = this._path();
    try {
      const [count, page] = await Promise.all([
        this.api.searchCount({ all, none, rootId, pathPrefix }),
        this.api.search({ all, none, rootId, pathPrefix, afterId: null, pageSize: PAGE_SIZE }),
      ]);
      this._hitCount.set(count.total);
      this._photos.set(page.items);
      this._nextCursor.set(page.nextCursor ?? null);
    } catch (e) {
      this._error.set(this.msg(e)); this._photos.set([]); this._hitCount.set(0); this._nextCursor.set(null);
    } finally { this._loading.set(false); }
  }

  async loadMore(): Promise<void> {
    const cursor = this._nextCursor(); const rootId = this._rootId();
    if (cursor === null || rootId === null || this._loading()) return;
    this._loading.set(true);
    const { all, none } = splitInner(this._innerTokens());
    try {
      const page = await this.api.search({ all, none, rootId, pathPrefix: this._path(), afterId: cursor, pageSize: PAGE_SIZE });
      this._photos.update((cur) => [...cur, ...page.items]);
      this._nextCursor.set(page.nextCursor ?? null);
    } catch (e) { this._error.set(this.msg(e)); }
    finally { this._loading.set(false); }
  }

  // ---- 導航(全部經 URL)----
  selectRoot(id: number): void { this.pushUrl(id, '', []); }
  enterFolder(relPath: string): void { this.pushUrl(this._rootId(), relPath, this._innerTokens()); }
  addInnerTag(name: string, kind: TagKind): void {
    if (this._innerTokens().some((t) => t.text === name)) return;
    this.pushUrl(this._rootId(), this._path(), [...this._innerTokens(), { text: name, kind }]);
  }
  removeInnerTag(idx: number): void {
    this.pushUrl(this._rootId(), this._path(), this._innerTokens().filter((_, i) => i !== idx));
  }

  private pushUrl(rootId: number | null, path: string, inner: InnerToken[]): void {
    const queryParams: Record<string, string> = {};
    if (rootId !== null) queryParams['root'] = String(rootId);
    if (path) queryParams['path'] = path;
    const q = encodeInner(inner);
    if (q) queryParams['q'] = q;
    void this.router.navigate(['/browse'], { queryParams });
  }

  private msg(e: unknown): string { return e instanceof Error ? e.message : '載入失敗'; }
}

// 夾內 token 編解碼(沿用 gallery 的 ',' 串接慣例;kind 不進 URL,一律 general)。
function encodeInner(tokens: readonly InnerToken[]): string {
  return tokens.map((t) => t.text.trim()).filter(Boolean).join(',');
}
function decodeInner(q: string): InnerToken[] {
  if (!q) return [];
  return q.split(',').map((s) => s.trim()).filter(Boolean).map((text) => ({ text, kind: 'general' as TagKind }));
}
function splitInner(tokens: readonly InnerToken[]): { all: string[]; none: string[] } {
  const all: string[] = [], none: string[] = [];
  for (const t of tokens) {
    const raw = t.text.trim(); if (!raw) continue;
    if (raw.startsWith('-')) none.push(raw.slice(1)); else all.push(raw);
  }
  return { all, none };
}
```

- [ ] **Step 7: build 確認綠 + commit**

Run: `cd src/Pm.Web && npm run build`
Expected: build 成功(無型別錯誤)。Run `npm test -- browse-tree` 再確認 8 斷言綠。

```bash
git add src/Pm.Web/src/app/core/api/pm-api.ts src/Pm.Web/src/app/features/browse/browse-tree.ts src/Pm.Web/src/app/features/browse/browse-tree.spec.ts src/Pm.Web/src/app/features/browse/browse.store.ts
git commit -m "feat(web): browse 資料層 — PmApi 端點 + 純函式(樹/麵包屑)+ BrowseStore

folderRoots/folderTree/folderTags + SearchReq 加 rootId/pathPrefix;
breadcrumbFromPath/findNode/subfoldersOf(Vitest 8 斷言);BrowseStore
以 URL query(?root=&path=&q=)為單一真相,衍生麵包屑/子夾/計數。

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01GJJk3bb2x32Ru1AoxhVDZN"
```

---

### Task 4: route + activity 入口 + browse-view 三欄 + 樹側欄 + 麵包屑/子夾 + 遞迴圖牆

**Files:**
- Modify: `src/Pm.Web/src/app/app.routes.ts`(加 `/browse`)
- Modify: `src/Pm.Web/src/app/shell/shell.html`(activity bar 加入口)
- Create: `src/Pm.Web/src/app/features/browse/browse-view/browse-view.ts`
- Create: `src/Pm.Web/src/app/features/browse/folder-tree-sidebar/folder-tree-sidebar.ts` + `.html` + `.css`
- Create: `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.ts` + `.html` + `.css`

**Interfaces:**
- Consumes(Task 3):`BrowseStore`(全部 signal/方法)、`FolderNode`、`@core/ui/thumb` 的 `Thumb`(selector `app-thumb`,`[photoId]`/`[aspectRatio]`)、`@core/tag-color` 的 `tagColor`。
- Produces:route `/browse` → `BrowseView`;三欄殼 `app-browse-view` 組合 `app-folder-tree-sidebar` + `app-browse-grid`。

- [ ] **Step 1: 加 route**

Modify `src/Pm.Web/src/app/app.routes.ts`,在 `tags` 路由物件之後加:

```typescript
      {
        path: 'browse',
        loadComponent: () => import('./features/browse/browse-view/browse-view').then((m) => m.BrowseView),
      },
```

- [ ] **Step 2: activity bar 加入口**

Modify `src/Pm.Web/src/app/shell/shell.html`,在 `/tags`(標籤庫)`<a>` 之後加(folder 圖示,沿用既有 `.act` 樣式):

```html
    <a class="act" routerLink="/browse" routerLinkActive="active">
      <span class="lbl">資料夾瀏覽</span>
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
        <path d="M4 5a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z" />
      </svg>
    </a>
```

- [ ] **Step 3: browse-view 三欄殼(訂閱 URL)**

Create `src/Pm.Web/src/app/features/browse/browse-view/browse-view.ts`:

```typescript
import { Component, OnInit, inject, DestroyRef } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FolderTreeSidebar } from '../folder-tree-sidebar/folder-tree-sidebar';
import { BrowseGrid } from '../browse-grid/browse-grid';
import { BrowseStore } from '../browse.store';

// 資料夾瀏覽兩欄:資料夾樹側欄(252)· 圖牆(1fr)。inspector 暫不接(與 gallery 隔離)。
@Component({
  selector: 'app-browse-view',
  imports: [FolderTreeSidebar, BrowseGrid],
  template: `
    <div class="bview">
      <app-folder-tree-sidebar />
      <app-browse-grid />
    </div>
  `,
  styles: [`
    .bview { display: grid; grid-template-columns: 252px 1fr; height: 100vh; min-width: 0; }
  `],
})
export class BrowseView implements OnInit {
  readonly store = inject(BrowseStore);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    void this.store.loadRoots();
    // URL(root/path/q)是單一真相:初次 + 每次變動都套用。
    this.route.queryParams.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      const root = p['root'] != null ? Number(p['root']) : null;
      void this.store.applyUrl(root, (p['path'] as string) ?? '', (p['q'] as string) ?? '');
    });
  }
}
```

- [ ] **Step 4: 資料夾樹側欄**

Create `src/Pm.Web/src/app/features/browse/folder-tree-sidebar/folder-tree-sidebar.ts`:

```typescript
import { Component, inject } from '@angular/core';
import { BrowseStore } from '../browse.store';
import type { FolderNode, FolderRoot } from '@core/api/pm-api';

// 資料夾樹側欄:頂層並列各 root(多 root);選中 root 後展其樹(只渲染 1–2 層,深層靠主區子夾下鑽)。
@Component({
  selector: 'app-folder-tree-sidebar',
  imports: [],
  templateUrl: './folder-tree-sidebar.html',
  styleUrl: './folder-tree-sidebar.css',
})
export class FolderTreeSidebar {
  private readonly store = inject(BrowseStore);
  readonly roots = this.store.roots;
  readonly tree = this.store.tree;
  readonly currentRootId = this.store.currentRootId;
  readonly currentPath = this.store.currentPath;

  readonly fmt = (n: number): string => n.toLocaleString('en-US');

  selectRoot(r: FolderRoot): void { this.store.selectRoot(r.id); }
  enter(node: FolderNode): void { this.store.enterFolder(node.relPath); }
  // 第一層子資料夾(tree.children);只渲染到第二層,避免側欄爆長。
  firstLevel(): FolderNode[] { return this.tree()?.children ?? []; }
}
```

Create `src/Pm.Web/src/app/features/browse/folder-tree-sidebar/folder-tree-sidebar.html`(結構對齊 mockup `.sidebar` + facet-sidebar 的 `.frow`;icon 用 folder 色 `--color-t-meta`):

```html
<aside class="sidebar">
  <div class="side-h">資料夾 <span class="sub">{{ roots().length }} 來源</span></div>

  <!-- 多 root:各 root 一列(頂層並列);單 root 也照列。 -->
  @for (r of roots(); track r.id) {
    <div class="frow root-row" [class.on]="currentRootId() === r.id" (click)="selectRoot(r)">
      <svg class="ic" viewBox="0 0 24 24" fill="currentColor"><path d="M4 5a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z"/></svg>
      <span class="nm">{{ r.name }}</span>
      <span class="n">{{ fmt(r.photoCount) }}</span>
    </div>

    <!-- 選中的 root 才展第一層子資料夾(點 → 主區進該夾) -->
    @if (currentRootId() === r.id) {
      @for (c of firstLevel(); track c.relPath) {
        <div class="frow indent" [class.on]="currentPath() === c.relPath" (click)="enter(c)">
          <svg class="ic" viewBox="0 0 24 24" fill="currentColor"><path d="M4 5a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z"/></svg>
          <span class="nm">{{ c.name }}</span>
          <span class="n">{{ fmt(c.photoCount) }}</span>
        </div>
      }
    }
  }
</aside>
```

Create `src/Pm.Web/src/app/features/browse/folder-tree-sidebar/folder-tree-sidebar.css`(**只手寫 + `var(--token)`,不得 `@apply`**;對齊 facet-sidebar.css):

```css
:host { display: block; }
.sidebar { width: 252px; height: 100vh; background: var(--color-panel); border-right: 1px solid var(--color-hair); overflow-y: auto; padding: 16px 14px; }
.side-h { font-family: var(--font-display); font-weight: 600; font-size: 15px; margin: 2px 2px 14px; display: flex; align-items: center; gap: 8px; }
.side-h .sub { font-family: var(--font-mono); font-size: 10.5px; color: var(--color-faint); font-weight: 400; margin-left: auto; }
.frow { display: flex; align-items: center; gap: 8px; padding: 6px 8px; border-radius: 7px; cursor: pointer; color: var(--color-text); font-size: 13px; }
.frow:hover { background: var(--color-raised); }
.frow.on { background: rgba(34, 211, 238, 0.1); box-shadow: inset 0 0 0 1px rgba(34, 211, 238, 0.28); }
.frow .ic { width: 15px; height: 15px; flex: none; color: var(--color-t-meta); }
.frow .nm { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.frow .n { margin-left: auto; font-family: var(--font-mono); font-size: 11px; color: var(--color-faint); }
.root-row { font-weight: 600; }
.indent { margin-left: 18px; }
```

- [ ] **Step 5: 遞迴圖牆 + 麵包屑 + 子夾 bar(夾內 tag 帶留 Task 5)**

Create `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.ts`:

```typescript
import { Component, inject, signal, ElementRef, ViewChild, AfterViewInit, OnDestroy, computed } from '@angular/core';
import { BrowseStore } from '../browse.store';
import { Thumb } from '@core/ui/thumb';
import type { PhotoListItem, FolderNode } from '@core/api/pm-api';

// 資料夾瀏覽主區:麵包屑 + 子資料夾晶片(深層下鑽)+ 遞迴圖牆(無限捲)。夾內疊 tag 帶於 Task 5 接入。
@Component({
  selector: 'app-browse-grid',
  imports: [Thumb],
  templateUrl: './browse-grid.html',
  styleUrl: './browse-grid.css',
})
export class BrowseGrid implements AfterViewInit, OnDestroy {
  private readonly store = inject(BrowseStore);
  readonly breadcrumb = this.store.breadcrumb;
  readonly subfolders = this.store.subfolders;
  readonly photos = this.store.photos;
  readonly hitCount = this.store.hitCount;
  readonly currentCount = this.store.currentCount;
  readonly loading = this.store.loading;
  readonly error = this.store.error;
  readonly hasMore = this.store.hasMore;

  readonly hitText = computed(() => this.hitCount().toLocaleString('en-US'));
  readonly fmt = (n: number): string => n.toLocaleString('en-US');

  aspect(p: PhotoListItem): string { return p.width && p.height ? `${p.width}/${p.height}` : '1/1'; }
  enter(relPath: string): void { this.store.enterFolder(relPath); }
  enterChild(c: FolderNode): void { this.store.enterFolder(c.relPath); }
  pick(p: PhotoListItem): void { this.store.select(p.id); }

  @ViewChild('sentinel') private sentinel?: ElementRef<HTMLElement>;
  private io?: IntersectionObserver;
  ngAfterViewInit(): void {
    if (!this.sentinel) return;
    this.io = new IntersectionObserver((es) => {
      if (es[0]?.isIntersecting && this.hasMore() && !this.loading()) void this.store.loadMore();
    }, { rootMargin: '600px' });
    this.io.observe(this.sentinel.nativeElement);
  }
  ngOnDestroy(): void { this.io?.disconnect(); }
}
```

Create `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.html`(對齊 mockup 分頁 2:topbar 麵包屑 → subbar 子夾晶片 → masonry 圖牆 + 哨兵;**masonry class 不可叫 .grid**):

```html
<div class="main">
  <!-- 麵包屑 -->
  <div class="topbar">
    <div class="crumbs">
      <svg class="ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 5a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z"/></svg>
      @for (c of breadcrumb(); track c.relPath; let last = $last) {
        <span class="c" [class.cur]="last" (click)="enter(c.relPath)">{{ c.name }}</span>
        @if (!last) { <span class="sep">›</span> }
      }
    </div>
  </div>

  <!-- 子資料夾晶片(深層下鑽);無子夾則不顯示 -->
  @if (subfolders().length) {
    <div class="subbar">
      <span class="lab">子資料夾</span>
      @for (c of subfolders(); track c.relPath) {
        <button class="dchip" (click)="enterChild(c)">
          <svg viewBox="0 0 24 24" fill="currentColor"><path d="M4 5a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z"/></svg>
          {{ c.name }} <span class="n">{{ fmt(c.photoCount) }}</span>
        </button>
      }
    </div>
  }

  <!-- toolbar:當前夾命中數 -->
  <div class="toolbar"><span class="count">此資料夾含子夾 <b>{{ hitText() }}</b> 張</span></div>

  <!-- 遞迴圖牆 -->
  <div class="view">
    @if (error()) {
      <div class="empty">載入失敗:{{ error() }}</div>
    } @else if (!loading() && photos().length === 0) {
      <div class="empty">這個資料夾沒有圖片。{{ subfolders().length ? '點上方子資料夾往下看。' : '' }}</div>
    }
    <div class="masonry">
      @for (p of photos(); track p.id) {
        <div class="tile" (click)="pick(p)"><app-thumb [photoId]="p.id" [aspectRatio]="aspect(p)" /></div>
      }
    </div>
    @if (loading()) { <div class="empty">載入中…</div> }
    <div #sentinel class="scroll-sentinel" aria-hidden="true"></div>
  </div>
</div>
```

Create `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.css`(**只 `var(--token)`,不得 `@apply`**;麵包屑/subbar/masonry 對齊 mockup 與 photo-grid.css):

```css
:host { display: block; height: 100%; min-height: 0; }
.main { display: flex; flex-direction: column; min-width: 0; height: 100%; background: var(--color-canvas); }
.topbar { min-height: 48px; flex: none; border-bottom: 1px solid var(--color-hair); display: flex; align-items: center; gap: 10px; padding: 8px 15px; background: linear-gradient(180deg, #181b21, #15171c); }
.crumbs { display: flex; align-items: center; gap: 7px; font-size: 13.5px; flex-wrap: wrap; }
.crumbs .ic { width: 16px; height: 16px; color: var(--color-t-meta); flex: none; }
.crumbs .c { color: var(--color-muted); cursor: pointer; padding: 2px 5px; border-radius: 5px; }
.crumbs .c:hover { color: var(--color-text); background: var(--color-raised); }
.crumbs .c.cur { color: var(--color-text); font-weight: 600; }
.crumbs .sep { color: var(--color-faint); font-size: 11px; }
.subbar { min-height: 39px; flex: none; border-bottom: 1px solid var(--color-hair-soft); display: flex; align-items: center; gap: 8px; padding: 6px 15px; flex-wrap: wrap; }
.subbar .lab { font-size: 11.5px; color: var(--color-faint); flex: none; }
.dchip { display: inline-flex; align-items: center; gap: 8px; border: 1px solid var(--color-hair); background: var(--color-raised); border-radius: 7px; padding: 4px 11px; font-size: 12.5px; color: var(--color-text); cursor: pointer; transition: border-color var(--dur-fast), background var(--dur-fast); }
.dchip:hover { border-color: var(--color-t-meta); background: var(--color-raised-2); }
.dchip svg { width: 14px; height: 14px; color: var(--color-t-meta); flex: none; }
.dchip .n { font-family: var(--font-mono); font-size: 10px; color: var(--color-faint); }
.toolbar { height: 38px; flex: none; border-bottom: 1px solid var(--color-hair-soft); display: flex; align-items: center; gap: 14px; padding: 0 15px; color: var(--color-muted); font-size: 12.5px; }
.toolbar .count b { color: var(--color-text); font-family: var(--font-mono); font-weight: 500; }
.view { flex: 1; overflow-y: auto; min-height: 0; }
.empty { padding: 28px 18px; color: var(--color-muted); font-size: 13px; text-align: center; }
.masonry { column-count: 4; column-gap: 12px; padding: 16px 18px; }
@media (max-width: 1500px) { .masonry { column-count: 3; } }
.tile { break-inside: avoid; margin-bottom: 12px; border-radius: var(--radius-card); overflow: hidden; position: relative; cursor: pointer; border: 1px solid var(--color-hair-soft); background: var(--color-raised); transition: transform 0.16s, box-shadow 0.16s, border-color 0.16s; }
.tile:hover { transform: translateY(-3px); box-shadow: 0 14px 30px -14px rgba(0, 0, 0, 0.8); border-color: #3b4150; }
.scroll-sentinel { height: 1px; }
```

- [ ] **Step 6: build + 手測檢查點**

Run: `cd src/Pm.Web && npm run build`
Expected: build 成功。

手測(需後端;起 app:見專案 README,或 `dotnet run --project src/Pm.Api`,前端 `ng serve` 代理或 `ng build` 後由 .NET serve):
- 點 activity bar「資料夾瀏覽」→ 進 `/browse`,URL 自動補 `?root=<第一個有圖的 root>`。
- 側欄列出各 root + 第一層子資料夾,數字 = 遞迴 photo 數。
- 點側欄子資料夾 / 主區子夾晶片 → 麵包屑更新、圖牆換成該夾(含子夾)的圖、URL `path=` 變動。
- 點麵包屑上層 → 回上層。
- 捲到底 → 自動載更多(無限捲)。
- 空資料夾 → 顯示空狀態文案。

- [ ] **Step 7: Commit**

```bash
git add src/Pm.Web/src/app/app.routes.ts src/Pm.Web/src/app/shell/shell.html src/Pm.Web/src/app/features/browse/browse-view src/Pm.Web/src/app/features/browse/folder-tree-sidebar src/Pm.Web/src/app/features/browse/browse-grid
git commit -m "feat(web): /browse 資料夾瀏覽 — 入口 + 樹側欄 + 麵包屑 + 子夾下鑽 + 遞迴圖牆

activity bar 加入口;browse-view 訂閱 URL(root/path/q);folder-tree-sidebar
頂層並列 root + 第一層子夾;browse-grid 麵包屑 + 子資料夾晶片下鑽 + masonry
無限捲圖牆(複用 app-thumb)。深層靠主區子夾晶片,側欄不展到底。

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01GJJk3bb2x32Ru1AoxhVDZN"
```

---

### Task 5: 夾內疊 tag 自動完成 + 空狀態打磨

**Files:**
- Create: `src/Pm.Web/src/app/features/browse/inner-tag-filter/inner-tag-filter.ts` + `.html` + `.css`
- Modify: `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.ts` + `.html`(插入 filter 帶)

**Interfaces:**
- Consumes:`BrowseStore`(`innerTokens`/`addInnerTag`/`removeInnerTag`/`currentRootId`/`currentPath`)、`PmApi.folderTags`、`@core/tag-color` 的 `tagColor`。
- Produces:`app-inner-tag-filter` 元件(夾內篩 tag 帶,放在 browse-grid 的 subbar 與 toolbar 之間)。

- [ ] **Step 1: inner-tag-filter 元件**

Create `src/Pm.Web/src/app/features/browse/inner-tag-filter/inner-tag-filter.ts`(自動完成只查**夾內** tag,仿 photo-grid 的 combobox):

```typescript
import { Component, inject, signal, computed } from '@angular/core';
import { BrowseStore, type InnerToken } from '../browse.store';
import { PmApi, type FolderTag } from '@core/api/pm-api';
import { tagColor, DANGER, type TagKind } from '@core/tag-color';

// 夾內疊 tag:+tag 自動完成只列「當前資料夾範圍內實際存在」的 tag(打字即時過濾),選了 = 範圍 AND tag。
@Component({
  selector: 'app-inner-tag-filter',
  imports: [],
  templateUrl: './inner-tag-filter.html',
  styleUrl: './inner-tag-filter.css',
})
export class InnerTagFilter {
  private readonly store = inject(BrowseStore);
  private readonly api = inject(PmApi);
  readonly tokens = this.store.innerTokens;
  readonly kindColor = tagColor;

  readonly suggestions = signal<FolderTag[]>([]);
  readonly acIndex = signal(-1);
  private all: FolderTag[] = [];     // 當前夾全部可用 tag(載一次,前端過濾)
  private loadedKey = '';

  // 開啟輸入時載當前夾可用 tag(只在 root/path 變動時重載)。
  async ensureLoaded(): Promise<void> {
    const rootId = this.store.currentRootId();
    if (rootId === null) return;
    const key = `${rootId}:${this.store.currentPath()}`;
    if (key === this.loadedKey) return;
    this.loadedKey = key;
    try { this.all = await this.api.folderTags(rootId, this.store.currentPath()); }
    catch { this.all = []; }
  }

  async onType(v: string): Promise<void> {
    await this.ensureLoaded();
    this.acIndex.set(-1);
    const term = v.trim().toLowerCase().replace(/\s+/g, '_');
    const selected = new Set(this.tokens().map((t) => t.text.toLowerCase()));
    const rows = this.all
      .filter((r) => !selected.has(r.name.toLowerCase()) && (!term || r.name.toLowerCase().includes(term)))
      .slice(0, 12);
    this.suggestions.set(rows);
  }

  move(d: number): void {
    const n = this.suggestions().length; if (!n) return;
    this.acIndex.set(Math.max(0, Math.min(this.acIndex() + d, n - 1)));
  }
  onEnter(input: HTMLInputElement): void {
    const rows = this.suggestions(); const i = this.acIndex();
    const pick = i >= 0 && i < rows.length ? rows[i] : rows[0];
    if (pick) this.add(pick, input);
  }
  add(s: FolderTag, input: HTMLInputElement): void {
    this.store.addInnerTag(s.name, s.kind as TagKind);
    input.value = ''; this.close();
  }
  remove(idx: number, ev: Event): void { ev.stopPropagation(); this.store.removeInnerTag(idx); }
  close(): void { this.suggestions.set([]); this.acIndex.set(-1); }

  tokenStyle(t: InnerToken): Record<string, string> {
    const c = this.kindColor(t.kind);
    return { color: c, background: this.rgba(c, 0.12), 'border-color': this.rgba(c, 0.34) };
  }
  private rgba(hex: string, a: number): string {
    const n = parseInt(hex.slice(1), 16);
    return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
  }
}
```

Create `src/Pm.Web/src/app/features/browse/inner-tag-filter/inner-tag-filter.html`(對齊 mockup 的 `.filterbar`):

```html
<div class="filterbar">
  <span class="lab">夾內再篩</span>
  @for (t of tokens(); track $index) {
    <span class="tchip" [style]="tokenStyle(t)">
      {{ t.text }} <span class="x" (click)="remove($index, $event)">×</span>
    </span>
  }
  <div class="ac-wrap">
    <input #fi class="addinput" placeholder="+ tag"
      (focus)="onType(fi.value)" (input)="onType(fi.value)"
      (keydown.arrowdown)="$event.preventDefault(); move(1)"
      (keydown.arrowup)="$event.preventDefault(); move(-1)"
      (keydown.enter)="onEnter(fi)" (keydown.escape)="close()" />
    @if (suggestions().length) {
      <div class="ac-pop">
        @for (s of suggestions(); track s.name; let i = $index) {
          <button type="button" class="ac-row" [class.active]="acIndex() === i"
            (mousedown)="$event.preventDefault()" (click)="add(s, fi)">
            <span class="dot" [style.color]="kindColor(s.kind)" [style.background]="kindColor(s.kind)"></span>
            <span class="acname">{{ s.name }}</span>
            <span class="account">{{ s.count }}</span>
          </button>
        }
      </div>
    }
  </div>
</div>
```

Create `src/Pm.Web/src/app/features/browse/inner-tag-filter/inner-tag-filter.css`(**只 `var(--token)`**;對齊 photo-grid.css 的 ac-pop/ac-row + mockup filterbar):

```css
:host { display: block; }
.filterbar { min-height: 40px; display: flex; align-items: center; gap: 9px; padding: 7px 15px; border-bottom: 1px solid var(--color-hair-soft); background: rgba(34, 211, 238, 0.025); flex-wrap: wrap; }
.filterbar .lab { font-size: 11.5px; color: var(--color-faint); flex: none; }
.tchip { display: inline-flex; align-items: center; gap: 6px; border-radius: 20px; padding: 3px 6px 3px 10px; font-size: 12px; font-weight: 600; white-space: nowrap; border: 1px solid transparent; }
.tchip .x { opacity: 0.55; font-family: var(--font-mono); cursor: pointer; }
.ac-wrap { position: relative; display: flex; }
.addinput { border: 1px dashed #3b4150; border-radius: 20px; padding: 3px 11px; font-size: 12px; color: var(--color-text); background: transparent; outline: 0; min-width: 90px; }
.addinput:focus-visible { border-color: var(--color-accent); border-style: solid; }
.ac-pop { position: absolute; top: calc(100% + 7px); left: 0; min-width: 210px; max-height: 260px; overflow-y: auto; z-index: 50; background: var(--color-panel); border: 1px solid var(--color-hair); border-radius: 8px; box-shadow: var(--shadow-2); padding: 4px; }
.ac-row { width: 100%; display: flex; align-items: center; gap: 8px; padding: 6px 8px; border: 0; border-radius: 6px; background: transparent; color: var(--color-text); font-size: 12.5px; text-align: left; cursor: pointer; }
.ac-row:hover, .ac-row.active { background: var(--color-raised); }
.ac-row .dot { width: 8px; height: 8px; border-radius: 50%; flex: none; }
.ac-row .acname { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ac-row .account { flex: none; font-family: var(--font-mono); font-size: 10px; color: var(--color-faint); }
```

- [ ] **Step 2: 接入 browse-grid**

Modify `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.ts`:`import` 與 `imports` 陣列加 `InnerTagFilter`:

```typescript
import { InnerTagFilter } from '../inner-tag-filter/inner-tag-filter';
// @Component imports: [Thumb, InnerTagFilter]
```

Modify `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.html`:在 `@if (subfolders()...) { 子夾 subbar }` 之後、`<div class="toolbar">` 之前插入:

```html
  <app-inner-tag-filter />
```

並把空狀態文案補上「篩 tag 後 0 結果」分支 —— 將 `.empty` 那段改為:

```html
    } @else if (!loading() && photos().length === 0) {
      <div class="empty">
        @if (store.innerTokensLength()) { 此資料夾內沒有符合篩選的圖片。試著移除一些夾內 tag。 }
        @else { 這個資料夾沒有圖片。{{ subfolders().length ? '點上方子資料夾往下看。' : '' }} }
      </div>
```

為此在 `browse-grid.ts` 注入 store 並加一個 helper(或直接讀 store):把 `private readonly store` 改 `readonly store`,並在 BrowseStore 加一個 `readonly innerTokensLength = computed(() => this._innerTokens().length)` 暴露(Task 3 的 store 末加此 computed)。

> 註:若不想動 Task 3 的 store,可改在 browse-grid 內 `readonly innerCount = computed(() => this.store.innerTokens().length)` 並在模板用 `innerCount()`。二擇一,擇一即可,勿兩者並存。

- [ ] **Step 3: build + 手測**

Run: `cd src/Pm.Web && npm run build`
Expected: build 成功。

手測(起後端 + 有 WD14 tag 的圖庫,或手動加 manual tag 的圖):
- 進某夾 → 「夾內再篩」帶出現,按 `+ tag` 輸入框 focus → 浮層列出**該夾內實際有的 tag** + 各自張數(不是全庫 tag)。
- 選一個 → 成 chip、圖牆縮到符合的、命中數變小、URL `q=` 變動。
- 換到另一個夾 → 可用 tag 清單跟著變。
- 移除 chip → 回到純資料夾瀏覽。
- 篩到 0 結果 → 顯示「沒有符合篩選」空狀態。
- 鍵盤:`+tag` 輸入框 Tab 進得去、focus ring 可見(未被 outline:none 蓋)。

- [ ] **Step 4: Commit**

```bash
git add src/Pm.Web/src/app/features/browse/inner-tag-filter src/Pm.Web/src/app/features/browse/browse-grid src/Pm.Web/src/app/features/browse/browse.store.ts
git commit -m "feat(web): browse 夾內疊 tag 自動完成 + 空狀態

inner-tag-filter:+tag 自動完成只列當前夾內實際存在的 tag(folderTags),
選了 = 資料夾範圍 AND tag(經 URL q=);空資料夾 / 篩無結果各自空狀態文案。

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01GJJk3bb2x32Ru1AoxhVDZN"
```

---

## 前端完成驗證

- [ ] `cd src/Pm.Web && npm run build` 綠、`npm test`(含 browse-tree 8 斷言)綠。
- [ ] 手測全流程:activity 入口 → 多 root 並列 → 樹下鑽 / 麵包屑 / 子夾晶片 → 遞迴圖牆無限捲 → 夾內疊 tag → 空狀態。
- [ ] 視覺對齊 mockup `docs/mockups/folder-dimension-design.html` 分頁 2。
- [ ] 樣式鐵則:browse 三個元件 `.css` 全 `var(--token)`、無 `@apply`;masonry 未叫 `.grid`;focus ring 未被蓋。
- [ ] 後端遺留 Minor 一併處理:此時可抽 `PhotoQueryService.ApplyScopeFilter` private helper(連同既有 includeGroups/excludeIds 重複),如最終整體審查所建議 —— 視為獨立小 commit,跑後端測試確認綠。

## Self-Review(對 spec/前端範本)

- spec §五 BrowseStore + 5 元件 → Task 3(store/api/純函式)+ Task 4(view/sidebar/grid)+ Task 5(inner-tag-filter)✅。
- D1 獨立路由 `/browse` → Task 4 Step 1 ✅;D3 遞迴顯示 → 圖牆接 `pathPrefix` 範圍(後端遞迴)✅;D4 側欄淺樹 + 主區子夾下鑽 → folder-tree-sidebar 只渲第一層 + browse-grid subbar 晶片 ✅;D5 夾內 tag 扁平只列範圍內 → inner-tag-filter `folderTags` ✅;多 root → 側欄頂層並列 ✅。
- URL 單一真相 → BrowseStore `pushUrl` + browse-view 訂閱 ✅;空狀態 → Task 5 ✅。
- 樣式鐵則(元件 .css 無 @apply、var token、masonry 非 .grid)→ Global Constraints + 各 .css 已遵守 ✅。
- 型別跨 Task 一致(`FolderNode`/`FolderRoot`/`FolderTag`/`InnerToken`/`BrowseStore` 方法名)✅。
- 無 placeholder;Task 5 Step 2 的 `innerTokensLength` 二擇一已明確標示擇一。
