import { Component, computed, inject, signal, input, output, ElementRef, ViewChild, AfterViewInit, OnDestroy } from '@angular/core';
import { tagColor, DANGER, tint } from '@core/tag-color';
import { PmApi, type PhotoListItem, type TagListRow } from '@core/api/pm-api';
import { GalleryStore, type SearchToken } from '../gallery.store';
import { normalizeTagQuery, exactMatch, excludeSelected } from '@core/tag-search';
import { displayOf } from '@core/tag-display';
import { ToastService } from '@core/ui/toast';
import { ConfirmService } from '@core/ui/confirm';
import { Thumb } from '@core/ui/thumb';
import { Masonry } from '../../../core/ui/masonry';
import { MIN_COL_WIDTH, MASONRY_GAP } from '../../../core/layout-breakpoints';

// 契約:頂欄 token 搜尋列 + masonry 圖牆。點 tile → 寫入 store 選取。
@Component({
  selector: 'app-photo-grid',
  imports: [Thumb, Masonry],
  templateUrl: './photo-grid.html',
  styleUrl: './photo-grid.css',
})
export class PhotoGrid implements AfterViewInit, OnDestroy {
  private readonly store = inject(GalleryStore);
  private readonly api = inject(PmApi);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  // 資料來源:store(來自 PmApi)
  readonly photos = this.store.photos;
  readonly hitCount = this.store.hitCount;
  readonly wd14Queue = this.store.wd14Queue;
  readonly loading = this.store.loading;
  readonly error = this.store.error;
  readonly hasMore = this.store.hasMore;

  // 頂欄目前搜尋 token(可 ×)
  readonly tokens = this.store.tokens;

  // 選取狀態(讀 store)
  readonly selectedId = this.store.selectedId;
  readonly selCount = computed(() => (this.selectedId() === null ? 0 : 1));
  // 選取格在目前載入清單中的 index,餵給 Masonry 設 aria-pressed(無選取 → -1)。
  readonly selectedIndex = computed(() => {
    const id = this.selectedId();
    return id === null ? -1 : this.photos().findIndex((p) => p.id === id);
  });

  // 兩段式檢視切換:dense=小圖密集 / large=大圖(純視覺本地狀態)
  readonly viewMode = signal<'dense' | 'large'>('dense');

  // ③g:由 gallery-view 傳入是否手機抽屜模式;true → 顯示「篩選」鈕。
  readonly mobile = input(false);
  // 點「篩選」鈕 → 請上層開左抽屜(facet)。
  readonly openFilter = output<void>();
  // 點圖 / 鍵盤選取 → 請上層開右抽屜(inspector)。同圖重點也會 emit,故能重開。
  readonly opened = output<void>();

  // 窄寬(手機)toolbar 溢出選單「⋯ 更多」開合:收次要操作(模型佇列狀態 + 重標失敗),
  // 桌面 inline 顯示故此選單僅 @media 窄寬出現。點外部關閉走透明 backdrop,不掛 document listener。
  readonly moreOpen = signal(false);
  toggleMore(ev: Event): void {
    ev.stopPropagation();
    this.moreOpen.update((v) => !v);
  }
  // 選單內按「重標失敗」:先關選單再走原 confirm 流程,避免 confirm 對話框後選單仍開著。
  moreRequeue(): void {
    this.moreOpen.set(false);
    this.requeueFailed();
  }
  // 選單內按「重標查詢」:同上,先關選單。
  moreRequeueQuery(): void {
    this.moreOpen.set(false);
    this.requeueByQuery();
  }

  // masonry 所需
  readonly gap = MASONRY_GAP;
  aspectNum = (p: unknown): number => {
    const photo = p as PhotoListItem;
    return photo.width && photo.height ? photo.width / photo.height : 1;
  };
  minColWidth(): number {
    const m = this.viewMode();
    return m === 'dense' ? MIN_COL_WIDTH.dense : m === 'large' ? MIN_COL_WIDTH.large : MIN_COL_WIDTH.standard;
  }

  // 千分位
  readonly hitCountText = computed(() => this.hitCount().toLocaleString('en-US'));
  readonly wd14QueueText = computed(() => this.wd14Queue().toLocaleString('en-US'));

