import { Injectable } from '@angular/core';
import { getMockPhoto, type MockPhoto } from '@testing/mock-data';

// 對外 re-export 型別,讓元件只從 store 取型別(未來換 PmApi 時不動元件 import)
export type { MockPhoto } from '@testing/mock-data';
export type { TagKind } from '@core/tag-color';

// Inspector 的資料接縫:目前讀 mock,未來換成 PmApi.photo(id)。
@Injectable({ providedIn: 'root' })
export class InspectorStore {
  // id 為 null 回 null;否則查對應 photo(未來:PmApi.photo(id))
  lookup(id: number | null): MockPhoto | null {
    return id == null ? null : (getMockPhoto(id) ?? null);
  }
}
