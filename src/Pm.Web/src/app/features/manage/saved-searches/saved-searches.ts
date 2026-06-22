import { Component, inject, signal } from '@angular/core';
import { ManageStore, type SavedSearch } from '../manage.store';
import { TAG_COLOR } from '@core/tag-color';

// 契約(route /saved):收藏的搜尋 —— 查詢卡片格。
@Component({
  selector: 'app-saved-searches',
  imports: [],
  templateUrl: './saved-searches.html',
  styleUrl: './saved-searches.css',
})
export class SavedSearches {
  private readonly store = inject(ManageStore);

  // 收藏的搜尋(資料來源來自 store)
  readonly saved = signal<SavedSearch[]>(this.store.saved());

  // meta 色點(special 卡片標題用)
  readonly metaColor = TAG_COLOR['meta'];

  // 目前 hover 的卡片 index(純視覺,signal 管狀態)
  readonly hovered = signal<number | null>(null);

  // 點卡片:本輪先只記目前選到的查詢(下輪接真實 API)
  readonly active = signal<number | null>(null);

  onEnter(i: number): void {
    this.hovered.set(i);
  }
  onLeave(): void {
    this.hovered.set(null);
  }
  onPick(i: number): void {
    this.active.set(i);
  }
}
