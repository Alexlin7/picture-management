import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { PmApi, type PhotoListItem, type FolderNode, type FolderRoot } from '@core/api/pm-api';
import { type TagKind } from '@core/tag-color';
import { encodeTokens, decodeTokens, splitTokens } from '@core/tag-search';
import { breadcrumbFromPath, subfoldersOf } from './browse-tree';

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

  // 查詢世代:每次 applyUrl 套用新 URL 即 ++,使在途的舊 search/loadTree/loadMore 回應失效。
  // 切夾競態的單一真相 —— 所有非同步寫入前都比對自己的 gen 是否仍是當前世代。
  private gen = 0;

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
    const gen = ++this.gen;   // 新一輪 URL 套用 → 使在途的舊查詢/loadMore 失效
    if (rootId !== null && rootId !== this._rootId()) {
      this._rootId.set(rootId);
      await this.loadTree(rootId, gen);
      if (gen !== this.gen) return;   // 已被更新的 URL 取代 → 整個放棄
    }
    this._path.set(path);
    this._innerTokens.set(decodeTokens(q));
    await this.search(gen);
  }

  private async loadTree(rootId: number, gen: number): Promise<void> {
    try {
      const t = await this.api.folderTree(rootId);
      if (gen !== this.gen) return;   // 舊 root 的樹晚到 → 丟棄
      this._tree.set(t);
    } catch (e) {
      if (gen !== this.gen) return;
      this._error.set(this.msg(e)); this._tree.set(null);
    }
  }

  // 重查圖(path / 夾內 tag 變動):重置游標與累積。
  private async search(gen: number): Promise<void> {
    const rootId = this._rootId();
    if (rootId === null) return;
    this._loading.set(true); this._error.set(null);
    const { all, none } = splitTokens(this._innerTokens());
    const pathPrefix = this._path();
    try {
      const [count, page] = await Promise.all([
        this.api.searchCount({ all, none, rootId, pathPrefix }),
        this.api.search({ all, none, rootId, pathPrefix, afterId: null, pageSize: PAGE_SIZE }),
      ]);
      if (gen !== this.gen) return;   // 被更新的查詢取代 → 丟棄(不覆蓋新夾結果)
      this._hitCount.set(count.total);
      this._photos.set(page.items);
      this._nextCursor.set(page.nextCursor ?? null);
    } catch (e) {
      if (gen !== this.gen) return;
      this._error.set(this.msg(e)); this._photos.set([]); this._hitCount.set(0); this._nextCursor.set(null);
    } finally {
      if (gen === this.gen) this._loading.set(false);
    }
  }

  async loadMore(): Promise<void> {
    const cursor = this._nextCursor(); const rootId = this._rootId();
    if (cursor === null || rootId === null || this._loading()) return;
    const gen = this.gen;   // 沿用當前世代;切夾後此 loadMore 的回應將被丟棄
    this._loading.set(true);
    const { all, none } = splitTokens(this._innerTokens());
    try {
      const page = await this.api.search({ all, none, rootId, pathPrefix: this._path(), afterId: cursor, pageSize: PAGE_SIZE });
      if (gen !== this.gen) return;   // 切夾後舊 loadMore 丟棄(不 append、不覆蓋 cursor)
      this._photos.update((cur) => [...cur, ...page.items]);
      this._nextCursor.set(page.nextCursor ?? null);
    } catch (e) {
      if (gen !== this.gen) return;
      this._error.set(this.msg(e));
    } finally {
      if (gen === this.gen) this._loading.set(false);
    }
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
    const q = encodeTokens(inner);
    if (q) queryParams['q'] = q;
    void this.router.navigate(['/browse'], { queryParams });
  }

  private msg(e: unknown): string { return e instanceof Error ? e.message : '載入失敗'; }
}
