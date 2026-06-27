import { Component, OnInit, inject, signal } from '@angular/core';
import { ManageStore, type ImportRowView, type TagKind } from '../manage.store';
import { TAG_COLOR, KIND_LABEL, hexToRgba } from '@core/tag-color';
import { loadPresets, savePresets, addPreset, removePreset, type TagPreset } from './tag-preset';

// 契約(route /import):匯入確認 —— 路徑段 → tag 確認表。
// 資料一律來自 ManageStore(PmApi.pendingSegments);套用走 applyRule + applyPathTags。
@Component({
  selector: 'app-import-confirm',
  imports: [],
  templateUrl: './import-confirm.html',
  styleUrl: './import-confirm.css',
})
export class ImportConfirm implements OnInit {
  private readonly store = inject(ManageStore);

  // hex → rgba(共用 @core/tag-color;template 沿用此方法)
  protected rgba(hex: string, a: number): string { return hexToRgba(hex, a); }

  // 讀 store signal
  protected readonly source = this.store.importSource;
  protected readonly rows = this.store.importRows;
  protected readonly pending = this.store.importPending;
  protected readonly loading = this.store.importLoading;
  protected readonly error = this.store.importError;
  protected readonly roots = this.store.roots;
  protected readonly importRootId = this.store.importRootId;

  protected readonly KIND_LABEL = KIND_LABEL;
  protected readonly TAG_COLOR = TAG_COLOR;

  // 可切換分類的候選(map 動作的「分類 ▾」小選單,也用於 preset 新增表單)
  protected readonly catOptions: TagKind[] = ['copyright', 'character', 'general', 'meta', 'path', 'manual'];

  // 哪一列的「分類 ▾」選單正開著(以 seg 為 key;null = 全關)
  protected readonly openMenu = signal<string | null>(null);

  // 哪一列的「套用 preset ▾」選單正開著(以 seg 為 key;null = 全關)
  protected readonly openPreset = signal<string | null>(null);

  // 常用 tag preset(localStorage)+ 管理面板開合
  protected readonly presets = signal<TagPreset[]>(loadPresets(localStorage));
  protected readonly showPresets = signal(false);

  ngOnInit(): void {
    void this.store.loadImport();
  }

  // 切換來源 root。
  protected switchRoot(id: number): void {
    void this.store.selectImportRoot(id);
  }
  protected onRootSelect(ev: Event): void {
    this.switchRoot(Number((ev.target as HTMLSelectElement).value));
  }

  // inline 編輯某列的 tag 名。
  protected editTag(seg: string, ev: Event): void {
    this.store.setImportTag(seg, (ev.target as HTMLInputElement).value);
  }

  // ---- preset 面板 ----
  protected toggleShowPresets(): void {
    this.showPresets.update((v) => !v);
  }
  protected togglePresetMenu(seg: string, ev: Event): void {
    ev.stopPropagation();
    this.openPreset.update((cur) => (cur === seg ? null : seg));
  }
  // 套 preset 到某列:設 tag 名 + 分類。
  protected applyPreset(seg: string, p: TagPreset): void {
    this.store.setImportTag(seg, p.name);
    this.store.setImportCat(seg, p.kind);
    this.openPreset.set(null);
  }
  protected addPreset(nameInput: HTMLInputElement, kind: string): void {
    const name = nameInput.value.trim();
    if (!name) return;
    const next = addPreset(this.presets(), { name, kind: (kind || 'general') as TagKind });
    this.presets.set(next);
    savePresets(localStorage, next);
    nameInput.value = '';
  }
  protected removePreset(p: TagPreset): void {
    const next = removePreset(this.presets(), p);
    this.presets.set(next);
    savePresets(localStorage, next);
  }

  // 千分位
  protected fmt(n: number): string {
    return n.toLocaleString();
  }

  // 動作膠囊顏色(map 用 cat 的分色;year 用 meta;ignore 用灰)
  protected pillColor(r: ImportRowView): string {
    if (r.action === 'ignore') return 'var(--color-faint)';
    if (r.action === 'year') return TAG_COLOR['meta'];
    return TAG_COLOR[r.cat ?? 'general'];
  }

  protected toggleMenu(seg: string, ev: Event): void {
    ev.stopPropagation();
    this.openMenu.update((cur) => (cur === seg ? null : seg));
  }

  // 切某列的分類(改 cat,連動膠囊分色與送出時的 kind)
  protected pickCat(seg: string, cat: TagKind): void {
    this.store.setImportCat(seg, cat);
    this.openMenu.set(null);
  }

  // 底部按鈕:套用全部 → applyRule×N + applyPathTags;略過全部 → 本地清空。
  protected applyAll(): void {
    void this.store.applyAll();
  }
  protected skipAll(): void {
    this.store.skipAll();
  }
}
