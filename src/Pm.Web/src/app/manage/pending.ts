import { Component, inject, signal, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PmApi, PendingSegment } from '../api/pm-api';

@Component({
  selector: 'app-pending', standalone: true, imports: [FormsModule],
  styles: [`.pad{padding:16px} .seg{display:grid;grid-template-columns:1fr auto auto auto;gap:8px;align-items:center;
    padding:8px;border:1px solid var(--line);border-radius:8px;margin:6px 0}
    select,button{padding:6px;background:var(--panel-2);color:var(--text);border:1px solid var(--line);border-radius:6px}`],
  template: `
    <div class="pad">
      <h3>匯入確認:路徑 → 標籤</h3>
      @for (s of segs(); track s.segment) {
        <div class="seg">
          <span><b>{{ s.segment }}</b> <span style="color:var(--muted)">×{{ s.count }} · {{ s.samplePath }}</span></span>
          <select #act [value]="s.suggestedAction">
            <option value="map_to_tag">對應標籤</option>
            <option value="ignore">略過</option>
            <option value="meta_year">年份</option>
          </select>
          <button (click)="apply(s, act.value)">套用</button>
        </div>
      } @empty { <p style="color:var(--muted)">沒有新的路徑段要確認。</p> }
    </div>`,
})
export class Pending {
  api = inject(PmApi);
  @Input() rootId = 0;
  segs = signal<PendingSegment[]>([]);
  async ngOnInit() { if (this.rootId) this.segs.set(await this.api.pendingSegments(this.rootId)); }
  async apply(s: PendingSegment, action: string) {
    await this.api.applyRule({ rootId: this.rootId, segment: s.segment, action, tagName: s.segment });
    this.segs.update(x => x.filter(p => p.segment !== s.segment));
  }
}
