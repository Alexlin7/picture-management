# 搜尋重構(Spec 3 ①)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 gallery 頂端搜尋從「逼打運算子語法」改成「下拉驅動 substring 探索」:打片段→下拉挑既有標籤(AND 隱含)、點 chip 切排除、精準 Enter 驗證、中文顯示名也找得到。

**Architecture:** 純前端。新增 `core/tag-search.ts` 一組純函式(可 vitest 測),`GalleryStore` 加一個 token 切換方法,`photo-grid` 元件改 autocomplete/Enter/chip 互動。後端 `/api/tags`(canonical 不分大小寫 substring + 依張數排序)與 `/api/search`(All/None 布林)皆已就緒,**不動後端**。

**Tech Stack:** Angular 22(signals)、@angular/cdk、TypeScript 6、vitest 4(globals:true,測試不 import vitest)、Tailwind v4。

## Global Constraints

- 純前端:不碰後端 / SQLite / 原圖 / canonical tag;只動 `src/Pm.Web/`。
- 顯示層鐵則:canonical(`tag.name`)照存照搜;`tag-search.ts` 只做「比對用」字串轉換與反查,不改查詢送出的語意(送出仍是 canonical token)。
- UI lib 僅 `@angular/cdk`(不引 Material/PrimeNG)。
- 小切片、逐步 commit;純函式走 `npx ng test --watch=false`(綠)、UI 改動走 `npx ng build`(0 錯)+ 起 app 手測。
- vitest 設定為 globals(`describe`/`it`/`expect` 免 import;測試檔只 import 受測函式),比照 `core/tag-display.spec.ts`。

---

## File Structure

- **Create** `src/Pm.Web/src/app/core/tag-search.ts` — 搜尋用純函式:`normalizeTagQuery`、`toggleExclude`、`exactMatch`、`reverseDisplayLookup`。
- **Create** `src/Pm.Web/src/app/core/tag-search.spec.ts` — 上述純函式測試。
- **Modify** `src/Pm.Web/src/app/features/gallery/gallery.store.ts` — 加 `toggleToken(idx)`。
- **Modify** `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts` — autocomplete 正規化、精準 Enter 驗證、`noSuchTag` 提示、`toggleToken`、下拉用 `displayOf` 標籤、(Task 6)中文反查。
- **Modify** `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html` — chip 點選切排除、下拉標籤、常駐提示、查無此標訊息。

---

### Task 1: `normalizeTagQuery` 純函式

**Files:**
- Create: `src/Pm.Web/src/app/core/tag-search.ts`
- Test: `src/Pm.Web/src/app/core/tag-search.spec.ts`

**Interfaces:**
- Produces: `normalizeTagQuery(input: string): string` — 去前綴 `-`、trim、轉小寫、內部連續空白收成單一 `_`(治多字作品名)。

- [ ] **Step 1: 寫失敗測試**

建 `src/Pm.Web/src/app/core/tag-search.spec.ts`:

```ts
import { normalizeTagQuery } from './tag-search';

describe('normalizeTagQuery', () => {
  it('多字作品名空白轉底線', () => {
    expect(normalizeTagQuery('blue archive')).toBe('blue_archive');
  });
  it('去排除前綴 + 頭尾空白 + 轉小寫', () => {
    expect(normalizeTagQuery('  -Blue Archive ')).toBe('blue_archive');
  });
  it('連續空白收成單一底線', () => {
    expect(normalizeTagQuery('long   hair')).toBe('long_hair');
  });
  it('純空白回空字串', () => {
    expect(normalizeTagQuery('   ')).toBe('');
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd src/Pm.Web ; npx ng test --watch=false`
Expected: FAIL（`tag-search.ts` 不存在 / 匯出未定義）

- [ ] **Step 3: 最小實作**

建 `src/Pm.Web/src/app/core/tag-search.ts`:

