import { Component, OnInit, inject, DestroyRef } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FolderTreeSidebar } from '../folder-tree-sidebar/folder-tree-sidebar';
import { BrowseGrid } from '../browse-grid/browse-grid';
import { BrowseStore } from '../browse.store';

// 資料夾瀏覽兩欄:資料夾樹側欄(252)· 圖牆(1fr)。inspector 暫不接(與 gallery 隔離)。
@Component({
  selector: 'app-browse-view',
  imports: [FolderTreeSidebar, BrowseGrid],
  template: `
    <div class="bview">
      <app-folder-tree-sidebar />
      <app-browse-grid />
    </div>
  `,
  styles: [`
    .bview { display: grid; grid-template-columns: 252px 1fr; height: 100vh; min-width: 0; }
  `],
})
export class BrowseView implements OnInit {
  readonly store = inject(BrowseStore);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    void this.store.loadRoots();
    // URL(root/path/q)是單一真相:初次 + 每次變動都套用。
    this.route.queryParams.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      const root = p['root'] != null ? Number(p['root']) : null;
      void this.store.applyUrl(root, (p['path'] as string) ?? '', (p['q'] as string) ?? '');
    });
  }
}
