import { TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { signal } from '@angular/core';
import { PmApi } from '@core/api/pm-api';
import { InnerTagFilter } from './inner-tag-filter';
import { BrowseStore, type InnerToken } from '../browse.store';

describe('InnerTagFilter 夾內 tag 自動完成', () => {
  let comp: InnerTagFilter;
  let folderTags: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    const innerTokens = signal<InnerToken[]>([]);
    const fakeStore = {
      innerTokens,
      currentRootId: () => 1 as number | null,
      currentPath: () => 'Pixiv',
      addInnerTag: vi.fn(),
      removeInnerTag: vi.fn(),
    };
    folderTags = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: BrowseStore, useValue: fakeStore },
        { provide: PmApi, useValue: { folderTags } },
      ],
    });
    comp = TestBed.createComponent(InnerTagFilter).componentInstance;
  });

  it('F5:folderTags 第一次失敗後,同一夾再次輸入應重試而非永久空白', async () => {
    let call = 0;
    folderTags.mockImplementation(async () => {
      call++;
      if (call === 1) throw new Error('暫時性失敗');
      return [{ name: 'smile', kind: 'general', count: 3 }];
    });

    await comp.onType('s');                 // 第一次:失敗 → 無建議
    expect(comp.suggestions()).toEqual([]);

    await comp.onType('sm');                // 同一夾再輸入:應重試
    expect(folderTags).toHaveBeenCalledTimes(2);
    expect(comp.suggestions().map((r) => r.name)).toEqual(['smile']);
  });

  it('成功載入後同一夾不重複打 API(快取生效)', async () => {
    folderTags.mockResolvedValue([{ name: 'dress', kind: 'general', count: 1 }]);
    await comp.onType('d');
    await comp.onType('dr');
    expect(folderTags).toHaveBeenCalledTimes(1);   // 成功後快取,不重打
    expect(comp.suggestions().map((r) => r.name)).toEqual(['dress']);
  });
});
