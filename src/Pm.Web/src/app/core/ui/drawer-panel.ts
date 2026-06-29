import { Component, input, output } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';

/**
 * 共用覆蓋式抽屜側板(防打地鼠核心):facet / 資料夾樹 / inspector 共用同一支。
 * scrim + 滑入面板;面板頂部 header(標題 + 關閉 X)永不蓋內容 → ⤢ 等內容按鈕不被疊。
 * open=false 不渲染;關閉後焦點還原由 cdkTrapFocusAutoCapture 負責。
 */
@Component({
  selector: 'app-drawer-panel',
  imports: [A11yModule],
  template: `
    @if (open()) {
      <div class="dp-scrim" (click)="onScrim($event)" (keydown.escape)="close.emit()">
        <div
          class="dp-panel"
          [class.left]="side() === 'left'"
          [class.right]="side() === 'right'"
          cdkTrapFocus
          cdkTrapFocusAutoCapture
          role="dialog"
          aria-modal="true"
          [attr.aria-label]="title()"
        >
          <header class="dp-head">
            <span class="dp-title">{{ title() }}</span>
            <button class="dp-close" type="button" (click)="close.emit()" aria-label="關閉" title="關閉">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true">
                <line x1="6" y1="6" x2="18" y2="18" /><line x1="18" y1="6" x2="6" y2="18" />
              </svg>
            </button>
          </header>
          <div class="dp-body"><ng-content /></div>
        </div>
      </div>
    }
  `,
  styles: [`
    /* scrim 起於 rail 右側(left: --rail-width),讓 rail 在抽屜開啟時仍可見可用,不被遮罩蓋住。 */
    .dp-scrim {
      position: fixed; top: 0; right: 0; bottom: 0; left: var(--rail-width, 0); z-index: 600; display: flex;
      background: rgba(8, 9, 12, 0.6); backdrop-filter: blur(2px);
      animation: dp-fade 0.18s var(--ease-out, ease-out);
    }
    .dp-panel {
      display: flex; flex-direction: column; height: 100%;
      background: var(--color-panel); box-shadow: var(--shadow-3);
    }
    .dp-panel.left {
      margin-right: auto; width: 86vw; max-width: 330px;
      border-right: 1px solid var(--color-hair);
      animation: dp-slide-l 0.2s var(--ease-out, ease-out);
    }
    .dp-panel.right {
      margin-left: auto; width: 92vw; max-width: 360px;
      border-left: 1px solid var(--color-hair);
      animation: dp-slide-r 0.2s var(--ease-out, ease-out);
    }
    .dp-head {
      display: flex; align-items: center; gap: 8px; flex: none;
      min-height: 48px; padding: 8px 10px 8px 14px;
      border-bottom: 1px solid var(--color-hair);
    }
    .dp-title { font-family: var(--font-display); font-weight: 600; font-size: var(--text-title); }
    .dp-close {
      margin-left: auto; width: 44px; height: 44px; display: inline-grid; place-items: center;
      border: 1px solid var(--color-hair); border-radius: var(--radius-soft);
      background: var(--color-raised); color: var(--color-text); cursor: pointer;
      transition: border-color var(--dur-fast, 0.12s) var(--ease-out, ease-out),
                  color var(--dur-fast, 0.12s) var(--ease-out, ease-out);
    }
    .dp-close:hover { border-color: var(--color-danger); color: var(--color-danger); }
    .dp-close svg { width: 18px; height: 18px; }
    .dp-body { flex: 1 1 auto; min-height: 0; overflow: hidden; }
    @keyframes dp-fade { from { opacity: 0; } to { opacity: 1; } }
    @keyframes dp-slide-l { from { transform: translateX(-100%); } to { transform: none; } }
    @keyframes dp-slide-r { from { transform: translateX(100%); } to { transform: none; } }
    @media (prefers-reduced-motion: reduce) {
      .dp-scrim, .dp-panel.left, .dp-panel.right { animation: none; }
      .dp-close { transition: none; }
    }
  `],
})
export class DrawerPanel {
  readonly open = input(false);
  readonly side = input<'left' | 'right'>('left');
  readonly title = input('');
  readonly close = output<void>();

  // 只在點到最外層 scrim(非面板/內容)時關閉。
  onScrim(e: MouseEvent): void {
    if ((e.target as HTMLElement).classList.contains('dp-scrim')) this.close.emit();
  }
}
