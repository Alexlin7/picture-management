import { Injectable, computed, inject, signal } from '@angular/core';
import { PmApi, type TagListRow } from '@core/api/pm-api';

export type { TagListRow } from '@core/api/pm-api';
export type SortKey = 'count' | 'name' | 'kind' | 'recent';
export type SortDir = 'asc' | 'desc';

// 標籤庫管理資料接縫:列出/過濾/排序/新增/改名/改 kind/合併/刪除(含批次),皆走 PmApi。
@Injectable({ providedIn: 'root' })
export class TagsStore {
  private readonly api = inject(PmApi);

  private readonly _tags = signal<TagListRow[]>([]);   // API 原始順序
  private readonly _loading = signal(false);
  readonly loading = this._loading.asReadonly();
  private readonly _error = signal<string | null>(null);
  readonly error = this._error.asReadonly();

  readonly sortKey = signal<SortKey>('count');
  readonly sortDir = signal<SortDir>('desc');

  // 依排序準則重排(前端;'recent' = id 遞增 ≈ 建立順序)。
  readonly tags = computed<TagListRow[]>(() => {
    const arr = [...this._tags()];
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    const key = this.sortKey();
    arr.sort((a, b) => {
      let c = 0;
      if (key === 'count') c = a.count - b.count || a.name.localeCompare(b.name);
      else if (key === 'name') c = a.name.localeCompare(b.name);
      else if (key === 'kind') c = a.kind.localeCompare(b.kind) || a.name.localeCompare(b.name);
      else c = a.id - b.id;   // recent
      return c * dir;
    });
    return arr;
  });

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

  // 點欄位標頭:同 key 切升降;不同 key 換準則並套預設方向。
  setSort(key: SortKey): void {
    if (this.sortKey() === key) {
      this.sortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortKey.set(key);
      this.sortDir.set(key === 'name' || key === 'kind' ? 'asc' : 'desc');
    }
  }

  // 新增純標籤;回 existed(撞既有 CI → 後端回既有,不建新)。
  async create(name: string, kind: string): Promise<{ existed: boolean }> {
    const r = await this.api.createTag(name, kind);
    await this.load();
    return { existed: r.existed };
  }

  // 改名 and/or 改 kind;回 merged(改名撞既有 → 後端合併)。
  async update(id: number, dto: { name?: string; kind?: string }): Promise<boolean> {
    const r = await this.api.updateTag(id, dto);
    await this.load();
    return r.merged;
  }

  async merge(fromId: number, toId: number): Promise<void> {
    await this.api.mergeTags(fromId, toId);
    await this.load();
  }

  async remove(id: number): Promise<void> {
    await this.api.deleteTag(id);
    await this.load();
  }

  async removeMany(ids: number[]): Promise<void> {
    for (const id of ids) await this.api.deleteTag(id);
    await this.load();
  }
}
