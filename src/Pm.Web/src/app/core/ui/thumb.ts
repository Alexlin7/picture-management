import { Component, Input, OnChanges, OnDestroy, SimpleChanges, computed, inject, signal } from '@angular/core';
import { PmApi } from '@core/api/pm-api';

type ThumbState = 'loading' | 'loaded' | 'broken';

// 縮圖載入狀態機:撐過掃描中縮圖還沒產生的空窗(skeleton + 指數退避重試),
// 縮圖一生出來就自動補上;真的失敗(壞檔/重試耗盡)才落到靜態「無縮圖」佔位。
// 絕不碰原圖:src 一律走 PmApi.thumbUrl。
@Component({
  selector: 'app-thumb',
  imports: [],
  template: `
    <div class="thumb" [style.aspect-ratio]="aspectRatio">
      @if (state() !== 'broken') {
        <img
          class="img"
          [class.ready]="state() === 'loaded'"
          [src]="src()"
          [alt]="alt"
          loading="lazy"
          (load)="onLoad()"
          (error)="onError()"
        />
      }
      @if (state() === 'loading') {
        <div class="skeleton ph" aria-hidden="true"></div>
      }
      @if (state() === 'broken') {
        <div class="ph broken" aria-hidden="true">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6">
            <rect x="3" y="3" width="18" height="18" rx="2" />
            <circle cx="8.5" cy="8.5" r="1.5" />
            <path d="m21 15-5-5L5 21" />
          </svg>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; width: 100%; }
    .thumb {
      position: relative;
      width: 100%;
      overflow: hidden;
      background: var(--color-raised);
      border-radius: var(--radius-card);
    }
    .img {
      width: 100%;
      height: 100%;
      display: block;
      object-fit: cover;
      opacity: 0;
      transition: opacity 0.2s ease;
    }
    .img.ready { opacity: 1; }
    .ph { position: absolute; inset: 0; }
    .broken {
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--color-faint);
      background: var(--color-raised);
    }
    .broken svg { width: 30%; max-width: 48px; height: auto; }
  `],
})
export class Thumb implements OnChanges, OnDestroy {
  private readonly api = inject(PmApi);

  @Input({ required: true }) photoId!: number;
  @Input() aspectRatio = '1/1';
  @Input() alt = '';

  // 退避序列(ms);耗盡即 broken。初次 + 這 5 次 = 約 10s,覆蓋掃描中縮圖空窗。
  private static readonly RETRY_DELAYS = [400, 800, 1600, 3000, 5000];

  readonly state = signal<ThumbState>('loading');
  private readonly attempt = signal(0);
  private timer: ReturnType<typeof setTimeout> | null = null;

  // 目前 src:第一次無 query;重試帶遞增 cache-bust(?r=n)強制 img 重新載入。
  readonly src = computed(() => {
    const base = this.api.thumbUrl(this.photoId);
    const a = this.attempt();
    return a === 0 ? base : `${base}?r=${a}`;
  });

  ngOnChanges(changes: SimpleChanges): void {
    // 只在 photoId 變動(同 tile 被重用)時重置狀態機;
    // 僅改 aspectRatio/alt 不該讓已載入的圖閃回 skeleton。
    if (changes['photoId']) {
      this.clearTimer();
      this.attempt.set(0);
      this.state.set('loading');
    }
  }

  onLoad(): void {
    this.clearTimer();
    this.state.set('loaded');
  }

  onError(): void {
    const next = this.attempt();
    if (next >= Thumb.RETRY_DELAYS.length) {
      this.state.set('broken');
      return;
    }
    // 維持 loading(skeleton 持續),退避後遞增 attempt → src 改變 → img 重載。
    this.clearTimer(); // 防 error 連續觸發時舊 timer handle 懸空
    this.timer = setTimeout(() => this.attempt.set(next + 1), Thumb.RETRY_DELAYS[next]);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  ngOnDestroy(): void {
    this.clearTimer();
  }
}
