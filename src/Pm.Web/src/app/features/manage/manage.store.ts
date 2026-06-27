import { Injectable, computed, inject, signal } from '@angular/core';
import {
  PmApi,
  type Root,
  type ScanStatus,
  type PendingSegment,
  type SavedSearchRow,
} from '@core/api/pm-api';
import { type TagKind } from '@core/tag-color';

// Manage feature 的 signal store:元件與 @core/api/pm-api 的唯一接縫。
// 資料一律來自 PmApi;元件只讀 store 的 signal,store 負責呼叫 API + 映射形狀 + loading/error。

// ---- 圖庫來源 view ----
// API 只給 { id, name, absPath };mock-only 的 files/scan/status 無來源 → 不放進 view(deferred)。
export interface RootView {
  id: number;
  name: string;
  absPath: string;
}

// ---- 失蹤待辦匣 view ----
export type ReconState = 'pending' | 'waiting' | 'externalized' | 'deleted';
// API missing(): { id, fileHash, paths }。title/last 由 paths 映射;縮圖改用共用 <app-thumb>(依 id)。
export interface ReconView {
  id: number;
  fileHash: string;
  paths: string[];
  title: string; // 由最後出現路徑的檔名映射
  last: string; // 由 paths 組出「上次出現」說明
  state: ReconState;
}

// ---- 收藏的搜尋 view ----
// API SavedSearchRow: { id, name, queryJson, createdAt }。
// mock-only 的 hits(命中數)/ special 無來源 → 不放進 view(deferred)。
export interface SavedView {
  id: number;
  title: string; // ← name
  query: string; // ← queryJson(原始 JSON 字串,template 顯示用)
  createdAt: string;
}

// ---- 匯入確認 view ----
export type ImportAction = 'map' | 'ignore' | 'year';
// API PendingSegment: { segment, count, samplePath, suggestedAction }。
export interface ImportRowView {
  seg: string; // ← segment
  n: number; // ← count
  ex: string; // ← samplePath
  action: ImportAction; // ← suggestedAction(正規化)
  cat?: TagKind; // map 時的分類;預設 general
  tag?: string; // map 時產生的 tag 名;預設沿用 segment
}

// 把後端 suggestedAction 正規化成前端三動作之一(未知 → map)。
function normalizeAction(a: string): ImportAction {
  if (a === 'ignore' || a === 'year' || a === 'map') return a;
  return 'map';
}

// 從 paths 取一個檔名當標題(取最後一段)。
function fileNameOf(path: string): string {
  const parts = path.split(/[\\/]/).filter(Boolean);
  return parts.length ? parts[parts.length - 1] : path;
}

@Injectable({ providedIn: 'root' })
export class ManageStore {
  private readonly api = inject(PmApi);

  // ===== 圖庫來源 =====
  private readonly _roots = signal<RootView[]>([]);
  readonly roots = this._roots.asReadonly();
  private readonly _rootsLoading = signal(false);
  readonly rootsLoading = this._rootsLoading.asReadonly();
  private readonly _rootsError = signal<string | null>(null);
  readonly rootsError = this._rootsError.asReadonly();

  async loadRoots(): Promise<void> {
    this._rootsLoading.set(true);
    this._rootsError.set(null);
    try {
      const rows = await this.api.roots();
      this._roots.set(rows.map((r: Root) => ({ id: r.id, name: r.name, absPath: r.absPath })));
    } catch (e) {
      this._rootsError.set(this.msg(e));
      this._roots.set([]);
    } finally {
      this._rootsLoading.set(false);
    }
  }

  // 重新掃描某來源。
  async rescan(id: number): Promise<ScanStatus> {
    let status = await this.api.scan(id);
    while (status.state === 'running') {
      await sleep(4000);
      status = await this.api.scanStatus(id);
    }
    if (status.state === 'error') {
      throw new Error(status.error ?? '掃描失敗');
    }
    return status;
  }

  // 新增來源(資料夾挑選器無法在純前端做 → 由 caller 傳 absPath 文字;deferred 真正挑選器)。
  async createRoot(name: string, absPath: string): Promise<Root> {
    const root = await this.api.createRoot(name, absPath);
    await this.loadRoots();
    return root;
  }

  // ===== 失蹤待辦匣 =====
  private readonly _recon = signal<ReconView[]>([]);
  readonly recon = this._recon.asReadonly();
  private readonly _reconLoading = signal(false);
  readonly reconLoading = this._reconLoading.asReadonly();
  private readonly _reconError = signal<string | null>(null);
  readonly reconError = this._reconError.asReadonly();

  // 待處理張數(state === 'pending')。
  readonly reconPending = computed(() => this._recon().filter((r) => r.state === 'pending').length);

  async loadRecon(): Promise<void> {
    this._reconLoading.set(true);
    this._reconError.set(null);
    try {
      const rows = await this.api.missing();
      this._recon.set(
        rows.map((r) => {
          const last = r.paths[r.paths.length - 1] ?? '';
          return {
            id: r.id,
            fileHash: r.fileHash,
            paths: r.paths,
            title: last ? fileNameOf(last) : r.fileHash.slice(0, 12),
            last: last || '(無上次位置紀錄)',
            state: 'pending' as ReconState,
          };
        }),
      );
    } catch (e) {
      this._reconError.set(this.msg(e));
      this._recon.set([]);
    } finally {
      this._reconLoading.set(false);
    }
  }

