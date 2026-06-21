import { Component, inject, signal } from '@angular/core';
import { PmApi } from '../api/pm-api';

@Component({
  selector: 'app-reconcile', standalone: true,
  styles: [`.pad{padding:16px} .row{padding:8px;border:1px solid var(--line);border-radius:8px;margin:6px 0;font-size:13px}`],
  template: `
    <div class="pad">
      <h3>待確認:可能失蹤的圖</h3>
      <p style="color:var(--muted)">只列出所有位置都已不存在的圖(搬移的會自動續接,不在此)。</p>
      @for (m of missing(); track m.id) {
        <div class="row"><span class="">{{ m.paths.join(', ') }}</span></div>
      } @empty { <p style="color:var(--muted)">沒有待確認項目 🎉</p> }
    </div>`,
})
export class Reconcile {
  api = inject(PmApi);
  missing = signal<{ id: number; fileHash: string; paths: string[] }[]>([]);
  async ngOnInit() { this.missing.set(await this.api.missing()); }
}
