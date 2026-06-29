import { Component, inject, ElementRef, ViewChild, AfterViewInit, OnDestroy, computed, input, output } from '@angular/core';
import { BrowseStore } from '../browse.store';
import { Thumb } from '@core/ui/thumb';
import { Activate } from '@core/a11y/activate';
import { InnerTagFilter } from '../inner-tag-filter/inner-tag-filter';
import type { PhotoListItem, FolderNode } from '@core/api/pm-api';
import { Masonry } from '../../../core/ui/masonry';
import { MIN_COL_WIDTH, MASONRY_GAP } from '../../../core/layout-breakpoints';

// 資料夾瀏覽主區:麵包屑 + 子資料夾晶片(深層下鑽)+ 遞迴圖牆(無限捲)+ 夾內疊 tag 帶。
@Component({
  selector: 'app-browse-grid',
  imports: [Thumb, InnerTagFilter, Masonry, Activate],
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

  // 選取格在目前載入清單中的 index,餵給 Masonry 設 aria-pressed(無選取 → -1)。
  readonly selectedIndex = computed(() => {
    const id = this.store.selectedId();
    return id == null ? -1 : this.photos().findIndex((p) => p.id === id);
  });

  // ③g:手機抽屜模式;true → 顯示「資料夾」鈕。
  readonly mobile = input(false);
  // 點「資料夾」鈕 → 請上層開左抽屜(folder tree)。
  readonly openFilter = output<void>();
  // 點圖 / 鍵盤選取 → 請上層開右抽屜(inspector)。
  readonly opened = output<void>();

  readonly gap = MASONRY_GAP;
  readonly stdCol = MIN_COL_WIDTH.standard;
  aspectNum = (p: unknown): number => {
    const photo = p as PhotoListItem;
    return photo.width && photo.height ? photo.width / photo.height : 1;
  };

  aspect(p: PhotoListItem): string { return p.width && p.height ? `${p.width}/${p.height}` : '1/1'; }
  enter(relPath: string): void { this.store.enterFolder(relPath); }
  enterChild(c: FolderNode): void { this.store.enterFolder(c.relPath); }
  pick(p: PhotoListItem): void { this.store.select(p.id); }
  // masonry roving 導航:click / Enter / Space 觸發 → 選取該圖,並請上層開右抽屜(手機)。
  onActivate(e: { item: unknown; index: number }): void { this.pick(e.item as PhotoListItem); this.opened.emit(); }
  // 方向鍵走到結尾附近:補載下一頁(同 sentinel IO 的 store.loadMore 與守衛)。
  onLoadMore(): void { if (this.hasMore() && !this.loading()) void this.store.loadMore(); }

  @ViewChild('sentinel') private sentinel?: ElementRef<HTMLElement>;
  private io?: IntersectionObserver;
  ngAfterViewInit(): void {
    if (!this.sentinel) return;
    this.io = new IntersectionObserver((es) => {
      if (es[0]?.isIntersecting && this.hasMore() && !this.loading()) {
        // 載完一頁後重新觀察 sentinel:強制 IO 以當前狀態再回呼一次。
        // 若新頁仍未把 sentinel 推出 rootMargin(短結果集/矮版面),會續載直到填滿或無更多 —— 不再停擺。
        void this.store.loadMore().then(() => this.rearm());
      }
    }, { rootMargin: '600px' });
    this.io.observe(this.sentinel.nativeElement);
  }
  private rearm(): void {
    if (!this.io || !this.sentinel) return;
    this.io.unobserve(this.sentinel.nativeElement);
    this.io.observe(this.sentinel.nativeElement);
  }
  ngOnDestroy(): void { this.io?.disconnect(); }
}
