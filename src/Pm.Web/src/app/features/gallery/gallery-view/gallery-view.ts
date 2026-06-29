import { Component, OnInit, inject, DestroyRef, ElementRef, computed, signal, effect } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FacetSidebar } from '../facet-sidebar/facet-sidebar';
import { PhotoGrid } from '../photo-grid/photo-grid';
import { Inspector } from '@features/inspector/inspector/inspector';
import { DrawerPanel } from '@core/ui/drawer-panel';
import { GalleryStore } from '../gallery.store';
import { LightboxService } from '@core/ui/lightbox';
import { useStageWidth } from '../../../core/use-stage-width';
import { shouldAutoCollapse, INSPECTOR_COLLAPSE, FACET_COLLAPSE, MOBILE } from '../../../core/layout-breakpoints';

// 相簿三欄:facet 側欄(252)· 圖牆(1fr)· 檢視器(350)。
// 動態欄寬:useStageWidth 量測 host 元素寬度,依門檻自動或手動收合側欄。
@Component({
  selector: 'app-gallery-view',
  imports: [FacetSidebar, PhotoGrid, Inspector, DrawerPanel],
  template: `
    <div class="gview" [style.grid-template-columns]="gridCols()">
      @if (!mobile()) {
        <app-facet-sidebar [sidebarCollapsed]="facetCollapsed()" />
      }
      <div class="center-stage" data-testid="center-stage">
        @if (!mobile()) {
          <button
            class="edge-toggle et-left"
            (click)="toggleFacet()"
            [attr.aria-label]="facetCollapsed() ? '展開篩選側欄' : '收合篩選側欄'"
            [title]="facetCollapsed() ? '展開篩選' : '收合篩選'">
            <span aria-hidden="true">{{ facetCollapsed() ? '›' : '‹' }}</span>
          </button>
        }
        <app-photo-grid [mobile]="mobile()" (openFilter)="onOpenFilter()" (opened)="onImageOpened()" />
        @if (!mobile()) {
          <button
            class="edge-toggle et-right"
            (click)="toggleInspector()"
            [attr.aria-label]="inspectorCollapsed() ? '展開檢視器' : '收合檢視器'"
            [title]="inspectorCollapsed() ? '展開檢視器' : '收合檢視器'">
            <span aria-hidden="true">{{ inspectorCollapsed() ? '‹' : '›' }}</span>
          </button>
        }
      </div>
      @if (!mobile()) {
        <app-inspector [class.collapsed]="inspectorCollapsed()" [photoId]="store.selectedId()" (expand)="openLightbox()" />
      }

      @if (mobile()) {
        <app-drawer-panel side="left" [open]="facetDrawerOpen()" title="篩選" (close)="facetDrawerOpen.set(false)">
          <app-facet-sidebar />
        </app-drawer-panel>
        <app-drawer-panel side="right" [open]="inspectorDrawerOpen()" title="圖片詳情" (close)="inspectorDrawerOpen.set(false)">
          <app-inspector [photoId]="store.selectedId()" (expand)="openLightbox()" />
        </app-drawer-panel>
      }
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

      /* 中間圖牆欄:相對定位以容納邊緣 toggle 鈕。
         min-height:0 壓掉 grid item 預設 min-height:auto,height:100% 撐滿列高 ——
         否則內容(masonry)會把 grid item 撐高,photo-grid 的 .view 失去有界高度而不再捲。 */
      .center-stage {
        position: relative;
        min-width: 0;
        min-height: 0;
        height: 100%;
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

      /* ③g:把原側板元件投影進抽屜時,收束其內建固定尺寸,改填滿抽屜 body 並讓其自身 overflow 捲動。
         ::ng-deep 必要 —— 隔離編譯下父層選不到子元件內部 .sidebar/:host;限定在本 view scope 下。 */
      :host ::ng-deep app-drawer-panel app-facet-sidebar .sidebar {
        width: 100%;
        height: 100%;
        border-right: none; /* 抽屜 panel 自帶 border-right,去掉側欄重複的 hairline */
      }
      :host ::ng-deep app-drawer-panel app-inspector {
        width: 100%;
        height: 100%;
        border-left: none;
      }
    `,
  ],
})
export class GalleryView implements OnInit {
  readonly store = inject(GalleryStore);
  private readonly lightbox = inject(LightboxService);
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

  // ③g 手機抽屜模式:stage 寬 < MOBILE。桌面(false)行為一切照舊。
  readonly mobile = computed(() => {
    const w = this.stageWidth();
    return w > 0 && w < MOBILE;
  });
  readonly facetDrawerOpen = signal(false);
  readonly inspectorDrawerOpen = signal(false);

  // 篩選鈕 → 開左抽屜。
  onOpenFilter(): void { this.facetDrawerOpen.set(true); }
  // 點圖(含同圖重點、鍵盤選取)→ 手機才開右抽屜。
  onImageOpened(): void { if (this.mobile()) this.inspectorDrawerOpen.set(true); }

  readonly gridCols = computed(() => {
    if (this.mobile()) return '1fr';
    const f = this.facetCollapsed() ? '0' : '252px';
    const i = this.inspectorCollapsed() ? '0' : '350px';
    return `${f} 1fr ${i}`;
  });

  toggleFacet(): void { this.facetUserCollapsed.set(!this.facetCollapsed()); }
  toggleInspector(): void { this.inspectorUserCollapsed.set(!this.inspectorCollapsed()); }

  constructor() {
    // 由手機切回桌面(resize / 旋轉)時關掉抽屜,避免殘留覆蓋層卡住桌面三欄。
    effect(() => {
      if (!this.mobile()) {
        this.facetDrawerOpen.set(false);
        this.inspectorDrawerOpen.set(false);
      }
    });
  }

  // inspector「⤢ 放大」→ 以本頁 store 開 lightbox(←→ 走目前載入清單,到尾補載)。
  openLightbox(): void {
    const id = this.store.selectedId();
    if (id == null) return;
    this.lightbox.open({
      ids: () => this.store.photos().map((p) => p.id),
      total: () => this.store.hitCount(),
      startId: id,
      loadMore: () => { if (this.store.hasMore() && !this.store.loading()) void this.store.loadMore(); },
      select: (pid) => this.store.select(pid),
    });
  }

  ngOnInit(): void {
    void this.store.load(); // facet 樹
    // URL 'q' 是搜尋的單一真相:初次 + 每次變動(含上一頁)都套用並查詢。
    this.route.queryParams
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((p) => this.store.applyQuery((p['q'] as string) ?? ''));
  }
}
