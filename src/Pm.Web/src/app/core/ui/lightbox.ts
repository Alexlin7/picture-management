import { Component, Injectable, computed, effect, inject, signal } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import { PmApi, type PhotoDetail } from '@core/api/pm-api';

/**
 * Lightbox 來源契約:由開啟方(gallery-view / browse-view)以自己的 store 提供。
 * lightbox 不直接耦合任何 store —— 只透過這些 reactive getter 取值。
 */
export interface LightboxSource {
  ids: () => number[];          // 目前載入的 photo id 順序(隨無限捲增長)
  total: () => number;          // 命中總數(計數顯示用)
  startId: number;              // 開啟時的目前 photo id
  loadMore?: () => void;        // 走到結尾時補載下一頁(冪等;由呼叫端守衛)
  select?: (id: number) => void; // 換圖時同步外部選取(inspector 跟著走)
}

// Lightbox 大圖檢視服務。app 根放一個 <app-lightbox-host />。
@Injectable({ providedIn: 'root' })
export class LightboxService {
  readonly source = signal<LightboxSource | null>(null);

  open(source: LightboxSource): void { this.source.set(source); }
  close(): void { this.source.set(null); }
}

@Component({
  selector: 'app-lightbox-host',
  imports: [A11yModule],
  template: `
    @if (svc.source(); as src) {
      <div class="lb" cdkTrapFocus cdkTrapFocusAutoCapture role="dialog" aria-modal="true"
           [attr.aria-label]="'圖片檢視 第 ' + (index() + 1) + ' 張,共 ' + src.total() + ' 張'"
           (click)="onBackdrop($event)"
           (keydown.escape)="svc.close()"
           (keydown.arrowleft)="prev()"
           (keydown.arrowright)="next()">

        <!-- 左上:計數 -->
        <div class="lb-meta">
          <span class="count" data-testid="lightbox-count">{{ index() + 1 }} / {{ fmt(src.total()) }}</span>
        </div>

        <!-- 右上:下載 + 關閉 -->
        <div class="lb-tools">
          <a class="iconbtn" [href]="downloadUrl()" [attr.download]="fileName()" aria-label="下載原圖" title="下載原圖">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="7 10 12 15 17 10" /><line x1="12" y1="15" x2="12" y2="3" />
            </svg>
          </a>
          <button class="iconbtn close" type="button" (click)="svc.close()" aria-label="關閉(Esc)" title="關閉(Esc)">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true">
              <line x1="6" y1="6" x2="18" y2="18" /><line x1="18" y1="6" x2="6" y2="18" />
            </svg>
          </button>
        </div>

        <!-- 左右換圖 -->
        <button class="navbtn prev" type="button" (click)="prev()" [disabled]="index() === 0" aria-label="上一張(←)">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6" /></svg>
        </button>
        <button class="navbtn next" type="button" (click)="next()" [disabled]="atEnd()" aria-label="下一張(→)">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6" /></svg>
        </button>

        <!-- 圖片 + 字幕 -->
        <figure class="lb-figure">
          @if (currentId(); as cid) {
            <img class="lb-img" [src]="imgUrl()" [alt]="fileName()" />
          }
          <figcaption class="lb-caption">
            <span class="name">{{ fileName() }}</span>
            @if (dims()) { <span class="dim">{{ dims() }}</span> }
            <button class="insp" type="button" (click)="svc.close()">在側欄看詳情</button>
          </figcaption>
        </figure>
      </div>
    }
  `,
  styles: [`
    .lb { position: fixed; inset: 0; z-index: 1200; display: grid; place-items: center;
      background: rgba(8, 9, 12, 0.92); backdrop-filter: blur(8px); animation: lb-fade 0.2s var(--ease-out, ease-out); }
    @keyframes lb-fade { from { opacity: 0; } to { opacity: 1; } }
    .lb-figure { margin: 0; max-width: 92vw; max-height: 88vh; display: flex; flex-direction: column;
      align-items: center; animation: lb-pop 0.2s var(--ease-out, ease-out); }
    @keyframes lb-pop { from { transform: scale(0.965); opacity: 0; } to { transform: scale(1); opacity: 1; } }
    .lb-img { max-width: 92vw; max-height: 82vh; object-fit: contain; border-radius: var(--radius-soft, 7px);
      box-shadow: 0 24px 70px -20px rgba(0, 0, 0, 0.85); background: var(--color-raised, #23272f); }
    .lb-caption { margin-top: 12px; display: flex; align-items: center; gap: 14px; font-size: var(--text-sm);
      color: var(--color-muted, #959ba7); max-width: 92vw; }
    .lb-caption .name { color: var(--color-text, #e8eaed); font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lb-caption .dim { font-family: var(--font-mono, monospace); font-size: var(--text-xs); color: var(--color-faint, #6b7280); }
    .lb-caption .insp { margin-left: auto; color: var(--color-accent, #22d3ee); cursor: pointer; font-size: var(--text-sm);
      background: none; border: 0; padding: 4px 6px; border-radius: var(--radius-soft, 7px); }
    .lb-caption .insp:hover { background: rgba(34, 211, 238, 0.1); }
    .lb-meta { position: fixed; top: 22px; left: 20px; color: var(--color-muted, #959ba7); font-size: var(--text-body); }
    .lb-meta .count { font-family: var(--font-mono, monospace); color: var(--color-text, #e8eaed); }
    .lb-tools { position: fixed; top: 16px; right: 18px; display: flex; align-items: center; gap: 8px; }
    .iconbtn { width: 44px; height: 44px; display: inline-grid; place-items: center; border-radius: 50%;
      background: rgba(35, 39, 47, 0.7); border: 1px solid var(--color-hair-strong, #3b4150);
      color: var(--color-text, #e8eaed); cursor: pointer;
      transition: background var(--dur-fast) var(--ease-out, ease-out), border-color var(--dur-fast) var(--ease-out, ease-out); }
    .iconbtn:hover { background: var(--color-raised-2, #2a2f39); border-color: var(--color-accent, #22d3ee); }
    .iconbtn:active { filter: brightness(0.9); }
    .iconbtn svg { width: 20px; height: 20px; }
    .iconbtn.close:hover { border-color: var(--color-danger); color: var(--color-danger); }
    .navbtn { position: fixed; top: 50%; transform: translateY(-50%); width: 52px; height: 52px;
      display: inline-grid; place-items: center; border-radius: 50%; background: rgba(35, 39, 47, 0.55);
      border: 1px solid var(--color-hair-strong, #3b4150); color: var(--color-text, #e8eaed); cursor: pointer;
      transition: background var(--dur-fast) var(--ease-out, ease-out), border-color var(--dur-fast) var(--ease-out, ease-out); }
    .navbtn:hover { background: var(--color-raised-2, #2a2f39); border-color: var(--color-accent, #22d3ee); }
    .navbtn.prev { left: 20px; } .navbtn.next { right: 20px; }
    .navbtn svg { width: 26px; height: 26px; }
    .navbtn:disabled { opacity: 0.3; cursor: default; pointer-events: none; }
    @media (prefers-reduced-motion: reduce) { .lb, .lb-figure { animation: none; }
      .iconbtn, .navbtn, .lb-caption .insp { transition: none; } }
    @media (max-width: 640px) {
      .lb-img { max-width: 92vw; }
      .navbtn { width: 44px; height: 44px; } .navbtn.prev { left: 8px; } .navbtn.next { right: 8px; }
      .lb-meta { top: 14px; left: 12px; font-size: var(--text-sm); } .lb-tools { top: 12px; right: 12px; }
      .lb-caption { font-size: var(--text-sm); gap: 10px; padding: 0 10px; }
    }
  `],
})
export class LightboxHost {
  private readonly api = inject(PmApi);
  readonly index = signal(0);
  private readonly detail = signal<PhotoDetail | null>(null);