  // tile 用真實長寬比預留空間(無尺寸退 1:1):避免變形,且載入前先佔位不跳動。
  aspect(p: PhotoListItem): string {
    return p.width && p.height ? `${p.width}/${p.height}` : '1/1';
  }

  // kind → 顏色(共用分色 helper)
  kindColor = tagColor;

  // masonry 圖格(role=button)可及名稱:序位名,讓 SR 不只念「button」。
  readonly tileLabel = (_: unknown, i: number): string => `圖片 ${i + 1}`;

  // 移除頂欄 token
  removeToken(idx: number, ev: Event): void {
    ev.stopPropagation();
    this.store.removeToken(idx);
  }

  // 點 token chip(非 ×)→ 切換 排除/包含。
  toggleToken(idx: number, ev: Event): void {
    ev.stopPropagation();
    this.store.toggleToken(idx);
  }

  // 下拉建議的顯示文字:中文顯示名 + 角色作品(displayOf);退回底線轉空白。
  sugLabel(s: TagListRow): string {
    const d = displayOf({ name: s.name, kind: s.kind });
    return d.work ? `${d.label} 〔${d.work}〕` : d.label;
  }

  // token 膠囊樣式(分色,半透明底);排除 token(-x)用 DANGER 紅 + 刪除線區分。
  tokenStyle(t: SearchToken): Record<string, string> {
    const excl = t.text.startsWith('-');
    const c = excl ? DANGER : this.kindColor(t.kind);
    const base = {
      color: c,
      background: this.rgba(c, 0.12),
      'border-color': this.rgba(c, 0.3),
    };
    return excl ? { ...base, 'text-decoration': 'line-through' } : base;
  }

  // 點 tile → 寫入 store 選取
  pick(p: PhotoListItem): void {
    this.store.select(p.id);
  }

  // masonry roving 導航:click / Enter / Space 觸發 → 選取該圖,並請上層開右抽屜(手機)。
  onActivate(e: { item: unknown; index: number }): void {
    this.pick(e.item as PhotoListItem);
    this.opened.emit();
  }

  // 方向鍵走到結尾附近:補載下一頁(沿用 sentinel IO 的同一 store.loadMore 與守衛)。
  onLoadMore(): void {
    if (this.hasMore() && !this.loading()) void this.store.loadMore();
  }

  // 無限捲哨兵:接近底部時自動載下一頁(rootMargin 提前預抓)。
  @ViewChild('sentinel') private sentinel?: ElementRef<HTMLElement>;
  private io?: IntersectionObserver;