  // 「移到圖庫外」/「繼續等待」→ 軟刪 archive(保留 photo+tags)。
  async externalize(id: number): Promise<void> {
    await this.api.archivePhoto(id);
    this.setReconState(id, 'externalized');
  }
  async keepWaiting(id: number): Promise<void> {
    // 純本地標記:不動後端身分,等下次掃描自動續接。
    this.setReconState(id, 'waiting');
  }
  // 「已刪除」→ 硬刪 purge(使用者明示)。
  async purge(id: number): Promise<void> {
    await this.api.purgePhoto(id);
    this.setReconState(id, 'deleted');
  }
  // 復原:回到 pending(僅本地 UI;archive 的真正復原靠同 hash 回來時自動續接)。
  resetReconState(id: number): void {
    this.setReconState(id, 'pending');
  }
  private setReconState(id: number, state: ReconState): void {
    this._recon.update((list) => list.map((it) => (it.id === id ? { ...it, state } : it)));
  }

  // ===== 收藏的搜尋 =====
  private readonly _saved = signal<SavedView[]>([]);
  readonly saved = this._saved.asReadonly();
  private readonly _savedLoading = signal(false);
  readonly savedLoading = this._savedLoading.asReadonly();
  private readonly _savedError = signal<string | null>(null);
  readonly savedError = this._savedError.asReadonly();

  async loadSaved(): Promise<void> {
    this._savedLoading.set(true);
    this._savedError.set(null);
    try {
      const rows = await this.api.savedSearches();
      this._saved.set(
        rows.map((r: SavedSearchRow) => ({
          id: r.id,
          title: r.name,
          query: r.queryJson,
          createdAt: r.createdAt,
        })),
      );
    } catch (e) {
      this._savedError.set(this.msg(e));
      this._saved.set([]);
    } finally {
      this._savedLoading.set(false);
    }
  }

  async deleteSaved(id: number): Promise<void> {
    await this.api.deleteSavedSearch(id);
    this._saved.update((list) => list.filter((s) => s.id !== id));
  }

  // ===== 匯入確認:路徑段 → tag =====
  // rootId 來源:無「當前 root」概念 → 預設取第一個 root(deferred 註明需可選 root)。
  private readonly _importRootId = signal<number | null>(null);
  readonly importRootId = this._importRootId.asReadonly();
  private readonly _importSource = signal<string>('');
  readonly importSource = this._importSource.asReadonly();

  private readonly _importRows = signal<ImportRowView[]>([]);
  readonly importRows = this._importRows.asReadonly();
  private readonly _importLoading = signal(false);
  readonly importLoading = this._importLoading.asReadonly();
  private readonly _importError = signal<string | null>(null);
  readonly importError = this._importError.asReadonly();

  // 待確認段數(template note 用)。
  readonly importPending = computed(() => this._importRows().length);

  // 載入待確認路徑段:先確保有 roots;rootId 未指定取第一個 root 當來源。
  async loadImport(rootId?: number): Promise<void> {
    this._importLoading.set(true);
    this._importError.set(null);
    try {
      if (this._roots().length === 0) {
        await this.loadRoots();
      }
      const target = rootId != null
        ? this._roots().find((r) => r.id === rootId) ?? null
        : this._roots()[0] ?? null;
      if (!target) {
        this._importRootId.set(null);
        this._importSource.set('');
        this._importRows.set([]);
        return;
      }
      this._importRootId.set(target.id);
      this._importSource.set(target.name);
      const segs = await this.api.pendingSegments(target.id);
      this._importRows.set(
        segs.map((s: PendingSegment) => {
          const action = normalizeAction(s.suggestedAction);
          return {
            seg: s.segment,
            n: s.count,
            ex: s.samplePath,
            action,
            cat: action === 'map' ? ('general' as TagKind) : undefined,
            tag: action === 'map' ? s.segment : undefined,
          };
        }),
      );
    } catch (e) {
      this._importError.set(this.msg(e));
      this._importRows.set([]);
    } finally {
      this._importLoading.set(false);
    }
  }

  // 切換來源 root:設定並重新載入該 root 的待確認段。
  async selectImportRoot(id: number): Promise<void> {
    await this.loadImport(id);
  }

  // 切某列分類(本地;送出時帶到 applyRule 的 kind)。
  setImportCat(seg: string, cat: TagKind): void {
    this._importRows.update((rs) => rs.map((r) => (r.seg === seg ? { ...r, cat } : r)));
  }

  // 改某列產生的 tag 名(inline 編輯 / 套 preset 用)。
  setImportTag(seg: string, tag: string): void {
    this._importRows.update((rs) => rs.map((r) => (r.seg === seg ? { ...r, tag } : r)));
  }

  // 套用某一列規則。action 維持前端詞彙(map/ignore/year),後端會正規化;
  // map 帶上 tagName 與 kind(cat),讓使用者選的分類生效。
  private async applyOne(rootId: number, r: ImportRowView): Promise<void> {
    await this.api.applyRule({
      rootId,
      segment: r.seg,
      action: r.action,
      tagName: r.action === 'map' ? (r.tag ?? r.seg) : undefined,
      kind: r.action === 'map' ? (r.cat ?? 'general') : undefined,
    });
  }

  // 套用全部規則後,觸發路徑→tag 套用,再清空清單。
  async applyAll(): Promise<void> {
    const rootId = this._importRootId();
    if (rootId === null) return;
    this._importLoading.set(true);
    this._importError.set(null);
    try {
      for (const r of this._importRows()) {
        await this.applyOne(rootId, r);
      }
      await this.api.applyPathTags(rootId);
      this._importRows.set([]);
    } catch (e) {
      this._importError.set(this.msg(e));
    } finally {
      this._importLoading.set(false);
    }
  }

  // 略過全部:本地清空(不送規則)。
  skipAll(): void {
    this._importRows.set([]);
  }

  private msg(e: unknown): string {
    if (e instanceof Error) return e.message;
    return '載入失敗';
  }
}

export type { TagKind } from '@core/tag-color';

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms));
}
