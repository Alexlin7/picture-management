import { Component, signal } from '@angular/core';
import { Gallery } from '../gallery/gallery';
import { Inspector } from '../inspector/inspector';

@Component({
  selector: 'app-workbench',
  standalone: true,
  imports: [Gallery, Inspector],
  styles: [`
    .wrap { display: grid; grid-template-columns: 58px 1fr 360px; height: 100vh; }
    .activity { background: var(--panel-2); border-right: 1px solid var(--line); }
    .main { overflow: hidden; }
    .insp { border-left: 1px solid var(--line); background: var(--panel); }
  `],
  template: `
    <div class="wrap">
      <nav class="activity"></nav>
      <section class="main"><app-gallery (select)="sel.set($event)" /></section>
      <aside class="insp"><app-inspector [photoId]="sel()" /></aside>
    </div>
  `,
})
export class Workbench {
  sel = signal<number | null>(null);
}
