import { Component, inject, signal } from '@angular/core';
import { Gallery } from '../gallery/gallery';
import { Inspector } from '../inspector/inspector';
import { Roots } from '../manage/roots';
import { Reconcile } from '../manage/reconcile';
import { Pending } from '../manage/pending';
import { PmApi } from '../api/pm-api';

type View = 'gallery' | 'roots' | 'reconcile' | 'pending';

@Component({
  selector: 'app-workbench',
  standalone: true,
  imports: [Gallery, Inspector, Roots, Reconcile, Pending],
  styles: [`
    .wrap { display: grid; grid-template-columns: 58px 1fr 360px; height: 100vh; }
    .activity { background: var(--panel-2); border-right: 1px solid var(--line);
      display: flex; flex-direction: column; align-items: center; gap: 6px; padding-top: 10px; }
    .activity button { width: 42px; height: 42px; border-radius: 10px; cursor: pointer;
      background: transparent; border: 1px solid transparent; color: var(--muted); font-size: 18px; }
    .activity button.on { background: var(--panel); border-color: var(--line); color: var(--accent); }
    .main { overflow: hidden; }
    .insp { border-left: 1px solid var(--line); background: var(--panel); }
  `],
  template: `
    <div class="wrap">
      <nav class="activity">
        <button [class.on]="view() === 'gallery'" (click)="view.set('gallery')" title="相簿">▦</button>
        <button [class.on]="view() === 'roots'" (click)="view.set('roots')" title="圖庫來源">⌂</button>
        <button [class.on]="view() === 'pending'" (click)="view.set('pending')" title="匯入確認">✎</button>
        <button [class.on]="view() === 'reconcile'" (click)="view.set('reconcile')" title="待確認匣">⚑</button>
      </nav>
      <section class="main">
        @switch (view()) {
          @case ('gallery') { <app-gallery (select)="sel.set($event)" /> }
          @case ('roots') { <app-roots /> }
          @case ('reconcile') { <app-reconcile /> }
          @case ('pending') { <app-pending [rootId]="firstRootId()" /> }
        }
      </section>
      <aside class="insp"><app-inspector [photoId]="sel()" /></aside>
    </div>
  `,
})
export class Workbench {
  private api = inject(PmApi);
  sel = signal<number | null>(null);
  view = signal<View>('gallery');
  firstRootId = signal<number>(0);

  async ngOnInit() {
    const roots = await this.api.roots();
    if (roots.length) this.firstRootId.set(roots[0].id);
  }
}
