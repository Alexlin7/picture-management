// src/Pm.Web/src/app/core/ui/masonry.ts
import {
  Component, ContentChild, DestroyRef, ElementRef, TemplateRef, computed, effect, inject, input, signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { computeMasonryLayout, isBoxInWindow, type MasonryBox } from '../masonry-layout';
import { useStageWidth } from '../use-stage-width';

@Component({
  selector: 'app-masonry',
  standalone: true,
  imports: [NgTemplateOutlet],
  template: `
    <div class="m-root" [style.height.px]="layout().containerHeight">
      @for (entry of visibleItems(); track entry.i) {
        <div class="m-item"
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
  }
}
