import { Component, computed, inject, input, signal } from '@angular/core';
import { GalleryStore, type FacetNode } from '../gallery.store';
import { TAG_COLOR } from '@core/tag-color';
import { Activate } from '@core/a11y/activate';
import { loadCollapsed, saveCollapsed, toggleCollapsed, type FacetSection } from './facet-collapse';

// 屬性/年份/rootless 分區初始顯示筆數;超過則收起並提供「顯示更多」。
const TOP_N = 12;

// 契約:相簿左側 facet 側欄(作品/角色 DAG 樹、屬性、年份)。
// 由 workflow agent 補完內部(templateUrl/styleUrl + 互動)。
@Component({
  selector: 'app-facet-sidebar',
  imports: [Activate],
  templateUrl: './facet-sidebar.html',
  styleUrl: './facet-sidebar.css',
})
export class FacetSidebar {
  private readonly store = inject(GalleryStore);

  /** 由 gallery-view 傳入;true = 側欄寬收合至 0(不與內部分區 collapsed 衝突)。 */
  sidebarCollapsed = input(false);

  // 資料來源:store(來自 PmApi.tagTree())
  readonly tree = this.store.tree;
  readonly hitCount = this.store.hitCount;

  // ---- 過濾框 + top-N(屬性/年份/rootless 三個平面清單;DAG 樹不在此 backlog 過濾) ----
  readonly filterText = signal('');
  setFilter(e: Event): void { this.filterText.set((e.target as HTMLInputElement).value); }
  readonly q = computed(() => this.filterText().trim().toLowerCase());

  // 當前搜尋 token 集合,給 .on 高亮用(O(1) 查詢)。
  private readonly activeSet = computed(() => new Set(this.store.tokens().map((t) => t.text)));
  readonly isActive = (name: string): boolean => this.activeSet().has(name);

  readonly TOP_N = TOP_N;
  readonly showAllGeneral = signal(false);
  readonly showAllMeta = signal(false);
  readonly showAllRootless = signal(false);
  toggleShowAll(s: 'general' | 'meta' | 'rootless'): void {
    const sig = s === 'general' ? this.showAllGeneral : s === 'meta' ? this.showAllMeta : this.showAllRootless;
    sig.update((v) => !v);
  }

  readonly generalFiltered = computed(() => {
    const q = this.q();
    const rows = this.store.facetsGeneral();
    return q ? rows.filter(([n]) => n.toLowerCase().includes(q)) : rows;
  });
  readonly metaFiltered = computed(() => {
    const q = this.q();
    const rows = this.store.facetsMeta();
    return q ? rows.filter(([n]) => n.toLowerCase().includes(q)) : rows;
  });
  readonly generalVisible = computed(() =>
    this.showAllGeneral() ? this.generalFiltered() : this.generalFiltered().slice(0, TOP_N));
  readonly metaVisible = computed(() =>
    this.showAllMeta() ? this.metaFiltered() : this.metaFiltered().slice(0, TOP_N));

  readonly rootlessFiltered = computed(() => {
    const q = this.q();
    const rows = this.store.rootless();
    return q ? rows.filter((r) => r.name.toLowerCase().includes(q)) : rows;
  });
  readonly rootlessVisible = computed(() =>
    this.showAllRootless() ? this.rootlessFiltered() : this.rootlessFiltered().slice(0, TOP_N));

  // kind → 顏色
  readonly color = (kind: string): string => TAG_COLOR[kind] ?? TAG_COLOR['general'];

  // 使用者手動覆寫的展開/收合(true=展開,false=收合)。
  private readonly overrides = signal<Map<FacetNode, boolean>>(new Map());

  // 預設展開:第一層有 children 的節點(隨 tree 變動而重算)。
  private readonly defaultOpen = computed(
    () => new Set(this.tree().filter((n) => n.children?.length)),
  );

  readonly isOpen = (node: FacetNode): boolean => {
    const ov = this.overrides().get(node);
    if (ov !== undefined) return ov;
    return this.defaultOpen().has(node);
  };

  toggle(node: FacetNode): void {
    const next = new Map(this.overrides());
    next.set(node, !this.isOpen(node));
    this.overrides.set(next);
  }

  // 鍵盤樹操作:聚焦樹列時 → 展開、← 收合(標準 tree 行為;箭頭 span 仍供滑鼠點)。
  expand(node: FacetNode): void { if (this.hasKids(node) && !this.isOpen(node)) this.toggle(node); }
  collapse(node: FacetNode): void { if (this.hasKids(node) && this.isOpen(node)) this.toggle(node); }

  // 分區整段收折(dag/屬性/年份),狀態存 localStorage,預設全展。
  private readonly collapsed = signal<Set<FacetSection>>(loadCollapsed(localStorage));
  readonly isCollapsed = (s: FacetSection): boolean => this.collapsed().has(s);
  toggleSection(s: FacetSection): void {
    const next = toggleCollapsed(this.collapsed(), s);
    this.collapsed.set(next);
    saveCollapsed(localStorage, next);
  }

  // 千分位數字
  readonly fmt = (n: number): string => n.toLocaleString();

  // 縮排:8 + depth*13 (px),對齊 mockup renderTree()
  readonly pad = (depth: number): string => `${8 + depth * 13}px`;

  readonly hasKids = (node: FacetNode): boolean => !!(node.children && node.children.length);

  // 點標籤名 → 加進上方搜尋(AND token);展開箭頭仍只負責收合(已 stopPropagation)。
  pick(node: FacetNode): void {
    this.store.addToken({ text: node.name, kind: node.kind });
  }
  // 屬性/年份區(無 kind 的 [name,count])。
  pickName(name: string, kind: 'general' | 'meta'): void {
    this.store.addToken({ text: name, kind });
  }
}
