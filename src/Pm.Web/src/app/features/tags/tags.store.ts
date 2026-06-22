import { Injectable, inject, signal } from '@angular/core';
import { PmApi, type TagListRow } from '@core/api/pm-api';

export type { TagListRow } from '@core/api/pm-api';

// 標籤庫管理資料接縫:列出/過濾/改名/刪除,皆走 PmApi。
@Injectable({ providedIn: 'root' })
export class TagsStore {
  private readonly api = inject(PmApi);

  private readonly _tags = signal<TagListRow[]>([]);
  readonly tags = this._tags.asReadonly();
  private readonly _loading = signal(false);
  readonly loading = this._loading.asReadonly();
  private readonly _error = signal<string | null>(null);
  readonly error = this._error.asReadonly();

  private _q = '';

  async load(q = this._q): Promise<void> {
    this._q = q;
    this._loading.set(true);
    this._error.set(null);
    try {
      this._tags.set(await this.api.tags(q || undefined, 500));
    } catch (e) {
      this._error.set(e instanceof Error ? e.message : '載入失敗');
      this._tags.set([]);
    } finally {
      this._loading.set(false);
    }
  }

  // 改名;撞既有名(CI)→ 後端自動合併。回是否合併。完成後重新載入。
  async rename(id: number, name: string): Promise<boolean> {
    const r = await this.api.renameTag(id, name);
    await this.load();
    return r.merged;
  }

  async remove(id: number): Promise<void> {
    await this.api.deleteTag(id);
    await this.load();
  }
}
