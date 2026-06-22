import { Component, computed, inject, input, signal } from '@angular/core';
import { artGradient } from '@core/placeholder-art';
import {
  InspectorStore,
  type MockPhoto,
  type TagKind,
} from '../inspector.store';
import { TAG_COLOR, KIND_LABEL } from '@core/tag-color';

// 契約:右側檢視器。輸入選中的 photo id(signal input)。
// 內容:預覽圖、身分→位置簽名、tag lanes(分色)、WD14 建議(虛線 chip + ✓/✕)、EXIF。
@Component({
  selector: 'app-inspector',
  imports: [],
  templateUrl: './inspector.html',
  styleUrl: './inspector.css',
})
export class Inspector {
  private readonly store = inject(InspectorStore);

  photoId = input<number | null>(null);

  // null → 顯示空狀態;否則取對應 photo(資料來源接縫:InspectorStore)
  readonly photo = computed<MockPhoto | null>(() =>
    this.store.lookup(this.photoId()),
  );

  // tag lane 的固定排序(依 kind)
  private readonly laneOrder: TagKind[] = [
    'character', 'copyright', 'general', 'meta', 'path', 'manual',
  ];

  // 依序組出有 tag 的 lane(空 lane 不顯示)
  readonly lanes = computed(() => {
    const p = this.photo();
    if (!p) return [];
    return this.laneOrder
      .map((kind) => ({
        kind,
        label: KIND_LABEL[kind],
        color: TAG_COLOR[kind],
        tags: p.tags.filter((t) => t.kind === kind),
      }))
      .filter((lane) => lane.tags.length > 0);
  });

  // 預覽漸層(對齊 mockup 的 art(seed))
  readonly previewBg = computed(() => {
    const p = this.photo();
    return p ? artGradient(p.seed) : '';
  });

  // SHA-256 身分:用 seed 假造 8 碼 hex(對齊 mockup (seed*2654435761>>>0).toString(16))
  readonly hash = computed(() => {
    const p = this.photo();
    if (!p) return '';
    return ((p.seed * 2654435761) >>> 0).toString(16).padStart(8, '0');
  });

  // hex → rgba helper(半透明底色 / 邊框用)
  rgba(hex: string, a: number): string {
    const n = parseInt(hex.slice(1), 16);
    return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${a})`;
  }

  kindColor(kind: TagKind): string {
    return TAG_COLOR[kind];
  }

  pct(c: number): number {
    return Math.round(c * 100);
  }

  // ---- WD14 建議的本地互動(本輪「按鈕先到位」,只記錄決定不打 API)----
  // key = sugg 的 name,value = 'accept' | 'reject';用 signal 讓 UI 反應
  private decisions = signal<Record<string, 'accept' | 'reject'>>({});
  decisionOf(name: string): 'accept' | 'reject' | undefined {
    return this.decisions()[name];
  }
  accept(name: string): void {
    this.decisions.update((d) => ({ ...d, [name]: 'accept' }));
  }
  reject(name: string): void {
    this.decisions.update((d) => ({ ...d, [name]: 'reject' }));
  }
}
