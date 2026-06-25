import { Component, inject } from '@angular/core';
import { BrowseStore } from '../browse.store';
import type { FolderNode, FolderRoot } from '@core/api/pm-api';

// 資料夾樹側欄:頂層並列各 root(多 root);選中 root 後展其樹(只渲染 1–2 層,深層靠主區子夾下鑽)。
@Component({
  selector: 'app-folder-tree-sidebar',
  imports: [],
  templateUrl: './folder-tree-sidebar.html',
  styleUrl: './folder-tree-sidebar.css',
})
export class FolderTreeSidebar {
  private readonly store = inject(BrowseStore);
  readonly roots = this.store.roots;
  readonly tree = this.store.tree;
  readonly currentRootId = this.store.currentRootId;
  readonly currentPath = this.store.currentPath;

  readonly fmt = (n: number): string => n.toLocaleString('en-US');

  selectRoot(r: FolderRoot): void { this.store.selectRoot(r.id); }
  enter(node: FolderNode): void { this.store.enterFolder(node.relPath); }
  // 第一層子資料夾(tree.children);只渲染到第二層,避免側欄爆長。
  firstLevel(): FolderNode[] { return this.tree()?.children ?? []; }
}
