import { Component, Injectable, computed, effect, signal } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import type { TagListRow } from '@core/api/pm-api';
import { tagColor, KIND_LABEL } from '@core/tag-color';

export interface MergeResult {
  from: TagListRow; // 被刪除的來源
  to: TagListRow;   // 保留的 canonical
}
interface MergeReq {
  a: TagListRow;
  b: TagListRow;
  resolve: (r: MergeResult | null) => void;
}

// 合併方向對話框:讓使用者選保留哪個 canonical 名稱。回 Promise<MergeResult | null>。
// app 根放一個 <app-merge-dialog-host />。後端 merge 語意為「刪 from、保留 to」。
@Injectable({ providedIn: 'root' })
export class MergeDialogService {
  readonly current = signal<MergeReq | null>(null);

  ask(a: TagListRow, b: TagListRow): Promise<MergeResult | null> {
    // 同一時間只允許一個對話框;有 pending 直接回 null 避免懸掛 Promise。
    if (this.current()) return Promise.resolve(null);
    return new Promise<MergeResult | null>((resolve) => this.current.set({ a, b, resolve }));
  }

  settle(r: MergeResult | null): void {
    const c = this.current();
    if (!c) return;
    this.current.set(null);
    c.resolve(r);
  }
}

@Component({
  selector: 'app-merge-dialog-host',
  imports: [A11yModule],
  template: `
    @if (svc.current(); as c) {
      <div class="md-backdrop" (click)="svc.settle(null)">
        <div
          class="md-box"
          cdkTrapFocus
          cdkTrapFocusAutoCapture
          role="dialog"
          aria-modal="true"
          aria-labelledby="md-title"
          (click)="$event.stopPropagation()"
          (keydown.escape)="svc.settle(null)"
        >
          <div class="md-title" id="md-title">合併標籤</div>
          <div class="md-cards" role="radiogroup" aria-label="選擇保留的標籤">
            @for (t of [c.a, c.b]; track t.id; let i = $index) {
              <button
                type="button"
                class="md-card"
                [class.keep]="keepIndex() === i"
                role="radio"
                [attr.aria-checked]="keepIndex() === i"
                (click)="keepIndex.set(i)"
              >
                <span class="md-name">
                  <span class="md-dot" [style.background]="color(t.kind)"></span>
                  {{ t.name }}
                </span>
                <span class="md-meta">{{ kindLabel(t.kind) }} · {{ t.count }} 張</span>
                <span class="md-state">{{ keepIndex() === i ? '保留' : '刪除' }}</span>
              </button>
            }
          </div>
          <div class="md-summary">
            保留「{{ kept().name }}」,刪除「{{ dropped().name }}」(它的 {{ dropped().count }} 張改掛到「{{ kept().name }}」)。
          </div>
          <div class="md-acts">
            <button class="md-btn ghost" type="button" (click)="svc.settle(null)">取消</button>
            <button class="md-btn" type="button" (click)="confirm()">合併</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .md-backdrop {
      position: fixed;
      inset: 0;
      z-index: 1100;
      display: grid;
      place-items: center;
      background: rgba(0, 0, 0, 0.55);
      animation: md-fade 0.12s ease-out;
    }
    .md-box {
      width: min(460px, calc(100vw - 32px));
      padding: 18px 18px 14px;
      border-radius: 12px;
      background: var(--color-panel, #1b1b1f);
      border: 1px solid var(--color-hair, #333);
      box-shadow: 0 18px 50px -12px rgba(0, 0, 0, 0.7);
      color: var(--color-text);
    }
    .md-title { font-size: var(--text-title); font-weight: 600; margin-bottom: 14px; }
    .md-cards { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
    .md-card {
      display: flex;
      flex-direction: column;
      gap: 6px;
      padding: 12px;
      text-align: left;
      border-radius: 9px;
      border: 1px solid var(--color-hair, #333);
      background: var(--color-raised, #2a2a30);
      color: var(--color-muted, #bbb);
      cursor: pointer;
    }
    .md-card.keep {
      border-color: var(--color-accent, #22D3EE);
      color: var(--color-text);
    }
    .md-name { display: flex; align-items: center; gap: 7px; font-size: var(--text-title); font-weight: 600; }
    .md-dot { width: 9px; height: 9px; border-radius: 50%; flex: 0 0 auto; }
    .md-meta { font-size: var(--text-sm); }
    .md-state { font-size: var(--text-sm); font-weight: 600; }
    .md-card.keep .md-state { color: var(--color-accent, #22D3EE); }
    .md-summary { font-size: var(--text-sm); line-height: 1.5; color: var(--color-muted, #bbb); margin-top: 14px; }
    .md-acts { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; }
    .md-btn {
      font-size: var(--text-body);
      padding: 7px 16px;
      border-radius: 7px;
      border: 1px solid var(--color-hair, #333);
      background: var(--color-raised, #2a2a30);
      color: var(--color-text);
      cursor: pointer;
    }
    .md-btn.ghost { background: transparent; color: var(--color-muted, #bbb); }
    .md-btn.ghost:hover { color: var(--color-text); }
    @keyframes md-fade { from { opacity: 0; } to { opacity: 1; } }
  `],
})
export class MergeDialogHost {
  // 預設保留使用數多者(與舊邏輯一致);count 相等則保留第二個(b)。0=保留 a,1=保留 b。
  readonly keepIndex = signal(0);
  readonly color = tagColor;
  readonly kindLabel = (k: string): string => (KIND_LABEL as Record<string, string>)[k] ?? k;

  readonly kept = computed<TagListRow>(() => {
    const c = this.svc.current()!;
    return this.keepIndex() === 0 ? c.a : c.b;
  });
  readonly dropped = computed<TagListRow>(() => {
    const c = this.svc.current()!;
    return this.keepIndex() === 0 ? c.b : c.a;
  });

  constructor(readonly svc: MergeDialogService) {
    // 每次開新對話框,依 count 設預設保留方向(保留多者;相等保留 b)。
    effect(() => {
      const c = this.svc.current();
      if (c) this.keepIndex.set(c.a.count > c.b.count ? 0 : 1);
      else this.keepIndex.set(0);
    });
  }

  confirm(): void {
    this.svc.settle({ from: this.dropped(), to: this.kept() });
  }
}
