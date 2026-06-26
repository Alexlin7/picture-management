import { Component, OnInit, inject, DestroyRef, ElementRef, computed, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FacetSidebar } from '../facet-sidebar/facet-sidebar';
import { PhotoGrid } from '../photo-grid/photo-grid';
import { Inspector } from '@features/inspector/inspector/inspector';
import { GalleryStore } from '../gallery.store';
import { useStageWidth } from '../../../core/use-stage-width';
import { shouldAutoCollapse, INSPECTOR_COLLAPSE, FACET_COLLAPSE } from '../../../core/layout-breakpoints';

// 相簿三欄:facet 側欄(252)· 圖牆(1fr)· 檢視器(350)。
// 動態欄寬:useStageWidth 量測 host 元素寬度,依門檻自動或手動收合側欄。
@Component({
  selector: 'app-gallery-view',
  imports: [FacetSidebar, PhotoGrid, Inspector],
  template: `
    <div class="gview" [style.grid-template-columns]="gridCols()">
      <app-facet-sidebar [sidebarCollapsed]="facetCollapsed()" />
      <div class="center-stage">
        <button
          class="edge-toggle et-left"
          (click)="toggleFacet()"
          [attr.aria-label]="facetCollapsed() ? '展開篩選側欄' : '收合篩選側欄'"
          [title]="facetCollapsed() ? '展開篩選' : '收合篩選'">
          <span aria-hidden="true">{{ facetCollapsed() ? '›' : '‹' }}</span>
        </button>
        <app-photo-grid />
        <button
          class="edge-toggle et-right"
          (click)="toggleInspector()"
          [attr.aria-label]="inspectorCollapsed() ? '展開檢視器' : '收合檢視器'"
          [title]="inspectorCollapsed() ? '展開檢視器' : '收合檢視器'">
          <span aria-hidden="true">{{ inspectorCollapsed() ? '‹' : '›' }}</span>
        </button>
      </div>
      <app-inspector [class.collapsed]="inspectorCollapsed()" [photoId]="store.selectedId()" />
    </div>
  `,
  styles: [
    `
      .gview {
        display: grid;
        height: 100vh;
        min-width: 0;
        position: relative;
        transition: grid-template-columns 0.15s ease;
      }
      @media (prefers-reduced-motion: reduce) {
        .gview {
          transition: none;
        }
      }

      /* 中間圖牆欄:相對定位以容納邊緣 toggle 鈕。 */
      .center-stage {
        position: relative;
        min-width: 0;
      }

      /* 側欄收合/展開箭頭(et-left=facet 邊緣;et-right=inspector 邊緣)。 */
      .edge-toggle {
        position: absolute;
        z-index: 10;
        top: 50%;
        transform: translateY(-50%);
        width: 18px;
        height: 44px;
        background: var(--color-panel);
        border: 1px solid var(--color-hair);
        color: var(--color-muted);
        cursor: pointer;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 13px;
        padding: 0;
        transition: background 0.1s, color 0.1s;
      }
      .edge-toggle:hover {
        color: var(--color-text);
        background: var(--color-raised);
      }
      .edge-toggle:focus-visible {
        outline: 2px solid var(--color-accent);
        outline-offset: 2px;
      }
      .et-left {
        left: 0;
        border-left: none;
        border-radius: 0 var(--radius-soft) var(--radius-soft) 0;
      }
      .et-right {
        right: 0;
        border-right: none;
        border-radius: var(--radius-soft) 0 0 var(--radius-soft);
      }
    `,
  ],
})
export class GalleryView implements OnInit {
  readonly store = inject(GalleryStore);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly hostRef = inject(ElementRef<HTMLElement>);
  private readonly stageWidth = useStageWidth(this.hostRef, this.destroyRef);

  readonly facetUserCollapsed = signal<boolean | null>(null);
  readonly inspectorUserCollapsed = signal<boolean | null>(null);

  readonly facetCollapsed = computed(() =>
    this.facetUserCollapsed() ?? shouldAutoCollapse(this.stageWidth(), FACET_COLLAPSE));
  readonly inspectorCollapsed = computed(() =>
    this.inspectorUserCollapsed() ?? shouldAutoCollapse(this.stageWidth(), INSPECTOR_COLLAPSE));

  readonly gridCols = computed(() => {
    const f = this.facetCollapsed() ? '0' : '252px';
    const i = this.inspectorCollapsed() ? '0' : '350px';
    return `${f} 1fr ${i}`;
  });

  toggleFacet(): void { this.facetUserCollapsed.set(!this.facetCollapsed()); }
  toggleInspector(): void { this.inspectorUserCollapsed.set(!this.inspectorCollapsed()); }

  ngOnInit(): void {
    void this.store.load(); // facet 樹
    // URL 'q' 是搜尋的單一真相:初次 + 每次變動(含上一頁)都套用並查詢。
    this.route.queryParams
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((p) => this.store.applyQuery((p['q'] as string) ?? ''));
  }
}
