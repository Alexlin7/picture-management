import { Component, OnInit, inject, signal } from '@angular/core';
import { ManageStore } from '../manage.store';

// 契約(route /roots):圖庫來源 —— 來源清單(資料夾 icon、路徑、重新掃描)。
// 資料一律來自 ManageStore(PmApi)。mock-only 的檔數/掃描時間/狀態點無 API 來源 → 已隱藏。
@Component({
  selector: 'app-roots',
  imports: [],
  templateUrl: './roots.html',
  styleUrl: './roots.css',
})
export class Roots implements OnInit {
  private readonly store = inject(ManageStore);

  // 圖庫來源(讀 store signal)
  readonly roots = this.store.roots;
  readonly loading = this.store.rootsLoading;
  readonly error = this.store.rootsError;

  // 正在重新掃描的來源 id(純視覺,signal 管狀態)
  readonly scanning = signal<number | null>(null);

  ngOnInit(): void {
    void this.store.loadRoots();
  }

  // 點「重新掃描」:標記掃描中 → 呼 API → 完成後清掃描中。
  async onRescan(id: number): Promise<void> {
    this.scanning.set(id);
    try {
      await this.store.rescan(id);
    } finally {
      this.scanning.set(null);
    }
  }

  // 點「新增來源」:純前端無資料夾挑選器 → 用 prompt 收 name/absPath 文字。
  async onAddSource(): Promise<void> {
    const absPath = window.prompt('輸入來源資料夾的絕對路徑(例:D:\\pics)');
    if (!absPath) return;
    const name = window.prompt('輸入這個來源的名稱', absPath) ?? absPath;
    await this.store.createRoot(name, absPath);
  }
}
