import { Component, inject, signal } from '@angular/core';
import { ManageStore, type ReconRow } from '../manage.store';
import { artGradient } from '@core/placeholder-art';

// 契約(route /reconcile):失蹤待辦匣 —— 縮圖卡 + 上次位置 + 三動作。
type ReconState = 'pending' | 'waiting' | 'externalized' | 'deleted';

interface ReconItem extends ReconRow {
  art: string;
  state: ReconState;
}

@Component({
  selector: 'app-reconcile',
  imports: [],
  templateUrl: './reconcile.html',
  styleUrl: './reconcile.css',
})
export class Reconcile {
  private readonly store = inject(ManageStore);

  // 換位置(同 hash)已自動續接的張數,只是說明用
  readonly relocated = this.store.relocated();

  // 失蹤清單;state 用 signal 管,按鈕點擊後就地切狀態
  readonly items = signal<ReconItem[]>(
    this.store.reconRows().map((r) => ({ ...r, art: artGradient(r.seed), state: 'pending' as ReconState })),
  );

  // 還沒處理的(pending)張數 → vhead pill
  pendingCount(): number {
    return this.items().filter((it) => it.state === 'pending').length;
  }

  // 三動作:就地改 state(本輪按鈕先到位,真實 API 下輪)
  setState(target: ReconItem, state: ReconState): void {
    this.items.update((list) =>
      list.map((it) => (it === target ? { ...it, state } : it)),
    );
  }
}
