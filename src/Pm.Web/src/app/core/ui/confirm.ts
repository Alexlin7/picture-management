import { Component, Injectable, signal } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';

export interface ConfirmOpts {
  title?: string;
  confirmText?: string;
  cancelText?: string;
  danger?: boolean;
}
interface ConfirmReq extends ConfirmOpts {
  message: string;
  resolve: (ok: boolean) => void;
}

// confirm dialog(取代原生 confirm):回 Promise<boolean>。app 根放一個 <app-confirm-host />。
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  readonly current = signal<ConfirmReq | null>(null);

  ask(message: string, opts: ConfirmOpts = {}): Promise<boolean> {
    return new Promise<boolean>((resolve) => this.current.set({ message, ...opts, resolve }));
  }

  settle(ok: boolean): void {
    const c = this.current();
    if (!c) return;
    this.current.set(null);
    c.resolve(ok);
  }
}

// backdrop + 對話框;CDK cdkTrapFocus 鎖焦點,Esc / 點背景 = 取消。
@Component({
  selector: 'app-confirm-host',
  imports: [A11yModule],
  template: `
    @if (svc.current(); as c) {
      <div class="cf-backdrop" (click)="svc.settle(false)">
        <div
          class="cf-box"
          cdkTrapFocus
          cdkTrapFocusAutoCapture
          role="alertdialog"
          aria-modal="true"
          (click)="$event.stopPropagation()"
          (keydown.escape)="svc.settle(false)"
        >
          @if (c.title) { <div class="cf-title">{{ c.title }}</div> }
          <div class="cf-msg">{{ c.message }}</div>
          <div class="cf-acts">
            <button class="cf-btn ghost" type="button" (click)="svc.settle(false)">
              {{ c.cancelText || '取消' }}
            </button>
            <button class="cf-btn" type="button" [class.danger]="c.danger" (click)="svc.settle(true)">
              {{ c.confirmText || '確定' }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .cf-backdrop {
      position: fixed;
      inset: 0;
      z-index: 1100;
      display: grid;
      place-items: center;
      background: rgba(0, 0, 0, 0.55);
      animation: cf-fade 0.12s ease-out;
    }
    .cf-box {
      width: min(420px, calc(100vw - 32px));
      padding: 18px 18px 14px;
      border-radius: 12px;
      background: var(--color-panel, #1b1b1f);
      border: 1px solid var(--color-hair, #333);
      box-shadow: 0 18px 50px -12px rgba(0, 0, 0, 0.7);
      color: var(--color-ink, #e8e8ea);
    }
    .cf-title { font-size: 15px; font-weight: 600; margin-bottom: 8px; }
    .cf-msg { font-size: 13px; line-height: 1.5; color: var(--color-muted, #bbb); }
    .cf-acts { display: flex; justify-content: flex-end; gap: 8px; margin-top: 18px; }
    .cf-btn {
      font-size: 13px;
      padding: 7px 16px;
      border-radius: 7px;
      border: 1px solid var(--color-hair, #333);
      background: var(--color-raised, #2a2a30);
      color: var(--color-ink, #e8e8ea);
      cursor: pointer;
    }
    .cf-btn.ghost { background: transparent; color: var(--color-muted, #bbb); }
    .cf-btn.ghost:hover { color: var(--color-ink, #e8e8ea); }
    .cf-btn.danger { background: #F0616D; border-color: #F0616D; color: #fff; }
    @keyframes cf-fade { from { opacity: 0; } to { opacity: 1; } }
  `],
})
export class ConfirmHost {
  constructor(readonly svc: ConfirmService) {}
}
