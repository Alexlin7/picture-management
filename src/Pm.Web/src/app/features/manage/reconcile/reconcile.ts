import { Component, OnInit, inject } from '@angular/core';
import { ManageStore } from '../manage.store';

// 契約(route /reconcile):失蹤待辦匣 —— 縮圖卡 + 上次位置 + 三動作。
// 資料一律來自 ManageStore(PmApi.missing());縮圖改用真實 thumbUrl(不再用漸層 seed)。
@Component({
  selector: 'app-reconcile',
  imports: [],
  templateUrl: './reconcile.html',
  styleUrl: './reconcile.css',
})
export class Reconcile implements OnInit {
  private readonly store = inject(ManageStore);

  // 失蹤清單 + 載入/錯誤/待處理數(讀 store signal)
  readonly items = this.store.recon;
  readonly loading = this.store.reconLoading;
  readonly error = this.store.reconError;
  readonly pendingCount = this.store.reconPending;

  ngOnInit(): void {
    void this.store.loadRecon();
  }

  // 縮圖 URL(依 photo id;絕不碰原圖)
  thumb(id: number): string {
    return this.store.thumbUrl(id);
  }

  // 三動作:對應軟刪 / 硬刪 / 等待。
  keepWaiting(id: number): void {
    void this.store.keepWaiting(id);
  }
  externalize(id: number): void {
    void this.store.externalize(id);
  }
  markDeleted(id: number): void {
    void this.store.purge(id);
  }
  undo(id: number): void {
    this.store.resetReconState(id);
  }
}
