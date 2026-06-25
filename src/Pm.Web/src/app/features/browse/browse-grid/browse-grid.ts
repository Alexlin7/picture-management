import { Component, inject, ElementRef, ViewChild, AfterViewInit, OnDestroy, computed } from '@angular/core';
import { BrowseStore } from '../browse.store';
import { Thumb } from '@core/ui/thumb';
import { InnerTagFilter } from '../inner-tag-filter/inner-tag-filter';
import type { PhotoListItem, FolderNode } from '@core/api/pm-api';

// 資料夾瀏覽主區:麵包屑 + 子資料夾晶片(深層下鑽)+ 遞迴圖牆(無限捲)+ 夾內疊 tag 帶。
@Component({
  selector: 'app-browse-grid',
  imports: [Thumb, InnerTagFilter],
  templateUrl: './browse-grid.html',
  styleUrl: './browse-grid.css',
})
export class BrowseGrid implements AfterViewInit, OnDestroy {
  readonly store = inject(BrowseStore);
  readonly breadcrumb = this.store.breadcrumb;
  readonly subfolders = this.store.subfolders;
  readonly photos = this.store.photos;
  readonly hitCount = this.store.hitCount;
  readonly loading = this.store.loading;
  readonly error = this.store.error;
  readonly hasMore = this.store.hasMore;

  // 局部 computed,避免動 store —— 空狀態判斷「篩 tag 後 0 結果」用。
  readonly innerCount = computed(() => this.store.innerTokens().length);

  readonly hitText = computed(() => this.hitCount().toLocaleString('en-US'));
  readonly fmt = (n: number): string => n.toLocaleString('en-US');

  aspect(p: PhotoListItem): string { return p.width && p.height ? `${p.width}/${p.height}` : '1/1'; }
  enter(relPath: string): void { this.store.enterFolder(relPath); }
  enterChild(c: FolderNode): void { this.store.enterFolder(c.relPath); }
  pick(p: PhotoListItem): void { this.store.select(p.id); }

  @ViewChild('sentinel') private sentinel?: ElementRef<HTMLElement>;
  private io?: IntersectionObserver;
  ngAfterViewInit(): void {
    if (!this.sentinel) return;
    this.io = new IntersectionObserver((es) => {
      if (es[0]?.isIntersecting && this.hasMore() && !this.loading()) void this.store.loadMore();
    }, { rootMargin: '600px' });
    this.io.observe(this.sentinel.nativeElement);
  }
  ngOnDestroy(): void { this.io?.disconnect(); }
}
