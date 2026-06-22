import { Component, inject, signal } from '@angular/core';
import { TagsStore, type TagListRow } from '../tags.store';
import { tagColor } from '@core/tag-color';

// 路由 /tags:標籤庫管理 —— 列出 DB 所有標籤 + 使用數,改名(改成既有名=合併)/刪除。
@Component({
  selector: 'app-tag-manager',
  imports: [],
  templateUrl: './tag-manager.html',
  styleUrl: './tag-manager.css',
})
export class TagManager {
  private readonly store = inject(TagsStore);
  readonly tags = this.store.tags;
  readonly loading = this.store.loading;
  readonly error = this.store.error;

  readonly editingId = signal<number | null>(null);

  constructor() {
    void this.store.load('');
  }

  color = tagColor;   // 共用分色 helper(未知 kind 退 general)

  filter(q: string): void {
    void this.store.load(q.trim());
  }

  startEdit(t: TagListRow): void {
    this.editingId.set(t.id);
  }
  cancelEdit(): void {
    this.editingId.set(null);
  }
  async saveEdit(t: TagListRow, name: string): Promise<void> {
    const n = name.trim();
    this.editingId.set(null);
    if (!n || n === t.name) return;
    await this.store.rename(t.id, n);   // 撞既有名 → 後端自動合併
  }

  async remove(t: TagListRow): Promise<void> {
    const msg =
      t.count > 0
        ? `刪除標籤「${t.name}」?會一併解除它在 ${t.count} 張圖上的關聯。`
        : `刪除未使用的標籤「${t.name}」?`;
    if (!confirm(msg)) return;
    await this.store.remove(t.id);
  }
}
