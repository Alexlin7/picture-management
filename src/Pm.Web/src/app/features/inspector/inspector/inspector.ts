import { Component, computed, effect, inject, input } from '@angular/core';
import {
  InspectorStore,
  type PhotoDetail,
  type TagKind,
} from '../inspector.store';
import { TAG_COLOR, KIND_LABEL } from '@core/tag-color';

// 契約:右側檢視器。輸入選中的 photo id(signal input)。
// 內容:預覽圖(縮圖)、身分→位置簽名、tag lanes(分色)、EXIF。
// 資料來源接縫:InspectorStore(非同步載入 PhotoDetail)。
@Component({
  selector: 'app-inspector',
  imports: [],
  templateUrl: './inspector.html',
  styleUrl: './inspector.css',
})
export class Inspector {
  private readonly store = inject(InspectorStore);

  photoId = input<number | null>(null);

  // store 的非同步狀態(元件只讀)
  readonly photo = this.store.detail;
  readonly loading = this.store.loading;
  readonly error = this.store.error;
  readonly thumbUrl = this.store.thumbUrl;

  constructor() {
    // id 變動 → 觸發載入(store 內處理競態與清空)
    effect(() => {
      void this.store.load(this.photoId());
    });
  }

  // tag lane 的固定排序(依 kind)
  private readonly laneOrder: TagKind[] = [
    'character', 'copyright', 'general', 'meta', 'path', 'manual',
  ];

  // 依序組出有 tag 的 lane(空 lane 不顯示)。
  // 注意:tag.kind 來自 API(string),以 TAG_COLOR/KIND_LABEL 之 key 比對分色。
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

  // EXIF 是否有相機資訊(takenAt / cameraModel 任一存在)
  readonly hasExif = computed(() => {
    const p = this.photo();
    return !!p && (p.takenAt != null || p.cameraModel != null);
  });

  // SHA-256 身分:取 fileHash 前 8 碼。
  readonly hash = computed(() => {
    const p = this.photo();
    return p ? p.fileHash.slice(0, 8) : '';
  });

  // hex → rgba helper(半透明底色 / 邊框用)
  rgba(hex: string, a: number): string {
    const n = parseInt(hex.slice(1), 16);
    return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${a})`;
  }

  pct(c: number): number {
    return Math.round(c * 100);
  }

  // API 無 title 欄位:取首個位置 relPath 的檔名當標題,無位置則退回 hash。
  fileName(p: PhotoDetail): string {
    const rel = p.locations[0]?.relPath;
    if (!rel) return p.fileHash.slice(0, 12);
    const parts = rel.split(/[\\/]/);
    return parts[parts.length - 1] || rel;
  }
}
