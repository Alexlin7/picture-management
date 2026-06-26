// src/Pm.Web/src/app/core/ui/masonry.ts
import {
  Component, ContentChild, DestroyRef, ElementRef, Input, TemplateRef, computed, inject,
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
      @for (item of items; track $index) {
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

  @Input() items: unknown[] = [];
  @Input() aspect: (item: unknown) => number = () => 1;
  @Input() minColWidth = 180;
  @Input() gap = 12;

  @ContentChild(TemplateRef) tpl!: TemplateRef<unknown>;

  // useStageWidth 內掛 ResizeObserver,回唯讀寬度 signal;直接驅動 layout computed(無 rAF 自迴圈)。
  private readonly width = useStageWidth(this.hostRef, this.destroyRef);
  readonly layout = computed(() =>
    computeMasonryLayout(this.width(), this.items.map((i) => this.aspect(i)), this.minColWidth, this.gap));
  readonly cols = computed(() => this.layout().cols);
}
