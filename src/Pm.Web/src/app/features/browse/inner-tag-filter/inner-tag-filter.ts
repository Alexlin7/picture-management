import { Component, inject, signal } from '@angular/core';
import { BrowseStore, type InnerToken } from '../browse.store';
import { PmApi, type FolderTag } from '@core/api/pm-api';
import { tagColor, type TagKind } from '@core/tag-color';

// 夾內疊 tag:+tag 自動完成只列「當前資料夾範圍內實際存在」的 tag(打字即時過濾),選了 = 範圍 AND tag。
@Component({
  selector: 'app-inner-tag-filter',
  imports: [],
  templateUrl: './inner-tag-filter.html',
  styleUrl: './inner-tag-filter.css',
})
export class InnerTagFilter {
  private readonly store = inject(BrowseStore);
  private readonly api = inject(PmApi);
  readonly tokens = this.store.innerTokens;
  readonly kindColor = tagColor;

  readonly suggestions = signal<FolderTag[]>([]);
  readonly acIndex = signal(-1);
  private all: FolderTag[] = [];     // 當前夾全部可用 tag(載一次,前端過濾)
  private loadedKey = '';

  // 開啟輸入時載當前夾可用 tag(只在 root/path 變動時重載)。
  // 競態 guard:先佔住 key,await 後確認仍是同一 key 才寫入;
  // 快速切夾時舊夾的晚到結果會被丟棄,不會覆蓋新夾資料。
  async ensureLoaded(): Promise<void> {
    const rootId = this.store.currentRootId();
    if (rootId === null) return;
    const key = `${rootId}:${this.store.currentPath()}`;
    if (key === this.loadedKey) return;
    this.loadedKey = key;            // 先佔住 key
    try {
      const result = await this.api.folderTags(rootId, this.store.currentPath());
      if (this.loadedKey === key) this.all = result;   // 切夾後晚到的舊結果丟棄
    } catch {
      if (this.loadedKey === key) this.all = [];
    }
  }

  async onType(v: string): Promise<void> {
    await this.ensureLoaded();
    this.acIndex.set(-1);
    const term = v.trim().toLowerCase().replace(/\s+/g, '_');
    const selected = new Set(this.tokens().map((t) => t.text.toLowerCase()));
    const rows = this.all
      .filter((r) => !selected.has(r.name.toLowerCase()) && (!term || r.name.toLowerCase().includes(term)))
      .slice(0, 12);
    this.suggestions.set(rows);
  }

  move(d: number): void {
    const n = this.suggestions().length; if (!n) return;
    this.acIndex.set(Math.max(0, Math.min(this.acIndex() + d, n - 1)));
  }
  onEnter(input: HTMLInputElement): void {
    const rows = this.suggestions(); const i = this.acIndex();
    const pick = i >= 0 && i < rows.length ? rows[i] : rows[0];
    if (pick) this.add(pick, input);
  }
  add(s: FolderTag, input: HTMLInputElement): void {
    this.store.addInnerTag(s.name, s.kind as TagKind);
    input.value = ''; this.close();
  }
  remove(idx: number, ev: Event): void { ev.stopPropagation(); this.store.removeInnerTag(idx); }
  close(): void { this.suggestions.set([]); this.acIndex.set(-1); }

  tokenStyle(t: InnerToken): Record<string, string> {
    const c = this.kindColor(t.kind);
    return { color: c, background: this.rgba(c, 0.12), 'border-color': this.rgba(c, 0.34) };
  }
  private rgba(hex: string, a: number): string {
    const n = parseInt(hex.slice(1), 16);
    return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
  }
}