  ngAfterViewInit(): void {
    if (!this.sentinel) return;
    this.io = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting && this.hasMore() && !this.loading()) {
          void this.store.loadMore();
        }
      },
      { rootMargin: '600px' },
    );
    this.io.observe(this.sentinel.nativeElement);
  }

  ngOnDestroy(): void {
    this.io?.disconnect();
  }

  // 儲存目前搜尋:無 token 時 disabled(template 層也保護),成功/失敗皆 toast 提示。
  saveSearch(): void {
    const ts = this.tokens();
    if (!ts.length) return;
    const queryJson = JSON.stringify(ts);
    const name = ts.map((t) => t.text).join(' ');
    void this.api.createSavedSearch({ name, queryJson }).then(() => {
      this.toast.success('已儲存搜尋');
    }).catch(() => {
      this.toast.error('儲存搜尋失敗,請稍後再試');
    });
  }

  // 重標失敗的:把所有 WD14 error 狀態的 job 重排(mode:retry,非破壞不清既有 tag)。
  // 重標 = WD14 自動標籤推論,≠ 重掃(重掃=重建檔案索引,在「圖庫來源」頁)。
  requeueFailed(): void {
    void this.confirm
      .ask(
        '將所有標註失敗(WD14 error)的圖片重新加入佇列,等待推論引擎重試。\n\n' +
          '注意:「重標」= 重跑 WD14 自動標籤推論;「重掃」= 重建檔案索引(在「圖庫來源」頁)。',
        { title: '重標失敗的圖片?', confirmText: '重標失敗', cancelText: '取消' },
      )
      .then((ok) => {
        if (!ok) return;
        return this.api.requeue('retry', { error: true }).then((r) => {
          const detail = `(共 ${r.matched} 筆,新建 ${r.jobsCreated}、更新 ${r.jobsUpdated} 個 job)`;
          this.toast.success(`已重排 ${r.matched} 筆失敗標註 ${detail}`);
        });
      })
      .catch(() => {
        this.toast.error('重標失敗,請稍後再試');
      });
  }

  // 重標「目前查詢」命中的圖:把當前布林查詢(all/none)當 scope 傳後端 query scope。
  // 同 requeueFailed 為 retry 模式(非破壞)。requeueingQuery 控按鈕 disabled。
  readonly requeueingQuery = signal(false);
  requeueByQuery(): void {
    const { all, none } = this.store.currentQuery();
    const n = this.hitCount().toLocaleString('en-US');
    void this.confirm
      .ask(
        `將目前查詢命中的 ${n} 張圖重新加入 WD14 自動標籤佇列(retry,非破壞、不清既有 tag)。`,
        { title: '重標目前查詢的圖片?', confirmText: '重標查詢', cancelText: '取消' },
      )
      .then((ok) => {
        if (!ok) return;
        this.requeueingQuery.set(true);
        return this.api
          .requeue('retry', { query: { all, none } })
          .then((r) => {
            const detail = `(共 ${r.matched} 筆,新建 ${r.jobsCreated}、更新 ${r.jobsUpdated} 個 job)`;
            this.toast.success(`已重排 ${r.matched} 筆 ${detail}`);
          })
          .finally(() => this.requeueingQuery.set(false));
      })
      .catch(() => {
        this.requeueingQuery.set(false);
        this.toast.error('重標失敗,請稍後再試');
      });
  }

  // ---- 搜尋框 autocomplete(查既有標籤;與 inspector combobox 同模式,日後可抽共用)----
  readonly suggestions = signal<TagListRow[]>([]);
  readonly acIndex = signal(-1);
  readonly noSuchTag = signal<string | null>(null);
  private acDebounce: ReturnType<typeof setTimeout> | null = null;
  private acSeq = 0;

  // 打字 → debounce 查既有標籤(清 noSuchTag、傳原始 term)。
  onType(v: string): void {
    this.acIndex.set(-1);
    this.noSuchTag.set(null);
    const term = v.trim();
    if (this.acDebounce) clearTimeout(this.acDebounce);
    if (!term) { this.suggestions.set([]); return; }
    this.acDebounce = setTimeout(() => void this.doSuggest(term), 180);
  }

  private async doSuggest(term: string): Promise<void> {
    const seq = ++this.acSeq;
    try {
      const rows = await this.api.tags(normalizeTagQuery(term), 12);
      if (seq === this.acSeq) {
        this.suggestions.set(excludeSelected(rows, this.tokens().map((t) => t.text)));
      }
    } catch {
      if (seq === this.acSeq) this.suggestions.set([]);
    }
  }

  acMove(delta: number): void {
    const n = this.suggestions().length;
    if (!n) return;
    this.acIndex.set(Math.max(0, Math.min(this.acIndex() + delta, n - 1)));
  }

  // Enter:精準 exact 驗證 + 查無此標;移除 addSearch 舊路徑。
  onEnter(input: HTMLInputElement): void {
    const rows = this.suggestions();
    const i = this.acIndex();
    if (i >= 0 && i < rows.length) {
      this.pickSuggestion(rows[i], input);
      return;
    }
    const hit = exactMatch(rows, input.value);
    if (hit) {
      this.store.addToken({ text: hit.name, kind: hit.kind as SearchToken['kind'] });
      input.value = '';
      this.closeAc();
    } else {
      this.noSuchTag.set(`查無此標:${input.value.trim()}`);
    }
  }

  pickSuggestion(s: TagListRow, input: HTMLInputElement): void {
    this.store.addToken({ text: s.name, kind: s.kind as SearchToken['kind'] });
    input.value = '';
    this.closeAc();
  }

  closeAc(): void {
    this.suggestions.set([]);
    this.acIndex.set(-1);
    if (this.acDebounce) { clearTimeout(this.acDebounce); this.acDebounce = null; }
  }

  // CSS 色(含 var())→ 半透明 color-mix(共用 @core/tag-color;template 沿用此方法)
  rgba(c: string, a: number): string { return tint(c, a); }
}
