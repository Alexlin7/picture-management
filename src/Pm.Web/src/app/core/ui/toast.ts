import { Component, Injectable, signal } from '@angular/core';

export type ToastKind = 'info' | 'success' | 'error';
export interface Toast {
  id: number;
  text: string;
  kind: ToastKind;
}

// 輕量 toast(無外部依賴,signal-based)。app 根放一個 <app-toast-host />。
@Injectable({ providedIn: 'root' })
export class ToastService {
  private seq = 0;
  readonly toasts = signal<Toast[]>([]);

  show(text: string, kind: ToastKind = 'info', ms = 3200): void {
    const id = ++this.seq;
    this.toasts.update((t) => [...t, { id, text, kind }]);
    if (ms > 0) setTimeout(() => this.dismiss(id), ms);
  }
  info(text: string): void { this.show(text, 'info'); }
  success(text: string): void { this.show(text, 'success'); }
  error(text: string): void { this.show(text, 'error', 5000); }

  dismiss(id: number): void {
    this.toasts.update((t) => t.filter((x) => x.id !== id));
  }
}

// 固定定位於右上的 toast 容器。
@Component({
  selector: 'app-toast-host',
  imports: [],
  template: `
    <div class="toast-wrap" role="status" aria-live="polite">
      @for (t of svc.toasts(); track t.id) {
        <div class="toast" [class.success]="t.kind === 'success'" [class.error]="t.kind === 'error'">
          <span class="msg">{{ t.text }}</span>
          <button class="x" type="button" (click)="svc.dismiss(t.id)" aria-label="關閉">×</button>
        </div>
      }
    </div>
  `,
  styles: [`
    .toast-wrap {
      position: fixed;
      top: 16px;
      right: 16px;
      z-index: 1000;
      display: flex;
      flex-direction: column;
      gap: 8px;
      pointer-events: none;
    }
    .toast {
      pointer-events: auto;
      display: flex;
      align-items: center;
      gap: 10px;
      min-width: 220px;
      max-width: 360px;
      padding: 10px 12px;
      border-radius: 8px;
      font-size: 13px;
      color: var(--color-text);
      background: var(--color-panel);
      border: 1px solid var(--color-hair);
      border-left: 3px solid var(--color-muted);
      box-shadow: 0 8px 24px -6px rgba(0, 0, 0, 0.5);
      animation: toast-in 0.15s ease-out;
    }
    .toast.success { border-left-color: var(--color-success); }
    .toast.error { border-left-color: #F0616D; }
    .toast .msg { flex: 1; min-width: 0; }
    .toast .x {
      flex: none;
      border: 0;
      background: transparent;
      color: inherit;
      opacity: 0.55;
      cursor: pointer;
      font-size: 15px;
      line-height: 1;
      padding: 0;
    }
    .toast .x:hover { opacity: 1; }
    @keyframes toast-in {
      from { opacity: 0; transform: translateY(-6px); }
      to { opacity: 1; transform: translateY(0); }
    }
  `],
})
export class ToastHost {
  constructor(readonly svc: ToastService) {}
}
