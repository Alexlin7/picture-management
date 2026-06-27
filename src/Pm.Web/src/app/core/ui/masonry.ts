// src/Pm.Web/src/app/core/ui/masonry.ts
import {
  Component, ContentChild, DestroyRef, ElementRef, TemplateRef, computed, inject, input,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { computeMasonryLayout } from '../masonry-layout';
import { useStageWidth } from '../use-stage-width';

@Component({
  selector: 'app-masonry',
  standalone: true,
  imports: [NgTemplateOutlet],
  template: `
    <div class="m-root" [style.height.px]="layout().containerHeight">
      @for (item of items(); track $index) {
        <div class="m-item"
          [style.left.px]="layout().boxes[$index]?.left ?? 0"
          [style.top.px]="layout().boxes[$index]?.top ?? 0"
          [style.width.px]="layout().boxes[$index]?.width ?? 0">
          <ng-container *ngTemplateOutlet="tpl; context: { $implicit: item, index: $index }" />
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

  @ContentChild(TemplateRef) tpl!: TemplateRef<unknown>;

  // useStageWidth 內掛 ResizeObserver,回唯讀寬度 signal;直接驅動 layout computed(無 rAF 自迴圈)。
  private readonly width = useStageWidth(this.hostRef, this.destroyRef);
  readonly layout = computed(() =>
    computeMasonryLayout(this.width(), this.items().map((i) => this.aspect()(i)), this.minColWidth(), this.gap()));
  readonly cols = computed(() => this.layout().cols);
}
