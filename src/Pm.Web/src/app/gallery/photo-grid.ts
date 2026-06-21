import { Component, computed, output, signal } from '@angular/core';
import { TAG_COLOR } from '../tag-color';
import {
  artGradient,
  MOCK_HIT_COUNT,
  MOCK_PHOTOS,
  MOCK_SEARCH_TOKENS,
  MOCK_WD14_QUEUE,
  type MockPhoto,
  type SearchToken,
} from '../mock/mock-data';

// 契約:頂欄 token 搜尋列 + masonry 圖牆。點 tile → 發出 photo id。
@Component({
  selector: 'app-photo-grid',
  imports: [],
  templateUrl: './photo-grid.html',
  styleUrl: './photo-grid.css',
})
export class PhotoGrid {
  selectPhoto = output<number>();

  // 假資料(本輪按鈕先到位)
  readonly photos = MOCK_PHOTOS;
  readonly hitCount = MOCK_HIT_COUNT;
  readonly wd14Queue = MOCK_WD14_QUEUE;

  // 頂欄目前搜尋 token(可 ×)
  readonly tokens = signal<SearchToken[]>([...MOCK_SEARCH_TOKENS]);

  // 選取狀態(signal)
  readonly selectedId = signal<number | null>(null);
  readonly selCount = computed(() => (this.selectedId() === null ? 0 : 1));

  // 兩段式檢視切換:dense=小圖密集 / large=大圖
  readonly viewMode = signal<'dense' | 'large'>('dense');

  // 千分位
  readonly hitCountText = computed(() => this.hitCount.toLocaleString('en-US'));
  readonly wd14QueueText = computed(() => this.wd14Queue.toLocaleString('en-US'));

  // tile 高度:較高 tile = round(220 + (1/ar)*70) px
  tileHeight(p: MockPhoto): number {
    return Math.round(220 + (1 / p.ar) * 70);
  }

  // tile 背景漸層(無真圖)
  art(seed: number): string {
    return artGradient(seed);
  }

  // hover 顯示前 3 個 tag 的 mini chips
  chips(p: MockPhoto) {
    return p.tags.slice(0, 3);
  }

  // kind → 顏色
  kindColor(kind: string): string {
    return TAG_COLOR[kind] ?? TAG_COLOR['general'];
  }

  // 移除頂欄 token
  removeToken(idx: number, ev: Event): void {
    ev.stopPropagation();
    this.tokens.update((ts) => ts.filter((_, i) => i !== idx));
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

  // 點 tile → emit id + 標記 selected
  pick(p: MockPhoto): void {
    this.selectedId.set(p.id);
    this.selectPhoto.emit(p.id);
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
