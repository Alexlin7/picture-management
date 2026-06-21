import { Component, signal } from '@angular/core';
import { MOCK_ROOTS, type RootRow } from '../mock/mock-data';

// 契約(route /roots):圖庫來源 —— 來源清單(資料夾 icon、狀態點、檔數、重新掃描)。
@Component({
  selector: 'app-roots',
  imports: [],
  templateUrl: './roots.html',
  styleUrl: './roots.css',
})
export class Roots {
  // 圖庫來源(假資料,本輪按鈕先到位,下輪接真實 API)
  readonly roots = signal<RootRow[]>(MOCK_ROOTS);

  // 正在重新掃描的來源 index(純視覺,signal 管狀態)
  readonly scanning = signal<number | null>(null);

  // 點「重新掃描」:本輪先標記正在掃描(下輪接真實 API)
  onRescan(i: number): void {
    this.scanning.set(i);
  }

  // 點「新增來源」:本輪先佔位(下輪接開資料夾挑選器)
  onAddSource(): void {
    // TODO: 下輪接真實 API / 資料夾挑選器
  }
}
