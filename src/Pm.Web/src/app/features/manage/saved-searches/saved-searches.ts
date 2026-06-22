import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ManageStore } from '../manage.store';

// 契約(route /saved):收藏的搜尋 —— 查詢卡片格。
// 資料一律來自 ManageStore(PmApi.savedSearches());mock-only 的 hits/special 無 API 來源 → 已隱藏。
@Component({
  selector: 'app-saved-searches',
  imports: [],
  templateUrl: './saved-searches.html',
  styleUrl: './saved-searches.css',
})
export class SavedSearches implements OnInit {
  private readonly store = inject(ManageStore);
  private readonly router = inject(Router);

  // 收藏的搜尋(讀 store signal)
  readonly saved = this.store.saved;
  readonly loading = this.store.savedLoading;
  readonly error = this.store.savedError;

  // 目前 hover 的卡片 id(純視覺,signal 管狀態)
  readonly hovered = signal<number | null>(null);
  // 目前選到的卡片 id
  readonly active = signal<number | null>(null);

  ngOnInit(): void {
    void this.store.loadSaved();
  }

  onEnter(id: number): void {
    this.hovered.set(id);
  }
  onLeave(): void {
    this.hovered.set(null);
  }

  // 點卡片:先標記選取,再導到 /gallery(套用查詢到頂欄屬下輪,deferred 註明)。
  onPick(id: number): void {
    this.active.set(id);
    void this.router.navigate(['/gallery']);
  }

  // 刪除收藏。
  onDelete(id: number, ev: Event): void {
    ev.stopPropagation();
    void this.store.deleteSaved(id);
  }
}
