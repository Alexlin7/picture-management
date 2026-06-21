import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PmApi, Root } from '../api/pm-api';

@Component({
  selector: 'app-roots', standalone: true, imports: [FormsModule],
  styles: [`.pad{padding:16px} .root{padding:8px;border:1px solid var(--line);border-radius:8px;margin:6px 0}
    input,button{padding:8px;margin-right:6px;background:var(--panel-2);color:var(--text);border:1px solid var(--line);border-radius:6px}`],
  template: `
    <div class="pad">
      <h3>圖庫來源</h3>
      <input [(ngModel)]="name" placeholder="名稱" />
      <input [(ngModel)]="path" placeholder="絕對路徑" style="width:320px" />
      <button (click)="add()">新增</button>
      @for (r of roots(); track r.id) {
        <div class="root">
          <b>{{ r.name }}</b> <span style="color:var(--muted)">{{ r.absPath }}</span>
          <button (click)="scan(r.id)">重新掃描</button>
        </div>
      }
    </div>`,
})
export class Roots {
  api = inject(PmApi);
  roots = signal<Root[]>([]);
  name = ''; path = '';
  async ngOnInit() { this.roots.set(await this.api.roots()); }
  async add() { if (this.name && this.path) { await this.api.createRoot(this.name, this.path); this.roots.set(await this.api.roots()); } }
  async scan(id: number) { await this.api.scan(id); }
}
