import { Component, OnInit, inject } from '@angular/core';
import { FacetSidebar } from '../facet-sidebar/facet-sidebar';
import { PhotoGrid } from '../photo-grid/photo-grid';
import { Inspector } from '@features/inspector/inspector/inspector';
import { GalleryStore } from '../gallery.store';

// 相簿三欄:facet 側欄(252)· 圖牆(1fr)· 檢視器(350)。
@Component({
  selector: 'app-gallery-view',
  imports: [FacetSidebar, PhotoGrid, Inspector],
  template: `
    <div class="gview">
      <app-facet-sidebar />
      <app-photo-grid />
      <app-inspector [photoId]="store.selectedId()" />
    </div>
  `,
  styles: [
    `
      .gview {
        display: grid;
        grid-template-columns: 252px 1fr 350px;
        height: 100vh;
        min-width: 0;
      }
      @media (max-width: 1180px) {
        .gview {
          grid-template-columns: 230px 1fr;
        }
        app-inspector {
          display: none;
        }
      }
    `,
  ],
})
export class GalleryView implements OnInit {
  readonly store = inject(GalleryStore);

  ngOnInit(): void {
    void this.store.load();
  }
}
