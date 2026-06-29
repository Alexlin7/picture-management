import { Component, OnInit, inject, DestroyRef, ElementRef, computed, signal, effect } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FolderTreeSidebar } from '../folder-tree-sidebar/folder-tree-sidebar';
import { BrowseGrid } from '../browse-grid/browse-grid';
import { Inspector } from '@features/inspector/inspector/inspector';
import { DrawerPanel } from '@core/ui/drawer-panel';
import { BrowseStore } from '../browse.store';
import { LightboxService } from '@core/ui/lightbox';
import { useStageWidth } from '../../../core/use-stage-width';
import { shouldAutoCollapse, FACET_COLLAPSE, INSPECTOR_COLLAPSE, MOBILE } from '../../../core/layout-breakpoints';

// 資料夾瀏覽三欄:資料夾樹側欄(252)· 圖牆(1fr)· 檢視器(350)。
// 動態欄寬:useStageWidth 量測 host 元素寬度,依門檻自動或手動收合側欄。
@Component({
  selector: 'app-browse-view',
  imports: [FolderTreeSidebar, BrowseGrid, Inspector, DrawerPanel],
  template: `
    <div class="bview" [style.grid-template-columns]="gridCols()">
      @if (!mobile()) {
        <app-folder-tree-sidebar [collapsed]="treeCollapsed()" />
      }
      <div class="center-stage" data-testid="center-stage">
        @if (!mobile()) {
          <button
            class="edge-toggle et-left"
            (click)="toggleTree()"
            [attr.aria-label]="treeCollapsed() ? '展開資料夾樹' : '收合資料夾樹'"
            [title]="treeCollapsed() ? '展開資料夾樹' : '收合資料夾樹'">
            <span aria-hidden="true">{{ treeCollapsed() ? '›' : '‹' }}</span>
          </button>
        }
        <app-browse-grid [mobile]="mobile()" (openFilter)="onOpenFilter()" (opened)="onImageOpened()" />
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
        <app-drawer-panel side="left" [open]="facetDrawerOpen()" title="資料夾" (close)="facetDrawerOpen.set(false)">
          <app-folder-tree-sidebar class="fill" />
        </app-drawer-panel>
        <app-drawer-panel side="right" [open]="inspectorDrawerOpen()" title="圖片詳情" (close)="inspectorDrawerOpen.set(false)">
          <app-inspector class="fill" [photoId]="store.selectedId()" (expand)="openLightbox()" />
        </app-drawer-panel>
      }
    </div>
  `,
  styles: [
    `
      .bview {
        display: grid;
        height: 100vh;
        min-width: 0;
        position: relative;
        transition: grid-template-columns var(--dur-fast) ease;
      }
      @media (prefers-reduced-motion: reduce) {
        .bview {
          transition: none;
        }
      }

      /* 中間圖牆欄:相對定位以容納邊緣 toggle 鈕。
         min-height:0 + height:100% 讓 grid item 被限制在列高,內層 .view 才能取得有界高度而可捲。 */
      .center-stage {
        position: relative;
        min-width: 0;
        min-height: 0;
        height: 100%;
      }

      /* 側欄收合/展開箭頭。 */
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
        font-size: var(--text-body);
        padding: 0;
        transition: background var(--dur-fast), color var(--dur-fast);
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

      /* ③g:投影進抽屜的子元件以 parent 加 class="fill" 觸發其自身 :host(.fill)(填滿 + 自捲),
         改各子元件自管 fill 變體,不再穿透封裝(已移除舊的 deep 選擇器)。 */
    `,
  ],
})
export class BrowseView implements OnInit {
  readonly store = inject(BrowseStore);
  private readonly lightbox = inject(LightboxService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly hostRef = inject(ElementRef<HTMLElement>);
  private readonly stageWidth = useStageWidth(this.hostRef, this.destroyRef);

  readonly treeUserCollapsed = signal<boolean | null>(null);
  readonly treeCollapsed = computed(() =>
    this.treeUserCollapsed() ?? shouldAutoCollapse(this.stageWidth(), FACET_COLLAPSE));
  readonly inspectorUserCollapsed = signal<boolean | null>(null);
  readonly inspectorCollapsed = computed(() =>
    this.inspectorUserCollapsed() ?? shouldAutoCollapse(this.stageWidth(), INSPECTOR_COLLAPSE));
  // ③g 手機抽屜模式:stage 寬 < MOBILE。
  readonly mobile = computed(() => {
    const w = this.stageWidth();
    return w > 0 && w < MOBILE;
  });
  readonly facetDrawerOpen = signal(false);
  readonly inspectorDrawerOpen = signal(false);

  onOpenFilter(): void { this.facetDrawerOpen.set(true); }
  onImageOpened(): void { if (this.mobile()) this.inspectorDrawerOpen.set(true); }

  constructor() {
    effect(() => {
      if (!this.mobile()) {
        this.facetDrawerOpen.set(false);
        this.inspectorDrawerOpen.set(false);
      }
    });
  }

  readonly gridCols = computed(() => {
    if (this.mobile()) return '1fr';
    const t = this.treeCollapsed() ? '0' : '252px';
    const i = this.inspectorCollapsed() ? '0' : '350px';
    return `${t} 1fr ${i}`;
  });

  toggleTree(): void { this.treeUserCollapsed.set(!this.treeCollapsed()); }
  toggleInspector(): void { this.inspectorUserCollapsed.set(!this.inspectorCollapsed()); }

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
    void this.store.loadRoots();
    // 與 gallery 共用 InspectorStore(root 單例);進入 browse 先清掉前一路由殘留的選取。
    this.store.select(null);
    // URL(root/path/q)是單一真相:初次 + 每次變動都套用。
    this.route.queryParams.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      const root = p['root'] != null ? Number(p['root']) : null;
      void this.store.applyUrl(root, (p['path'] as string) ?? '', (p['q'] as string) ?? '');
    });
  }
}
