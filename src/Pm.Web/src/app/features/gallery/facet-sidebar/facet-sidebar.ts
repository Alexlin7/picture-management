import { Component, computed, inject, signal } from '@angular/core';
import { GalleryStore, type FacetNode } from '../gallery.store';
import { TAG_COLOR } from '@core/tag-color';

// 契約:相簿左側 facet 側欄(作品/角色 DAG 樹、屬性、年份)。
// 由 workflow agent 補完內部(templateUrl/styleUrl + 互動)。
@Component({
  selector: 'app-facet-sidebar',
  imports: [],
  templateUrl: './facet-sidebar.html',
  styleUrl: './facet-sidebar.css',
})
export class FacetSidebar {
  private readonly store = inject(GalleryStore);

  // 資料來源:store(來自 PmApi.tagTree())
  readonly tree = this.store.tree;
  readonly rootless = this.store.rootless;
  readonly general = this.store.facetsGeneral;
  readonly meta = this.store.facetsMeta;
  readonly hitCount = this.store.hitCount;

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

  // 千分位數字
  readonly fmt = (n: number): string => n.toLocaleString();

  // 縮排:8 + depth*13 (px),對齊 mockup renderTree()
  readonly pad = (depth: number): string => `${8 + depth * 13}px`;

  readonly hasKids = (node: FacetNode): boolean => !!(node.children && node.children.length);
}
