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

  // 單張重標 / 清除 WD14 自動標(動作層);完成後 refresh detail 反映變化。
  // refresh:清舊 wd14 + 重排(worker 開啟才會立即重標);clear:清舊 wd14、不排。
  async retag(photoId: number, mode: 'retry' | 'refresh' | 'clear'): Promise<void> {
    await this.api.retag(photoId, mode);
    if (this.currentId === photoId) await this.refresh();
  }

  // ---- 加標籤 combobox:查既有標籤,避免打出近似重複 ----
  private readonly _suggestions = signal<TagListRow[]>([]);
  readonly suggestions = this._suggestions.asReadonly();

  // 目前查詢字,用來丟棄過期回應(快速打字 / 切圖時的競態,比照 load 的 currentId)。
  private suggestTerm = '';

  // 依關鍵字查標籤庫(不分大小寫,後端已處理);空字串清空。
  async suggest(q: string): Promise<void> {
    const term = q.trim();
    this.suggestTerm = term;
    if (!term) {
      this._suggestions.set([]);
      return;
    }
    try {
      const rows = await this.api.tags(term, 8);
      if (this.suggestTerm !== term) return; // 已有更新的查詢,丟棄這份過期回應
      this._suggestions.set(rows);
    } catch {
      if (this.suggestTerm === term) this._suggestions.set([]);
    }
  }

  clearSuggestions(): void {
    this._suggestions.set([]);
  }
}
