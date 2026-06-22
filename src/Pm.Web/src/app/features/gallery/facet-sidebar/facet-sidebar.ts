import { Component, inject, signal } from '@angular/core';
import { GalleryStore, type TreeNode } from '../gallery.store';
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

  // 資料來源:store(原假資料)
  readonly tree = this.store.tree;
  readonly rootless = this.store.rootless;
  readonly general = this.store.facetsGeneral;
  readonly meta = this.store.facetsMeta;

  // kind → 顏色
  readonly color = (kind: string): string => TAG_COLOR[kind] ?? TAG_COLOR['general'];

  // 展開狀態:用節點物件當 key(WeakSet 不能在 template 用,改 signal<Set>)。
  // 預設展開第一層(depth 0)有 children 的節點。(純視覺本地狀態)
  private readonly openSet = signal<Set<TreeNode>>(
    new Set(this.store.tree.filter((n) => n.children?.length)),
  );

  readonly isOpen = (node: TreeNode): boolean => this.openSet().has(node);

  toggle(node: TreeNode): void {
    const next = new Set(this.openSet());
    if (next.has(node)) next.delete(node);
    else next.add(node);
    this.openSet.set(next);
  }

  // 千分位數字
  readonly fmt = (n: number): string => n.toLocaleString();

  // 縮排:8 + depth*13 (px),對齊 mockup renderTree()
  readonly pad = (depth: number): string => `${8 + depth * 13}px`;

  readonly hasKids = (node: TreeNode): boolean => !!(node.children && node.children.length);
}