```ts
// 搜尋用純函式(無副作用)。canonical 照存照搜;本檔只做「比對用」字串轉換與反查。

// 使用者輸入 → 比對用字串:去 '-' 前綴、trim、轉小寫、內部連續空白收成單一 '_'。
export function normalizeTagQuery(input: string): string {
  return input
    .replace(/^-/, '')
    .trim()
    .toLowerCase()
    .replace(/\s+/g, '_');
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd src/Pm.Web ; npx ng test --watch=false`
Expected: PASS（含既有 36 測試仍綠）

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/tag-search.ts src/Pm.Web/src/app/core/tag-search.spec.ts
git commit -m "feat(web): tag-search normalizeTagQuery 純函式(TDD)"
```

---

### Task 2: `toggleExclude` 純函式

**Files:**
- Modify: `src/Pm.Web/src/app/core/tag-search.ts`
- Test: `src/Pm.Web/src/app/core/tag-search.spec.ts`

**Interfaces:**
- Produces: `toggleExclude(text: string): string` — 切換 token 文字的 `-` 排除前綴。

- [ ] **Step 1: 寫失敗測試**（加到 `tag-search.spec.ts` 末尾）

```ts
import { toggleExclude } from './tag-search';

describe('toggleExclude', () => {
  it('無前綴 → 加 -', () => {
    expect(toggleExclude('smile')).toBe('-smile');
  });
  it('有前綴 → 去 -', () => {
    expect(toggleExclude('-smile')).toBe('smile');
  });
});
```

> 註:把 `import { toggleExclude }` 併進檔首既有 import:`import { normalizeTagQuery, toggleExclude } from './tag-search';`

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd src/Pm.Web ; npx ng test --watch=false`
Expected: FAIL（`toggleExclude` 未定義）

- [ ] **Step 3: 最小實作**（加到 `tag-search.ts`）

```ts
// 切換排除前綴:無 '-' → 加;有 '-' → 去。
export function toggleExclude(text: string): string {
  return text.startsWith('-') ? text.slice(1) : '-' + text;
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd src/Pm.Web ; npx ng test --watch=false`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/tag-search.ts src/Pm.Web/src/app/core/tag-search.spec.ts
git commit -m "feat(web): tag-search toggleExclude 純函式(TDD)"
```

---

### Task 3: `exactMatch` 純函式

**Files:**
- Modify: `src/Pm.Web/src/app/core/tag-search.ts`
- Test: `src/Pm.Web/src/app/core/tag-search.spec.ts`

**Interfaces:**
- Consumes: `normalizeTagQuery`(Task 1);`TagListRow`(`@core/api/pm-api`:`{ id: number; name: string; kind: string; count: number }`)。
- Produces: `exactMatch(rows: readonly TagListRow[], term: string): TagListRow | null` — 在建議中找「正規化名稱完全相等」者。

- [ ] **Step 1: 寫失敗測試**（加到 `tag-search.spec.ts` 末尾）

```ts
import { exactMatch } from './tag-search';
import type { TagListRow } from '@core/api/pm-api';

const rows: TagListRow[] = [
  { id: 1, name: 'smile', kind: 'general', count: 9 },
  { id: 2, name: 'blue_archive', kind: 'copyright', count: 3 },
];

