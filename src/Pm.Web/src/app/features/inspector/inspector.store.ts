import { Injectable, computed, inject, signal } from '@angular/core';
import { PmApi, type PhotoDetail, type TagView, type TagListRow } from '@core/api/pm-api';

// 對外 re-export 型別,讓元件只從 store 取型別。
export type { PhotoDetail, TagView, LocationView, TagListRow } from '@core/api/pm-api';
export type { TagKind } from '@core/tag-color';

// Inspector 資料接縫:以 selectedId 非同步載入 PhotoDetail(PmApi.photo(id))。
// 元件用 effect 在 photoId 變動時呼 load(id);讀 detail/loading/error 三個 signal。
@Injectable({ providedIn: 'root' })
export class InspectorStore {
  private readonly api = inject(PmApi);

  private readonly _detail = signal<PhotoDetail | null>(null);
  readonly detail = this._detail.asReadonly();

  private readonly _loading = signal(false);
  readonly loading = this._loading.asReadonly();

  private readonly _error = signal<string | null>(null);
  readonly error = this._error.asReadonly();

  // 目前載入中的 id,用來丟棄過期回應(切換選取時的競態)。
  private currentId: number | null = null;

  // 以 id 載入 PhotoDetail;id 為 null 清空。
  async load(id: number | null): Promise<void> {
    this.currentId = id;
    this._error.set(null);
    if (id == null) {
      this._loading.set(false);
      this._detail.set(null);
      return;
    }
    this._loading.set(true);
    try {
      const d = await this.api.photo(id);
      if (this.currentId !== id) return; // 已切換,丟棄
      this._detail.set(d);
    } catch {
      if (this.currentId !== id) return;
      this._error.set('載入失敗');
      this._detail.set(null);
    } finally {
      if (this.currentId === id) this._loading.set(false);
    }
  }

  // 重新抓目前這張 photo 的 detail(tag 變動後用)。
  async refresh(): Promise<void> {
    await this.load(this.currentId);
  }

  // 手動 tag:新增後 refresh detail。
  async addTag(photoId: number, dto: { name: string; kind?: string }): Promise<void> {
    await this.api.addTag(photoId, dto);
    if (this.currentId === photoId) await this.refresh();
  }

  // 手動 tag:移除後 refresh detail。
  async removeTag(photoId: number, tagId: number): Promise<void> {
    await this.api.removeTag(photoId, tagId);
    if (this.currentId === photoId) await this.refresh();
  }

  // ---- 加標籤 combobox:查既有標籤,避免打出近似重複 ----
  private readonly _suggestions = signal<TagListRow[]>([]);
  readonly suggestions = this._suggestions.asReadonly();

  // 依關鍵字查標籤庫(不分大小寫,後端已處理);空字串清空。
  async suggest(q: string): Promise<void> {
    const term = q.trim();
    if (!term) {
      this._suggestions.set([]);
      return;
    }
    try {
      this._suggestions.set(await this.api.tags(term, 8));
    } catch {
      this._suggestions.set([]);
    }
  }

  clearSuggestions(): void {
    this._suggestions.set([]);
  }

  // 目前 detail 的縮圖 URL(無 detail 回 null)。
  readonly thumbUrl = computed<string | null>(() => {
    const d = this._detail();
    return d == null ? null : this.api.thumbUrl(d.id);
  });
}
