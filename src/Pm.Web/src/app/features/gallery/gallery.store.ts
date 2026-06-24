import { Injectable, computed, inject, signal, DestroyRef } from '@angular/core';
import { PmApi, type PhotoListItem } from '@core/api/pm-api';
import { type TagKind } from '@core/tag-color';
import { toggleExclude } from '@core/tag-search';

// 相簿資料來源 store:元件與 API 之間的唯一接縫。
// 資料一律來自 @core/api/pm-api 的 PmApi;元件只讀 store 的 signal。

// 頂欄搜尋 token:text 文字無 '-' 前綴 = all;'-x' = none(去掉 '-')。
export interface SearchToken {
  text: string;
  kind: TagKind;
}

// 側欄 facet 樹節點(由 PmApi.tagTree() 映射:count → n)。
export interface FacetNode {
  name: string;
  kind: TagKind;
  n: number;
  multi?: boolean;
  children?: FacetNode[];
}

const PAGE_SIZE = 60;

// 把 token 文字切成 all / none(去掉 '-' 前綴)。
function splitTokens(tokens: readonly SearchToken[]): { all: string[]; none: string[] } {
  const all: string[] = [];
  const none: string[] = [];
  for (const t of tokens) {
    const raw = t.text.trim();
    if (!raw) continue;
    if (raw.startsWith('-')) none.push(raw.slice(1));
    else all.push(raw);
  }
  return { all, none };
}

// TagTreeNode(count)→ FacetNode(n)遞迴映射。
function mapNode(n: { name: string; kind: string; count: number; multi?: boolean; children?: unknown }): FacetNode {
  const kids = (n.children as FacetNode['children'] | null | undefined) ?? undefined;
  return {
    name: n.name,
    kind: n.kind as TagKind,
    n: n.count,
    multi: n.multi,
    children: kids
      ? (n.children as { name: string; kind: string; count: number; multi?: boolean; children?: unknown }[]).map(mapNode)
      : undefined,
  };
}

@Injectable({ providedIn: 'root' })
export class GalleryStore {
  private readonly api = inject(PmApi);

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

  // ---- 圖牆資料(keyset 無限捲,累積)----
  private readonly _photos = signal<PhotoListItem[]>([]);
  readonly photos = this._photos.asReadonly();

  private readonly _nextCursor = signal<number | null>(null);
  readonly nextCursor = this._nextCursor.asReadonly();
  readonly hasMore = computed(() => this._nextCursor() !== null);

  private readonly _loading = signal(false);
  readonly loading = this._loading.asReadonly();
  private readonly _error = signal<string | null>(null);
  readonly error = this._error.asReadonly();

  // 命中數:真實總數(來自 /api/search/count)。
  private readonly _hitCount = signal(0);
  readonly hitCount = this._hitCount.asReadonly();

  // WD14 待標佇列:真實 pending+error(每 4s 輪詢)。
  private readonly _wd14Queue = signal(0);
  readonly wd14Queue = this._wd14Queue.asReadonly();

  // ---- 頂欄搜尋 token ----
  private readonly _tokens = signal<SearchToken[]>([]);
  readonly tokens = this._tokens.asReadonly();

  // ---- 側欄 facet ----
  private readonly _tree = signal<FacetNode[]>([]);
  readonly tree = this._tree.asReadonly();
  private readonly _rootless = signal<FacetNode[]>([]);
  readonly rootless = this._rootless.asReadonly();
  private readonly _facetsGeneral = signal<[string, number][]>([]);
  readonly facetsGeneral = this._facetsGeneral.asReadonly();
  private readonly _facetsMeta = signal<[string, number][]>([]);
  readonly facetsMeta = this._facetsMeta.asReadonly();

  // ---- 選取狀態(供 inspector)----
  private readonly _selectedId = signal<number | null>(null);
  readonly selectedId = this._selectedId.asReadonly();

  // 縮圖 URL(給 template;絕不碰原圖)。
  thumbUrl(id: number): string {
    return this.api.thumbUrl(id);
  }

  // 初次載入:facet 樹 + 第一頁圖。
  async load(): Promise<void> {
    await Promise.all([this.loadFacets(), this.search()]);
  }

  // 重新搜尋(token 變動時呼叫):重置游標與累積清單。
  async search(): Promise<void> {
    this._loading.set(true);
    this._error.set(null);
    const { all, none } = splitTokens(this._tokens());
    try {
      const [count, page] = await Promise.all([
        this.api.searchCount({ all, none }),
        this.api.search({ all, none, afterId: null, pageSize: PAGE_SIZE }),
      ]);
      this._hitCount.set(count.total);
      this._photos.set(page.items);
      this._nextCursor.set(page.nextCursor ?? null);
    } catch (e) {
      this._error.set(this.msg(e));
      this._photos.set([]);
      this._hitCount.set(0);
      this._nextCursor.set(null);
    } finally {
      this._loading.set(false);
    }
  }

  // 無限捲:載入下一頁並累積。
  async loadMore(): Promise<void> {
    const cursor = this._nextCursor();
    if (cursor === null || this._loading()) return;
    this._loading.set(true);
    this._error.set(null);
    const { all, none } = splitTokens(this._tokens());
    try {
      const page = await this.api.search({ all, none, afterId: cursor, pageSize: PAGE_SIZE });
      this._photos.update((cur) => [...cur, ...page.items]);
      this._nextCursor.set(page.nextCursor ?? null);
    } catch (e) {
      this._error.set(this.msg(e));
    } finally {
      this._loading.set(false);
    }
  }

  // 重新整理目前查詢。
  async refresh(): Promise<void> {
    await this.search();
  }

  // 載入 facet 樹。
  async loadFacets(): Promise<void> {
    try {
      const t = await this.api.tagTree();
      this._tree.set(t.tree.map(mapNode));
      this._rootless.set(t.rootless.map(mapNode));
      this._facetsGeneral.set(t.general);
      this._facetsMeta.set(t.meta);
    } catch (e) {
      this._error.set(this.msg(e));
      this._tree.set([]);
      this._rootless.set([]);
      this._facetsGeneral.set([]);
      this._facetsMeta.set([]);
    }
  }

  // 設定 token 並重新搜尋。
  setTokens(tokens: SearchToken[]): void {
    this._tokens.set(tokens);
    void this.search();
  }

  // 加一個 token(text 無 '-' = all;'-x' = none)並重新搜尋;已有同 text 則略過(去重)。
  addToken(token: SearchToken): void {
    const text = token.text.trim();
    if (!text) return;
    if (this._tokens().some((x) => x.text === text)) return;
    this._tokens.update((ts) => [...ts, { ...token, text }]);
    void this.search();
  }

  // 移除頂欄 token 並重新搜尋。
  removeToken(idx: number): void {
    this._tokens.update((ts) => ts.filter((_, i) => i !== idx));
    void this.search();
  }

  // 切換某 token 的 排除/包含(翻 '-' 前綴)並重新搜尋。
  toggleToken(idx: number): void {
    this._tokens.update((ts) => ts.map((t, i) => (i === idx ? { ...t, text: toggleExclude(t.text) } : t)));
    void this.search();
  }

  // 設定選取(供 inspector)。
  select(id: number | null): void {
    this._selectedId.set(id);
  }

  private msg(e: unknown): string {
    if (e instanceof Error) return e.message;
    return '載入失敗';
  }
}