describe('exactMatch', () => {
  it('多字輸入正規化後精準命中', () => {
    expect(exactMatch(rows, 'Blue Archive')?.id).toBe(2);
  });
  it('非完全相等 → null', () => {
    expect(exactMatch(rows, 'smil')).toBeNull();
  });
  it('空輸入 → null', () => {
    expect(exactMatch(rows, '  ')).toBeNull();
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd src/Pm.Web ; npx ng test --watch=false`
Expected: FAIL（`exactMatch` 未定義）

- [ ] **Step 3: 最小實作**（加到 `tag-search.ts`;檔首加 import）

檔首加:
```ts
import type { TagListRow } from '@core/api/pm-api';
```

實作:
```ts
// 在既有建議中找「正規化後名稱完全相等」者(精準 Enter 用);無 → null。
export function exactMatch(rows: readonly TagListRow[], term: string): TagListRow | null {
  const t = normalizeTagQuery(term);
  if (!t) return null;
  return rows.find((r) => r.name.toLowerCase() === t) ?? null;
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd src/Pm.Web ; npx ng test --watch=false`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/tag-search.ts src/Pm.Web/src/app/core/tag-search.spec.ts
git commit -m "feat(web): tag-search exactMatch 純函式(TDD)"
```

---

### Task 4: `reverseDisplayLookup` 純函式(中文反查)

**Files:**
- Modify: `src/Pm.Web/src/app/core/tag-search.ts`
- Test: `src/Pm.Web/src/app/core/tag-search.spec.ts`

**Interfaces:**
- Consumes: `EXPRESSION_DISPLAY_MAP`(`@core/tag-display` 或相對 `./tag-display`:`Record<string, { label: string; emoji: string; group: 'expression' }>`)。
- Produces: `reverseDisplayLookup(term: string): string[]` — 回傳「顯示 label 含此片段」的 canonical 英文 tag 名(僅 curated 顯示名)。

- [ ] **Step 1: 寫失敗測試**（加到 `tag-search.spec.ts` 末尾）

```ts
import { reverseDisplayLookup } from './tag-search';

describe('reverseDisplayLookup', () => {
  it('中文顯示名反查回 canonical', () => {
    expect(reverseDisplayLookup('微笑')).toContain('smile');
  });
  it('片段可命中多個 label', () => {
    // '笑' 命中 微笑/咧嘴笑/淺笑… 至少 2 個
    expect(reverseDisplayLookup('笑').length).toBeGreaterThan(1);
  });
  it('空片段 → 空陣列', () => {
    expect(reverseDisplayLookup('  ')).toEqual([]);
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd src/Pm.Web ; npx ng test --watch=false`
Expected: FAIL（`reverseDisplayLookup` 未定義）

- [ ] **Step 3: 最小實作**（加到 `tag-search.ts`;檔首加 import）

檔首加:
```ts
import { EXPRESSION_DISPLAY_MAP } from './tag-display';
```

實作:
```ts
// 中文/顯示名反查:回傳「顯示 label 含此片段」的 canonical 英文 tag 名。
// 僅覆蓋 curated 顯示名(表情等);空片段或無命中 → []。
export function reverseDisplayLookup(term: string): string[] {
  const t = term.trim();
  if (!t) return [];
  const out: string[] = [];
  for (const [canonical, entry] of Object.entries(EXPRESSION_DISPLAY_MAP)) {
    if (entry.label.includes(t)) out.push(canonical);
  }
  return out;
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd src/Pm.Web ; npx ng test --watch=false`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/tag-search.ts src/Pm.Web/src/app/core/tag-search.spec.ts
git commit -m "feat(web): tag-search reverseDisplayLookup 中文反查(TDD)"
```

---

### Task 5: store `toggleToken` + photo-grid 核心互動

把純函式接進 UI:autocomplete 正規化查詢、精準 Enter 驗證 + 查無此標、點 chip 切排除、下拉用顯示名、常駐提示;移除「打字空格=AND」舊路徑。

**Files:**
- Modify: `src/Pm.Web/src/app/features/gallery/gallery.store.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html`

**Interfaces:**
- Consumes: `normalizeTagQuery`/`exactMatch`/`toggleExclude`(Task 1-3);`displayOf`(`@core/tag-display`:`displayOf(tag: { name: string; kind: string }): { label: string; work?: string | null; ... }`);`SearchToken`(`{ text: string; kind: TagKind }`);`GalleryStore.addToken`/`removeToken`。
- Produces: `GalleryStore.toggleToken(idx: number): void`。

- [ ] **Step 1: store 加 `toggleToken`**

`gallery.store.ts` 檔首 import 加入 `toggleExclude`:
```ts
import { toggleExclude } from '@core/tag-search';
```

在 `removeToken(...)` 之後加:
```ts
  // 切換某 token 的 排除/包含(翻 '-' 前綴)並重新搜尋。
  toggleToken(idx: number): void {
    this._tokens.update((ts) => ts.map((t, i) => (i === idx ? { ...t, text: toggleExclude(t.text) } : t)));
    void this.search();
  }
```

- [ ] **Step 2: photo-grid.ts 改 autocomplete / Enter / 互動**

`photo-grid.ts` 檔首 import 區加入:
```ts
import { normalizeTagQuery, exactMatch } from '@core/tag-search';
import { displayOf } from '@core/tag-display';
```

新增 `noSuchTag` signal(放在 `suggestions` signal 附近):
```ts
  readonly noSuchTag = signal<string | null>(null);
```

把 `onType` 換成(清 noSuchTag、傳原始 term):
```ts
  onType(v: string): void {
    this.acIndex.set(-1);
    this.noSuchTag.set(null);
    const term = v.trim();
    if (this.acDebounce) clearTimeout(this.acDebounce);
    if (!term) { this.suggestions.set([]); return; }
    this.acDebounce = setTimeout(() => void this.doSuggest(term), 180);
  }
```

把 `doSuggest` 換成(送出前正規化、limit 放大到 12):
```ts
  private async doSuggest(term: string): Promise<void> {
    const seq = ++this.acSeq;
    try {
      const rows = await this.api.tags(normalizeTagQuery(term), 12);
      if (seq === this.acSeq) this.suggestions.set(rows);
    } catch {
      if (seq === this.acSeq) this.suggestions.set([]);
    }
  }
```

把 `onEnter` 換成(精準 exact 驗證 + 查無此標,移除 addSearch):
```ts
  onEnter(input: HTMLInputElement): void {
    const rows = this.suggestions();
    const i = this.acIndex();
    if (i >= 0 && i < rows.length) {
      this.pickSuggestion(rows[i], input);
      return;
    }
    const hit = exactMatch(rows, input.value);
    if (hit) {
      this.store.addToken({ text: hit.name, kind: hit.kind as SearchToken['kind'] });
      input.value = '';
      this.closeAc();
    } else {
      this.noSuchTag.set(`查無此標:${input.value.trim()}`);
    }
  }
```

刪除整個 `addSearch(...)` 方法(打字空格=AND 舊路徑已退場,不再被呼叫)。

加 `toggleToken` 與下拉顯示名 helper(放在 `removeToken` 附近):
```ts
  // 點 token chip(非 ×)→ 切換 排除/包含。
  toggleToken(idx: number, ev: Event): void {
    ev.stopPropagation();
    this.store.toggleToken(idx);
  }

  // 下拉建議的顯示文字:中文顯示名 + 角色作品(displayOf);退回底線轉空白。
  sugLabel(s: TagListRow): string {
    const d = displayOf({ name: s.name, kind: s.kind });
    return d.work ? `${d.label} 〔${d.work}〕` : d.label;
  }
```

- [ ] **Step 3: photo-grid.html 改 chip / 下拉 / 提示**

把 token chip 區塊(現 `<span class="tok" ...>`)換成(整個 chip 可點切排除,× 仍移除):
```html
      @for (t of tokens(); track $index) {
        <span class="tok" [style]="tokenStyle(t)" (click)="toggleToken($index, $event)" title="點一下切換 排除／包含">
          <b>{{ t.text }}</b>
          <span class="x" (click)="removeToken($index, $event)">×</span>
        </span>
      }
```

把下拉建議列的名稱(現 `<span class="acname">{{ s.name }}</span>`)換成:
```html
                <span class="acname">{{ sugLabel(s) }}</span>
```

在下拉 `@if (suggestions().length) { ... }` 之外、`.ac-wrap` 收尾前,加「查無此標」訊息:
```html
        @if (noSuchTag()) {
          <div class="ac-pop"><div class="ac-empty">{{ noSuchTag() }}</div></div>
        }
```

在 `.search` 容器之後(`<button class="btn ghost" ...儲存搜尋>` 之前)加常駐提示:
```html
    <div class="search-hint">挑標籤＝AND · 點標籤切換排除 · Enter 套用精準標</div>
```

- [ ] **Step 4: 補極簡樣式**（`photo-grid.css` 末尾追加;沿用 token var,勿裸 hex）

```css
.search-hint {
  font-size: 11px;
  color: var(--color-faint);
  white-space: nowrap;
  align-self: center;
}
.ac-empty {
  padding: 8px 11px;
  font-size: 12.5px;
  color: var(--color-muted);
}
```

- [ ] **Step 5: build 驗證**

Run: `cd src/Pm.Web ; npx ng build`
Expected: `Application bundle generation complete`、0 錯。

- [ ] **Step 6: 手測（起 app,逐項確認）**

Run: `dotnet run --project src/Pm.Api`（另窗 `cd src/Pm.Web ; npm start`,開 `http://localhost:4200`）
逐項:
1. 打「blue」→ 下拉出多個含 blue 的 tag(blue_eyes / blue_archive / mika_(blue_archive)…),依張數排,角色列顯示 `名 〔作品〕`。
2. 打「blue archive」→ 下拉出 `blue_archive` / `mika_(blue_archive)`(空格不再拆 AND)。
3. 從下拉挑兩個 tag → 兩個 token,圖牆 AND 收窄。
4. 點某 token chip → 變紅色刪除線(排除),查詢更新;再點一下變回包含。
5. 打一個確切既有 tag 直接 Enter(不選下拉)→ 套成正確 kind 顏色的 token。
6. 打一個不存在的字 Enter → 顯示「查無此標:…」,不亂加 token。
7. 頂端常駐提示可見。

- [ ] **Step 7: Commit**

```bash
git add src/Pm.Web/src/app/features/gallery/gallery.store.ts \
  src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts \
  src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html \
  src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.css
git commit -m "feat(web): 搜尋改下拉驅動(正規化查詢/精準Enter/點chip切排除/顯示名/提示)"
```

---

### Task 6: 中文反查接進 autocomplete

讓打 curated 中文顯示名(如「微笑」)也能在下拉找到對應英文 tag。

**Files:**
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts`

**Interfaces:**
- Consumes: `reverseDisplayLookup`(Task 4);`PmApi.tags`;`TagListRow`。

- [ ] **Step 1: photo-grid.ts import 加入 `reverseDisplayLookup`**

```ts
import { normalizeTagQuery, exactMatch, reverseDisplayLookup } from '@core/tag-search';
```

- [ ] **Step 2: `doSuggest` 合併反查結果**

把 `doSuggest` 換成:
```ts
  private async doSuggest(term: string): Promise<void> {
    const seq = ++this.acSeq;
    try {
      const main = await this.api.tags(normalizeTagQuery(term), 12);
      const canon = reverseDisplayLookup(term).slice(0, 5);
      const extra = canon.length
        ? (await Promise.all(canon.map((c) => this.api.tags(c, 1)))).flat()
        : [];
      if (seq !== this.acSeq) return;
      const byId = new Map<number, TagListRow>();
      for (const r of [...extra, ...main]) byId.set(r.id, r); // 反查命中優先,依 id 去重
      this.suggestions.set([...byId.values()]);
    } catch {
      if (seq === this.acSeq) this.suggestions.set([]);
    }
  }
```

- [ ] **Step 3: build 驗證**

Run: `cd src/Pm.Web ; npx ng build`
Expected: 0 錯。

- [ ] **Step 4: 手測**

開 `http://localhost:4200`,搜尋框打「微笑」→ 下拉出現 `smile`(中文反查命中);打「臉紅」→ 出現 `blush`。英文 substring(blue 等)行為不變。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts
git commit -m "feat(web): 搜尋 autocomplete 接中文顯示名反查"
```

---

## Self-Review

**Spec coverage(對照 `2026-06-24-gallery-topbar-ux-design.md` ①):**
- 下拉驅動 substring → Task 5 doSuggest 正規化 + 下拉挑。✓
- AND 隱含 → 沿用既有 token 間 AND(store splitTokens),挑即加 token。✓
- 空白正規化 → Task 1 `normalizeTagQuery`(空格→`_`)+ Task 5 接入。✓
- 排除改點選 → Task 2 `toggleExclude` + Task 5 chip click + store `toggleToken`。✓
- 精準 Enter 驗證 + 查無此標 → Task 3 `exactMatch` + Task 5 `onEnter`/`noSuchTag`。✓
- 常駐提示 → Task 5 `.search-hint`。✓
- 中文顯示名反查 → Task 4 + Task 6。✓
- 下拉顯示解析結構 → Task 5 `sugLabel`(displayOf label + work)。✓
- **不在本案**:掃描鈕刪除(②)、儲存搜尋接線(③)、批次 requeue(④)—— 屬 Spec 3 後續切片,本計畫只做 ①(prototype-first 先玩搜尋)。

**Placeholder scan:** 無 TODO/TBD;每個改動步驟附完整碼。✓
**Type consistency:** `TagListRow {id,name,kind,count}`、`SearchToken {text,kind}`、`displayOf({name,kind})→{label,work}` 全計畫一致;`toggleToken` store 簽名與 photo-grid 呼叫一致。✓