  readonly currentId = computed<number | null>(() => {
    const src = this.svc.source();
    if (!src) return null;
    const ids = src.ids();
    return ids[this.index()] ?? src.startId;
  });
  readonly imgUrl = computed(() => {
    const id = this.currentId();
    return id == null ? '' : this.api.fileUrl(id);
  });
  readonly downloadUrl = computed(() => {
    const id = this.currentId();
    return id == null ? '' : this.api.fileUrl(id, true);
  });
  readonly atEnd = computed(() => {
    const src = this.svc.source();
    return !src || this.index() >= src.ids().length - 1;
  });

  // 字幕:檔名(取首位置 relPath 的檔名,無則 hash)+ 尺寸。
  readonly fileName = computed(() => {
    const d = this.detail();
    if (!d) return '';
    const rel = d.locations[0]?.relPath;
    if (!rel) return d.fileHash.slice(0, 12);
    const parts = rel.split(/[\\/]/);
    return parts[parts.length - 1] || rel;
  });
  readonly dims = computed(() => {
    const d = this.detail();
    return d?.width && d?.height ? `${d.width} × ${d.height}` : '';
  });
  readonly fmt = (n: number): string => n.toLocaleString('en-US');

  constructor(readonly svc: LightboxService) {
    // 開啟時把 index 對到 startId;來源清掉時重置。
    effect(() => {
      const src = this.svc.source();
      if (!src) { this.detail.set(null); return; }
      const i = src.ids().indexOf(src.startId);
      this.index.set(i >= 0 ? i : 0);
    });
    // 目前圖變動 → 載入 detail 供字幕用(競態以 id 比對)。
    effect(() => {
      const id = this.currentId();
      if (id == null) return;
      void this.api.photo(id).then((d) => { if (this.currentId() === id) this.detail.set(d); }).catch(() => {});
    });
  }

  private setIndex(i: number): void {
    const src = this.svc.source();
    if (!src) return;
    this.index.set(i);
    src.select?.(src.ids()[i]);
  }

  prev(): void {
    if (this.index() > 0) this.setIndex(this.index() - 1);
  }
  next(): void {
    const src = this.svc.source();
    if (!src) return;
    if (this.index() < src.ids().length - 1) this.setIndex(this.index() + 1);
    else src.loadMore?.();   // 到尾補載;載到後 ids() 增長,再按一次即可前進
  }

  onBackdrop(e: MouseEvent): void {
    // 只在點到最外層遮罩(非圖片/按鈕)時關閉。
    if ((e.target as HTMLElement).classList.contains('lb')) this.svc.close();
  }
}
