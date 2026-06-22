import { Injectable, computed, signal } from '@angular/core';
import {
  DEFAULT_SELECTED_ID,
  MOCK_FACETS,
  MOCK_HIT_COUNT,
  MOCK_PHOTOS,
  MOCK_ROOTLESS,
  MOCK_SEARCH_TOKENS,
  MOCK_TREE,
  MOCK_WD14_QUEUE,
  getMockPhoto,
  type MockPhoto,
  type SearchToken,
} from '@testing/mock-data';

// 相簿資料來源 store:元件與假資料之間的唯一接縫。
// 日後把資料換成 @core/api/pm-api 的 PmApi 時只改這個檔,元件不動。
@Injectable({ providedIn: 'root' })
export class GalleryStore {
  // 圖牆資料
  private readonly _photos = signal<MockPhoto[]>(MOCK_PHOTOS);
  readonly photos = this._photos.asReadonly();

  // 命中數與 WD14 佇列(目前為常數)
  readonly hitCount = MOCK_HIT_COUNT;
  readonly wd14Queue = MOCK_WD14_QUEUE;

  // 頂欄目前搜尋 token(可 ×)
  private readonly _tokens = signal<SearchToken[]>([...MOCK_SEARCH_TOKENS]);
  readonly tokens = this._tokens.asReadonly();

  // 側欄 facet 資料
  readonly tree = MOCK_TREE;
  readonly rootless = MOCK_ROOTLESS;
  readonly facetsGeneral = MOCK_FACETS.general;
  readonly facetsMeta = MOCK_FACETS.meta;

  // 選取狀態
  private readonly _selectedId = signal<number | null>(DEFAULT_SELECTED_ID);
  readonly selectedId = this._selectedId.asReadonly();

  // 選取的 photo(供未來使用)
  readonly selectedPhoto = computed(() => {
    const id = this._selectedId();
    return id === null ? undefined : getMockPhoto(id);
  });

  // 移除頂欄 token
  removeToken(idx: number): void {
    this._tokens.update((ts) => ts.filter((_, i) => i !== idx));
  }

  // 設定選取
  select(id: number | null): void {
    this._selectedId.set(id);
  }
}

// 元件只從 store 取型別,不直接 import @testing/mock-data。
export type { MockPhoto, MockSugg, MockTag, SearchToken, TreeNode } from '@testing/mock-data';
