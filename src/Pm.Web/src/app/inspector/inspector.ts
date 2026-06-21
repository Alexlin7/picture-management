import { Component, Input, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { PmApi, PhotoDetail } from '../api/pm-api';
import { tagColor } from '../tag-color';

@Component({
  selector: 'app-inspector',
  standalone: true,
  imports: [DecimalPipe],
  styles: [`
    .pad { padding: 12px; overflow: auto; height: 100vh; box-sizing: border-box; }
    .hash { font-family: "JetBrains Mono", monospace; font-size: 11px; color: var(--muted); word-break: break-all; }
    .loc { font-size: 12px; padding: 4px 8px; background: var(--panel-2); border-radius: 6px; margin: 4px 0; }
    .chip { display: inline-block; font-size: 12px; padding: 2px 8px; border-radius: 999px;
      margin: 2px; border: 1px solid; }
    .empty { color: var(--muted); padding: 24px; text-align: center; }
  `],
  template: `
    @if (photo(); as p) {
      <div class="pad">
        <img [src]="api.thumbUrl(p.id)" style="width:100%;border-radius:8px" />
        <h4>身分</h4>
        <div class="hash">{{ p.fileHash }}</div>
        <h4>位置</h4>
        @for (l of p.locations; track l.relPath) {
          <div class="loc">{{ l.relPath }} <span style="color:var(--muted)">· {{ l.status }}</span></div>
        }
        <h4>標籤</h4>
        @for (t of p.tags; track t.id) {
          <span class="chip" [style.color]="color(t.kind)" [style.borderColor]="color(t.kind)"
                [style.borderStyle]="t.source === 'wd14' ? 'dashed' : 'solid'">
            {{ t.name }}@if (t.confidence != null) { <span> {{ (t.confidence * 100) | number:'1.0-0' }}%</span> }
          </span>
        }
        @if (p.cameraModel) { <p class="hash">📷 {{ p.cameraModel }}</p> }
      </div>
    } @else {
      <div class="empty">選一張圖看細節</div>
    }
  `,
})
export class Inspector {
  api = inject(PmApi);
  photo = signal<PhotoDetail | null>(null);
  color = tagColor;

  private _id: number | null = null;
  @Input() set photoId(v: number | null) {
    this._id = v;
    if (v == null) { this.photo.set(null); return; }
    this.api.photo(v).then(d => this.photo.set(d));
  }
}
