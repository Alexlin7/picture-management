import { Component, OnInit, inject, signal } from '@angular/core';
import { ManageStore } from '../manage.store';
import { ToastService } from '@core/ui/toast';

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
  private readonly toast = inject(ToastService);

  // 圖庫來源(讀 store signal)
  readonly roots = this.store.roots;
  readonly loading = this.store.rootsLoading;
  readonly error = this.store.rootsError;

  // 正在重新掃描的來源 id(純視覺,signal 管狀態)
  readonly scanning = signal<number | null>(null);

  // 新增來源 inline 表單開關
  readonly adding = signal(false);

  ngOnInit(): void {
    void this.store.loadRoots();
  }

  // 點「重新掃描」:標記掃描中 → 呼 API → 完成後清掃描中。
  async onRescan(id: number): Promise<void> {
    this.scanning.set(id);
    try {
      const status = await this.store.rescan(id);
      const r = status.result;
      this.toast.success(
        r
          ? `掃描完成:新增 ${r.newPhotos} 張,位置 ${r.newLocations} 筆,縮圖 ${r.thumbsGenerated} 張`
          : '掃描完成',
      );
    } catch (e) {
      this.toast.error(e instanceof Error ? e.message : '掃描啟動失敗');
    } finally {
      this.scanning.set(null);
    }
  }

  // 新增來源(inline 表單;無資料夾挑選器 → 手填絕對路徑 + 名稱)。
  async submitAdd(absPath: string, name: string, pathInput: HTMLInputElement): Promise<void> {
    const p = absPath.trim();
    if (!p) {
      this.toast.error('請輸入來源資料夾的絕對路徑');
      pathInput.focus();
      return;
    }
    const n = name.trim() || p;
    await this.store.createRoot(n, p);
    this.toast.success(`已新增來源「${n}」`);
    this.adding.set(false);
  }
}
