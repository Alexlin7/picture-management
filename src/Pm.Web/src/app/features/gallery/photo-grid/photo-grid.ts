import { Component, computed, inject, signal } from '@angular/core';
import { TAG_COLOR } from '@core/tag-color';
import { type PhotoListItem } from '@core/api/pm-api';
import { GalleryStore, type SearchToken } from '../gallery.store';

// 契約:頂欄 token 搜尋列 + masonry 圖牆。點 tile → 寫入 store 選取。
@Component({
  selector: 'app-photo-grid',
  imports: [],
  templateUrl: './photo-grid.html',
  styleUrl: './photo-grid.css',
})
export class PhotoGrid {
  private readonly store = inject(GalleryStore);

  // 資料來源:store(來自 PmApi)
  readonly photos = this.store.photos;
  readonly hitCount = this.store.hitCount;
  readonly wd14Queue = this.store.wd14Queue;
  readonly loading = this.store.loading;
  readonly error = this.store.error;
  readonly hasMore = this.store.hasMore;

  // 頂欄目前搜尋 token(可 ×)
  readonly tokens = this.store.tokens;

  // 選取狀態(讀 store)
  readonly selectedId = this.store.selectedId;
  readonly selCount = computed(() => (this.selectedId() === null ? 0 : 1));

  // 兩段式檢視切換:dense=小圖密集 / large=大圖(純視覺本地狀態)
  readonly viewMode = signal<'dense' | 'large'>('dense');

  // 千分位
  readonly hitCountText = computed(() => this.hitCount().toLocaleString('en-US'));
  readonly wd14QueueText = computed(() => this.wd14Queue.toLocaleString('en-US'));

  // 縮圖 URL(依 hash,絕不碰原圖)
  thumb(id: number): string {
    return this.store.thumbUrl(id);
  }

  // tile 高度:依 width/height 推 aspect ratio,較高 tile 較高。
  // API 無尺寸時退預設 ar=1.0。
  tileHeight(p: PhotoListItem): number {
    const ar = p.width && p.height ? p.width / p.height : 1.0;
    return Math.round(220 + (1 / ar) * 70);
  }

  // kind → 顏色
  kindColor(kind: string): string {
    return TAG_COLOR[kind] ?? TAG_COLOR['general'];
  }

  // 移除頂欄 token
  removeToken(idx: number, ev: Event): void {
    ev.stopPropagation();
    this.store.removeToken(idx);
  }

  // token 膠囊樣式(分色,半透明底)
  tokenStyle(t: SearchToken): Record<string, string> {
    const c = this.kindColor(t.kind);
    return {
      color: c,
      background: this.rgba(c, 0.12),
      'border-color': this.rgba(c, 0.3),
    };
  }

  // 點 tile → 寫入 store 選取
  pick(p: PhotoListItem): void {
    this.store.select(p.id);
  }

  // 載入下一頁
  loadMore(): void {
    void this.store.loadMore();
  }

  // hex → rgba helper(半透明底色)
  rgba(hex: string, a: number): string {
    const n = parseInt(hex.slice(1), 16);
    const r = (n >> 16) & 255;
    const g = (n >> 8) & 255;
    const b = n & 255;
    return `rgba(${r},${g},${b},${a})`;
  }
}
