import { Component, computed, inject, signal } from '@angular/core';
import { tagColor, DANGER } from '@core/tag-color';
import { PmApi, type PhotoListItem, type TagListRow } from '@core/api/pm-api';
import { GalleryStore, type SearchToken } from '../gallery.store';
import { normalizeTagQuery, exactMatch, excludeSelected } from '@core/tag-search';
import { displayOf } from '@core/tag-display';
import { ToastService } from '@core/ui/toast';

// 契約:頂欄 token 搜尋列 + masonry 圖牆。點 tile → 寫入 store 選取。
@Component({
  selector: 'app-photo-grid',
  imports: [],
  templateUrl: './photo-grid.html',
  styleUrl: './photo-grid.css',
})
export class PhotoGrid {
  private readonly store = inject(GalleryStore);
  private readonly api = inject(PmApi);
  private readonly toast = inject(ToastService);

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

  // 兩段式檢視切換:dense=小圖密集 / large=大圖(純視覺本地狀態)
  readonly viewMode = signal<'dense' | 'large'>('dense');

  // 千分位
  readonly hitCountText = computed(() => this.hitCount().toLocaleString('en-US'));
  readonly wd14QueueText = computed(() => this.wd14Queue.toLocaleString('en-US'));

  // 縮圖 URL(依 hash,絕不碰原圖)
  thumb(id: number): string {
    return this.store.thumbUrl(id);
  }

  // tile 用真實長寬比預留空間(無尺寸退 1:1):避免變形,且載入前先佔位不跳動。
  aspect(p: PhotoListItem): string {
    return p.width && p.height ? `${p.width}/${p.height}` : '1/1';
  }

  // kind → 顏色(共用分色 helper)
  kindColor = tagColor;

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

  // 載入下一頁
  loadMore(): void {
    void this.store.loadMore();
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

  // hex → rgba helper(半透明底色)
  rgba(hex: string, a: number): string {
    const n = parseInt(hex.slice(1), 16);
    const r = (n >> 16) & 255;
    const g = (n >> 8) & 255;
    const b = n & 255;
    return `rgba(${r},${g},${b},${a})`;
  }
}
