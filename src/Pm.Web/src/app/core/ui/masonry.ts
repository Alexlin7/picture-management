// src/Pm.Web/src/app/core/ui/masonry.ts
import {
  Component, ContentChild, DestroyRef, ElementRef, TemplateRef, computed, effect, inject, input, output, signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { computeMasonryLayout, isBoxInWindow, gridNavTarget, type MasonryBox, type GridNavDir } from '../masonry-layout';
import { useStageWidth } from '../use-stage-width';

@Component({
  selector: 'app-masonry',
  standalone: true,
  imports: [NgTemplateOutlet],
  template: `
    <div class="m-root" [style.height.px]="layout().containerHeight">
      @for (entry of visibleItems(); track entry.i) {
        <div class="m-item"
          data-testid="masonry-item"
          [class.roving]="roving()"
          [attr.data-i]="entry.i"
          [attr.role]="roving() ? 'button' : null"
          [attr.tabindex]="roving() ? (entry.i === activeIndex() ? 0 : -1) : null"
          (click)="onCellClick(entry.i)"
          (keydown)="onCellKeydown($event, entry.i)"
          [style.left.px]="entry.box?.left ?? 0"
          [style.top.px]="entry.box?.top ?? 0"
          [style.width.px]="entry.box?.width ?? 0">
          <ng-container *ngTemplateOutlet="tpl; context: { $implicit: entry.item, index: entry.i }" />
        </div>
      }
    </div>`,
  styles: [`
    .m-root { position: relative; width: 100%; }
    .m-item { position: absolute; }
    /* roving 模式:m-item 即可聚焦格(role=button),焦點環走全域 :focus-visible,
       對齊 tile 圓角讓 ring 不是直角。 */
    .m-item.roving { border-radius: var(--radius-card); cursor: pointer; }
  `],
})
export class Masonry {
  private readonly hostRef = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  // Signal inputs:computed 可正確追蹤全部四個輸入 → items/aspect/minColWidth/gap 任一變動
  // 皆觸發 layout 重算。原 @Input() 版本只追蹤 width(),導致 append/切視圖時 layout 不重算。
  items = input<unknown[]>([]);
  aspect = input<(item: unknown) => number>(() => 1);
  minColWidth = input(180);
  gap = input(12);

  // virtual scroll:傳入可捲動容器(消費端的 .view)即啟用 windowing,只渲染視窗 ± overscan 的 tile。
  // 不傳(null)→ fallback 全渲染(保持向後相容 + 測試/SSR 安全)。
  scrollEl = input<HTMLElement | null>(null);
  overscan = input(600); // px,與消費端 IntersectionObserver rootMargin 一致

  // roving tabindex 鍵盤格線導航:啟用後整個圖牆是「單一 Tab 停駐點」,
  // 進去後方向鍵在 tile 間移動(依版面幾何)、Enter/Space 觸發 activate。
  // 不啟用(預設)→ m-item 不可聚焦、不攔鍵盤,行為與原本完全相同。
  roving = input(false);
  // 使用者「觸發」某格(Enter/Space/click):回拋 item 與 index 給消費端(原本的 pick)。
  readonly activate = output<{ item: unknown; index: number }>();
  // 鍵盤往下/往右導航到接近結尾時發出:消費端據此載下一頁(純滾輪靠 sentinel IO,
  // 但方向鍵停在最後一格不會觸發 IO,故另發此事件補上)。
  readonly loadMore = output<void>();
  // 目前焦點格 index(roving 用)。items 變動時 clamp 在合法範圍。
  readonly activeIndex = signal(0);

  @ContentChild(TemplateRef) tpl!: TemplateRef<unknown>;

  // useStageWidth 內掛 ResizeObserver,回唯讀寬度 signal;直接驅動 layout computed(無 rAF 自迴圈)。
  private readonly width = useStageWidth(this.hostRef, this.destroyRef);
  readonly layout = computed(() =>
    computeMasonryLayout(this.width(), this.items().map((i) => this.aspect()(i)), this.minColWidth(), this.gap()));
  readonly cols = computed(() => this.layout().cols);

  private readonly scrollTop = signal(0);
  private readonly vpHeight = signal(0);

  // 只渲染落在視窗 ± overscan 的 tile。scrollEl 未設或視窗高未量到 → 全渲染(fallback)。
  readonly visibleItems = computed<{ item: unknown; box: MasonryBox | undefined; i: number }[]>(() => {
    const boxes = this.layout().boxes;
    const all = this.items().map((item, i) => ({ item, box: boxes[i] as MasonryBox | undefined, i }));
    if (!this.scrollEl()) return all;
    const vh = this.vpHeight();
    if (vh <= 0) return all;
    const st = this.scrollTop();
    const over = this.overscan();
    return all.filter((e) => e.box && isBoxInWindow(e.box, st, vh, over));
  });

  constructor() {
    effect((onCleanup) => {
      const el = this.scrollEl();
      if (!el) return;
      this.vpHeight.set(el.clientHeight || 0);   // 同步初始化,避免首幀空白
      const onScroll = () => requestAnimationFrame(() => this.scrollTop.set(el.scrollTop));
      const ro = new ResizeObserver(() => this.vpHeight.set(el.clientHeight));
      el.addEventListener('scroll', onScroll, { passive: true });
      ro.observe(el);
      onCleanup(() => {
        el.removeEventListener('scroll', onScroll);
        ro.disconnect();
      });
    });

    // items 變短時把 activeIndex clamp 回合法範圍(避免指向已不存在的格)。
    effect(() => {
      const n = this.items().length;
      if (n > 0 && this.activeIndex() > n - 1) this.activeIndex.set(n - 1);
    });
  }

  // ── roving tabindex 鍵盤導航 ──
  onCellClick(i: number): void {
    if (!this.roving()) return;            // 非 roving:不攔,維持原行為
    this.activeIndex.set(i);
    this.activate.emit({ item: this.items()[i], index: i });
  }

  onCellKeydown(e: KeyboardEvent, i: number): void {
    if (!this.roving()) return;
    const dirs: Record<string, GridNavDir> = {
      ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'up', ArrowDown: 'down',
    };
    if (e.key in dirs) {
      e.preventDefault();
      this.moveActive(dirs[e.key]);
    } else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      this.activate.emit({ item: this.items()[i], index: i });
    } else if (e.key === 'Home') {
      e.preventDefault();
      this.setActiveAndFocus(0);
    } else if (e.key === 'End') {
      e.preventDefault();
      this.setActiveAndFocus(this.items().length - 1);
    }
  }

  private moveActive(dir: GridNavDir): void {
    const next = gridNavTarget(this.layout().boxes, this.activeIndex(), dir);
    if (next !== this.activeIndex()) this.setActiveAndFocus(next);
    // 往下/往右走到最後一列附近(含已卡在最後一格不動)→ 請消費端載下一頁。
    // 純滾輪有 sentinel IO,但方向鍵停在結尾不會觸發 IO,故在此補發。
    if (dir === 'down' || dir === 'right') {
      const cols = this.cols() || 1;
      if (this.items().length - 1 - next <= cols) this.loadMore.emit();
    }
  }

  // 設定焦點格 + 必要時捲入視窗(windowing 才會 render 它)+ 下一幀聚焦該格。
  private setActiveAndFocus(i: number): void {
    if (i < 0 || i >= this.items().length) return;
    this.activeIndex.set(i);
    const box = this.layout().boxes[i];
    const el = this.scrollEl();
    if (el && box) {
      const top = box.top;
      const bottom = box.top + box.height;
      if (top < el.scrollTop) el.scrollTop = top - 12;
      else if (bottom > el.scrollTop + el.clientHeight) el.scrollTop = bottom - el.clientHeight + 12;
      this.scrollTop.set(el.scrollTop);   // 同步驅動 windowing 重算,確保目標格被渲染
    }
    requestAnimationFrame(() => {
      const cell = this.hostRef.nativeElement.querySelector(`.m-item[data-i="${i}"]`) as HTMLElement | null;
      cell?.focus();
    });
  }
}
