import { Component, OnDestroy, computed, effect, inject, input, signal } from '@angular/core';
import {
  InspectorStore,
  type PhotoDetail,
  type TagKind,
  type TagListRow,
} from '../inspector.store';
import { KIND_LABEL, tagColor } from '@core/tag-color';
import { groupTags, type DisplayTag } from '@core/tag-display';

// combobox 浮層的列:既有標籤列 或 「建立新標籤」列。
type ComboRow = { kind: 'tag'; tag: TagListRow } | { kind: 'create'; name: string };

// 契約:右側檢視器。輸入選中的 photo id(signal input)。
// 內容:預覽圖(縮圖)、身分→位置簽名、tag lanes(分色)、EXIF。
// 資料來源接縫:InspectorStore(非同步載入 PhotoDetail)。
@Component({
  selector: 'app-inspector',
  imports: [],
  templateUrl: './inspector.html',
  styleUrl: './inspector.css',
})
export class Inspector implements OnDestroy {
  private readonly store = inject(InspectorStore);

  photoId = input<number | null>(null);

  // store 的非同步狀態(元件只讀)
  readonly photo = this.store.detail;
  readonly loading = this.store.loading;
  readonly error = this.store.error;
  readonly thumbUrl = this.store.thumbUrl;

  constructor() {
    // id 變動 → 觸發載入(store 內處理競態與清空),並收起/清空加標籤 combobox
    // (避免殘留上一張圖的輸入與建議浮層)。
    effect(() => {
      const id = this.photoId();
      this.close();
      void this.store.load(id);
    });
  }

  ngOnDestroy(): void {
    if (this.debounce) clearTimeout(this.debounce);
  }

  // tag lanes:經 displayOf 依 group 分區(character/copyright/expression/general/meta,
  // 其餘 group 如 manual/path 不丟、附在後)。group 取代舊版誤把 source(path/manual)當 kind 的作法;
  // 來源(path/manual/wd14)改由每 tag 的 source 徽章呈現。空 lane 自然不出現。
  readonly lanes = computed(() => {
    const p = this.photo();
    if (!p) return [];
    return groupTags(p.tags).map((lane) => ({
      group: lane.group,
      label: KIND_LABEL[lane.group as TagKind] ?? lane.group,
      color: tagColor(lane.group),
      tags: lane.tags,
    }));
  });

  // 來源徽章文字:wd14 帶信心度,其餘(manual/path)直接顯示來源。
  sourceLabel(t: DisplayTag): string {
    if (t.source === 'wd14') return t.confidence != null ? `wd14 ${this.pct(t.confidence)}%` : 'wd14';
    return t.source ?? '';
  }

  // EXIF 是否有相機資訊(takenAt / cameraModel 任一存在)
  readonly hasExif = computed(() => {
    const p = this.photo();
    return !!p && (p.takenAt != null || p.cameraModel != null);
  });

  // SHA-256 身分:取 fileHash 前 8 碼。
  readonly hash = computed(() => {
    const p = this.photo();
    return p ? p.fileHash.slice(0, 8) : '';
  });

