import { Component, computed, inject, signal } from '@angular/core';
import { TagsStore, type TagListRow, type SortKey } from '../tags.store';
import { tagColor, KIND_LABEL } from '@core/tag-color';
import { ToastService } from '@core/ui/toast';
import { ConfirmService } from '@core/ui/confirm';
import { MergeDialogService } from '@core/ui/merge-dialog';
import { Activate } from '@core/a11y/activate';

// 可選 kind(順序固定;label 走 KIND_LABEL)
const KINDS = ['character', 'copyright', 'general', 'meta', 'path', 'manual'] as const;

// 路由 /tags:標籤庫管理 —— 新增 / 列表 + 排序 / 改名 + 改 kind / 合併 / 刪除(含批次)。
@Component({
  selector: 'app-tag-manager',
  imports: [Activate],
  templateUrl: './tag-manager.html',
  styleUrl: './tag-manager.css',
})
export class TagManager {
  private readonly store = inject(TagsStore);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly mergeDialog = inject(MergeDialogService);

  readonly tags = this.store.tags;
  readonly loading = this.store.loading;
  readonly error = this.store.error;
  readonly sortKey = this.store.sortKey;
  readonly sortDir = this.store.sortDir;

  readonly kinds = KINDS;
  readonly kindLabel = (k: string): string => (KIND_LABEL as Record<string, string>)[k] ?? k;
  readonly color = tagColor;

  readonly editingId = signal<number | null>(null);
  readonly selected = signal<Set<number>>(new Set());
  readonly selCount = computed(() => this.selected().size);
  readonly allSelected = computed(() => {
    const t = this.tags();
    return t.length > 0 && t.every((x) => this.selected().has(x.id));
  });

  constructor() {
    void this.store.load('');
  }

  filter(q: string): void {
    void this.store.load(q.trim());
  }

  // ---- 排序 ----
  sort(key: SortKey): void {
    this.store.setSort(key);
  }
  arrow(key: SortKey): string {
    if (this.sortKey() !== key) return '';
    return this.sortDir() === 'asc' ? '▲' : '▼';
  }

  // ---- 新增 ----
  async add(name: string, kind: string, nameInput: HTMLInputElement): Promise<void> {
    const n = name.trim();
    if (!n) return;
    const { existed } = await this.store.create(n, kind || 'manual');
    if (existed) this.toast.info(`「${n}」已存在,未重複建立`);
    else this.toast.success(`已新增標籤「${n}」`);
    nameInput.value = '';
    nameInput.focus();
  }

  // ---- 編輯(改名 + 改 kind)----
  startEdit(t: TagListRow): void {
    this.editingId.set(t.id);
  }
  cancelEdit(): void {
    this.editingId.set(null);
  }
  async saveEdit(t: TagListRow, name: string, kind: string): Promise<void> {
    this.editingId.set(null);
    const dto: { name?: string; kind?: string } = {};
    const n = name.trim();
    if (n && n !== t.name) dto.name = n;
    if (kind && kind !== t.kind) dto.kind = kind;
    if (!dto.name && !dto.kind) return;
    const merged = await this.store.update(t.id, dto);
    this.toast.success(merged ? `已合併到既有標籤` : `已更新「${dto.name ?? t.name}」`);
  }

  // ---- 單筆刪除 ----
  async remove(t: TagListRow): Promise<void> {
    const msg =
      t.count > 0
        ? `刪除標籤「${t.name}」?會一併解除它在 ${t.count} 張圖上的關聯(不刪圖)。`
        : `刪除未使用的標籤「${t.name}」?`;
    const ok = await this.confirm.ask(msg, { title: '刪除標籤', confirmText: '刪除', danger: true });
    if (!ok) return;
    await this.store.remove(t.id);
    this.toast.success(`已刪除「${t.name}」`);
  }

  // ---- 選取 ----
  isSel(id: number): boolean {
    return this.selected().has(id);
  }
  toggleSel(id: number): void {
    const s = new Set(this.selected());
    s.has(id) ? s.delete(id) : s.add(id);
    this.selected.set(s);
  }
  toggleAll(): void {
    this.selected.set(this.allSelected() ? new Set() : new Set(this.tags().map((t) => t.id)));
  }
  clearSel(): void {
    this.selected.set(new Set());
  }

  // ---- 批次刪除 ----
  async removeSelected(): Promise<void> {
    const ids = [...this.selected()];
    if (!ids.length) return;
    const ok = await this.confirm.ask(
      `刪除選取的 ${ids.length} 個標籤?會一併解除它們在圖上的關聯(不刪圖)。`,
      { title: '批次刪除', confirmText: `刪除 ${ids.length} 個`, danger: true },
    );
    if (!ok) return;
    await this.store.removeMany(ids);
    this.clearSel();
    this.toast.success(`已刪除 ${ids.length} 個標籤`);
  }

  // ---- 合併(恰選 2 個;由對話框讓使用者選保留方向)----
  async mergeSelected(): Promise<void> {
    const ids = [...this.selected()];
    if (ids.length !== 2) return;
    const a = this.tags().find((t) => t.id === ids[0])!;
    const b = this.tags().find((t) => t.id === ids[1])!;
    const dir = await this.mergeDialog.ask(a, b);
    if (!dir) return;   // 使用者取消
    await this.store.merge(dir.from.id, dir.to.id);
    this.clearSel();
    this.toast.success(`已合併到「${dir.to.name}」`);
  }
}
