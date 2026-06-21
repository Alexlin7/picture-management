import { Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { PmApi, PhotoListItem } from '../api/pm-api';

@Component({
  selector: 'app-gallery',
  standalone: true,
  imports: [ScrollingModule],
  styles: [`
    .bar { padding: 10px; border-bottom: 1px solid var(--line); }
    .bar input { width: 100%; padding: 8px 10px; background: var(--panel-2);
      border: 1px solid var(--line); border-radius: 8px; color: var(--text); box-sizing: border-box; }
    cdk-virtual-scroll-viewport { height: calc(100vh - 54px); }
    .row { display: grid; grid-template-columns: repeat(5, 1fr); gap: 6px; padding: 6px; }
    .tile { aspect-ratio: 1; background: var(--panel-2); border-radius: 6px;
      object-fit: cover; width: 100%; height: 100%; cursor: pointer; }
  `],
  template: `
    <div class="bar">
      <input placeholder="tag 搜尋(空白=AND,前綴 - =排除)"
             (keyup.enter)="run($any($event.target).value)" />
    </div>
    <cdk-virtual-scroll-viewport itemSize="140" (scrolledIndexChange)="onScroll($event)">
      <div class="row" *cdkVirtualFor="let r of rows()">
        @for (p of r; track p.id) {
          <img class="tile" [src]="api.thumbUrl(p.id)" (click)="select.emit(p.id)" loading="lazy" />
        }
      </div>
    </cdk-virtual-scroll-viewport>
  `,
})
export class Gallery {
  api = inject(PmApi);
  @Output() select = new EventEmitter<number>();

  private items = signal<PhotoListItem[]>([]);
  private cursor: number | null = null;
  private all: string[] = [];
  private none: string[] = [];
  private loading = false;

  // 把一維 items 切成每列 5 張給 virtual scroll
  rows = signal<PhotoListItem[][]>([]);

  async run(query: string) {
    this.all = []; this.none = [];
    for (const tok of query.split(/\s+/).filter(Boolean))
      (tok.startsWith('-') ? this.none : this.all).push(tok.replace(/^-/, ''));
    this.items.set([]); this.cursor = null;
    await this.loadMore();
  }

  async onScroll(index: number) {
    if (index > this.rows().length - 4 && this.cursor !== null) await this.loadMore();
  }

  private async loadMore() {
    if (this.loading) return;
    this.loading = true;
    try {
      const page = await this.api.search({ all: this.all, none: this.none, afterId: this.cursor, pageSize: 200 });
      this.items.update(x => [...x, ...page.items]);
      this.cursor = page.nextCursor ?? null;
      const flat = this.items();
      const rows: PhotoListItem[][] = [];
      for (let i = 0; i < flat.length; i += 5) rows.push(flat.slice(i, i + 5));
      this.rows.set(rows);
    } finally { this.loading = false; }
  }

  ngOnInit() { this.run(''); }
}