  // hex → rgba helper(半透明底色 / 邊框用)
  rgba(hex: string, a: number): string {
    const n = parseInt(hex.slice(1), 16);
    return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${a})`;
  }

  pct(c: number): number {
    return Math.round(c * 100);
  }

  // API 無 title 欄位:取首個位置 relPath 的檔名當標題,無位置則退回 hash。
  fileName(p: PhotoDetail): string {
    const rel = p.locations[0]?.relPath;
    if (!rel) return p.fileHash.slice(0, 12);
    const parts = rel.split(/[\\/]/);
    return parts[parts.length - 1] || rel;
  }

  // ---- 加標籤 combobox ----
  readonly suggestions = this.store.suggestions;
  readonly query = signal('');
  readonly activeIndex = signal(-1);   // -1 = 未選;0..n-1 = comboRows 的列
  private debounce: ReturnType<typeof setTimeout> | null = null;

  // 是否已有「不分大小寫完全相符」的既有標籤(有的話就不顯示「建立新標籤」列)。
  readonly exactMatch = computed(() => {
    const q = this.query().trim().toLowerCase();
    return !!q && this.suggestions().some((s) => s.name.toLowerCase() === q);
  });

  // 只有「無完全相符」且有輸入時才顯示「建立新標籤」列。
  readonly showCreate = computed(() => !!this.query().trim() && !this.exactMatch());

  // 浮層的列模型:既有標籤列 +(視情況)一列「建立新標籤」。
  // 把「建立列」當成 list 的一員,而非靠 index===length 的特例,讓 move/onEnter/template 共用單一索引空間。
  readonly comboRows = computed<ComboRow[]>(() => {
    const rows: ComboRow[] = this.suggestions().map((tag) => ({ kind: 'tag', tag }));
    if (this.showCreate()) rows.push({ kind: 'create', name: this.query().trim() });
    return rows;
  });

  // 打字 → debounce 查既有標籤;重置選取游標。
  onType(v: string): void {
    this.query.set(v);
    this.activeIndex.set(-1);
    if (this.debounce) clearTimeout(this.debounce);
    this.debounce = setTimeout(() => void this.store.suggest(v), 180);
  }

  // ↑↓ 在 comboRows 之間移動游標。
  move(delta: number): void {
    const count = this.comboRows().length;
    if (count === 0) return;
    const next = this.activeIndex() + delta;
    this.activeIndex.set(Math.max(0, Math.min(next, count - 1)));
  }

  // Enter:有游標→用游標那列;無游標→有完全相符用既有,否則建新。
  async onEnter(): Promise<void> {
    // 若 debounce 還沒落地,先即時查一次,確保用「目前輸入字」的最新建議判斷;
    // 否則快速打字後立刻 Enter/點加入會讀到上一輪陳舊 suggestions 而誤走「建立新標籤」。
    if (this.debounce) {
      clearTimeout(this.debounce);
      this.debounce = null;
      await this.store.suggest(this.query());
    }
    const rows = this.comboRows();
    const i = this.activeIndex();
    if (i >= 0 && i < rows.length) {
      this.activate(rows[i]);
      return;
    }
    // 無游標:CI 完全相符用既有,否則(有輸入)建新。
    const q = this.query().trim().toLowerCase();
    const exact = this.suggestions().find((r) => r.name.toLowerCase() === q);
    if (exact) {
      this.pick(exact);
      return;
    }
    if (this.query().trim()) this.createNew();
  }

  // 觸發某一列(點擊 / Enter 共用)。
  activate(row: ComboRow): void {
    if (row.kind === 'tag') this.pick(row.tag);
    else this.createNew(row.name);
  }

  // 點/選既有標籤:用「那個既有名」加到圖上(後端 upsert 命中既有,不會建新)。
  pick(row: TagListRow): void {
    const id = this.photoId();
    if (id == null) return;
    void this.store.addTag(id, { name: row.name });
    this.close();
  }

  // 建立新標籤(僅在無完全相符時才會走到):後端建成 source='manual' 並進標籤庫。
  createNew(name?: string): void {
    const id = this.photoId();
    const n = (name ?? this.query()).trim();
    if (id == null || !n) return;
    void this.store.addTag(id, { name: n });
    this.close();
  }

  // Esc / 選完:收起清單、清空輸入。
  close(): void {
    this.query.set('');
    this.activeIndex.set(-1);
    this.store.clearSuggestions();
  }

  // 既有標籤分色點:沿用共用 tagColor(未知 kind 退 general),與相簿/標籤庫一致。
  dotColor(kind: string): string {
    return tagColor(kind);
  }

  // 移除這張圖上的某個標籤關聯(不刪標籤庫裡的 tag 本身)。
  remove(tagId: number): void {
    const id = this.photoId();
    if (id == null) return;
    void this.store.removeTag(id, tagId);
  }

  // 單張 WD14 動作(動作層):refresh=清舊自動標+重排;clear=清舊自動標不排。
  // 重排是否立即重標取決於 worker 能力旗標(Inference:Wd14:Enabled);關閉時為 pre-queue。
  readonly retagging = signal(false);
  async retag(mode: 'refresh' | 'clear'): Promise<void> {
    const id = this.photoId();
    if (id == null || this.retagging()) return;
    this.retagging.set(true);
    try {
      await this.store.retag(id, mode);
    } finally {
      this.retagging.set(false);
    }
  }
}
