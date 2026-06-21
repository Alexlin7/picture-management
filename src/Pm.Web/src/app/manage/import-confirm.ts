import { Component, signal } from '@angular/core';
import {
  MOCK_IMPORT,
  MOCK_IMPORT_SOURCE,
  KIND_LABEL,
  type ImportRow,
  type TagKind,
} from '../mock/mock-data';
import { TAG_COLOR } from '../tag-color';

// 契約(route /import):匯入確認 —— 路徑段 → tag 確認表。
// 本輪「按鈕先到位」:互動用 signal 管狀態,禁止真實 HTTP。
@Component({
  selector: 'app-import-confirm',
  imports: [],
  templateUrl: './import-confirm.html',
  styleUrl: './import-confirm.css',
})
export class ImportConfirm {
  // hex → rgba helper(半透明底色用)
  protected rgba(hex: string, a: number): string {
    const n = parseInt(hex.slice(1), 16);
    return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
  }

  protected readonly source = MOCK_IMPORT_SOURCE;
  protected readonly KIND_LABEL = KIND_LABEL;
  protected readonly TAG_COLOR = TAG_COLOR;

  // 可切換分類的候選(map 動作的「分類 ▾」小選單)
  protected readonly catOptions: TagKind[] = ['copyright', 'character', 'general', 'meta', 'path', 'manual'];

  // 每列狀態用 signal 包起來,讓動作膠囊/選單能即時切換
  protected readonly rows = signal<ImportRow[]>(MOCK_IMPORT.map((r) => ({ ...r })));

  // 哪一列的「分類 ▾」選單正開著(null = 全關)
  protected readonly openMenu = signal<number | null>(null);

  // 千分位
  protected fmt(n: number): string {
    return n.toLocaleString();
  }

  // 動作膠囊顏色(map 用 cat 的分色;year 用 meta;ignore 用灰)
  protected pillColor(r: ImportRow): string {
    if (r.action === 'ignore') return 'var(--color-faint)';
    if (r.action === 'year') return TAG_COLOR['meta'];
    return TAG_COLOR[r.cat ?? 'general'];
  }

  protected toggleMenu(idx: number, ev: Event): void {
    ev.stopPropagation();
    this.openMenu.update((cur) => (cur === idx ? null : idx));
  }

  // 切某列的分類(改 cat,連動膠囊分色),tag 文字維持原樣(僅示意)
  protected pickCat(idx: number, cat: TagKind): void {
    this.rows.update((rs) => rs.map((r, i) => (i === idx ? { ...r, cat } : r)));
    this.openMenu.set(null);
  }

  // 底部按鈕:本輪僅 console，真實 API 下輪
  protected applyAll(): void {
    console.log('[import-confirm] 套用全部並完成匯入', this.rows());
  }
  protected skipAll(): void {
    console.log('[import-confirm] 略過全部');
  }
}
