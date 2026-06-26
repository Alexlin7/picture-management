import { TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach } from 'vitest';
import { Router } from '@angular/router';
import { PmApi, type FolderNode, type PhotoPage } from '@core/api/pm-api';
import { BrowseStore } from './browse.store';

// 可手動解析的 Promise。
function deferred<T>() {
  let resolve!: (v: T) => void;
  let reject!: (e: unknown) => void;
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}
const flush = () => new Promise((r) => setTimeout(r, 0));
const node = (name: string): FolderNode => ({ name, relPath: '', photoCount: 1, children: null });

// 記錄每次呼叫並回傳可控 deferred 的 PmApi 替身。
class MockApi {
  searchCalls: { req: any; d: ReturnType<typeof deferred<PhotoPage>> }[] = [];
  countCalls: { req: any; d: ReturnType<typeof deferred<{ total: number }>> }[] = [];
  treeCalls: { id: number; d: ReturnType<typeof deferred<FolderNode>> }[] = [];

  search(req: any): Promise<PhotoPage> {
    const d = deferred<PhotoPage>(); this.searchCalls.push({ req, d }); return d.promise;
  }
  searchCount(req: any): Promise<{ total: number }> {
    const d = deferred<{ total: number }>(); this.countCalls.push({ req, d }); return d.promise;
  }
  folderTree(id: number): Promise<FolderNode> {
    const d = deferred<FolderNode>(); this.treeCalls.push({ id, d }); return d.promise;
  }
  folderRoots() { return Promise.resolve([]); }
  folderTags() { return Promise.resolve([]); }
}

describe('BrowseStore 切夾競態', () => {
  let store: BrowseStore;
  let api: MockApi;

  beforeEach(() => {
    api = new MockApi();
    TestBed.configureTestingModule({
      providers: [
        { provide: PmApi, useValue: api },
        { provide: Router, useValue: { navigate: () => Promise.resolve(true) } },
      ],
    });
    store = TestBed.inject(BrowseStore);
  });

  // 載入資料夾 A 的初始頁(tree + count + search 全解析,cursor 就緒)。
  async function loadInitial(rootId: number, items: number[], cursor: number | null, total: number) {
    void store.applyUrl(rootId, '', '');
    await flush();
    api.treeCalls.at(-1)!.d.resolve(node('root' + rootId));
    await flush();
    api.countCalls.at(-1)!.d.resolve({ total });
    api.searchCalls.at(-1)!.d.resolve({ items: items.map((id) => ({ id, fileHash: '' })), nextCursor: cursor });
    await flush();
  }

  it('F1:舊資料夾在途的 loadMore 回應不得 append 進新資料夾,也不得覆蓋 cursor', async () => {
    await loadInitial(1, [1], 11, 100);
    expect(store.photos().map((p) => p.id)).toEqual([1]);
    expect(store.hasMore()).toBe(true);

    // A 的 loadMore 在途(尚未解析)
    void store.loadMore();
    await flush();
    const staleMore = api.searchCalls.at(-1)!;   // A 的下一頁請求

    // 切到資料夾 B,B 全部解析完成
    void store.applyUrl(2, '', '');
    await flush();
    api.treeCalls.at(-1)!.d.resolve(node('root2'));
    await flush();
    api.countCalls.at(-1)!.d.resolve({ total: 5 });
    api.searchCalls.at(-1)!.d.resolve({ items: [{ id: 50, fileHash: '' }], nextCursor: 50 });
    await flush();
    expect(store.photos().map((p) => p.id)).toEqual([50]);

    // 此刻才解析 A 的舊 loadMore —— 不可污染 B
    staleMore.d.resolve({ items: [{ id: 2, fileHash: '' }], nextCursor: 2 });
    await flush();

    expect(store.photos().map((p) => p.id)).toEqual([50]);   // 不是 [50, 2]
    expect(store.hasMore()).toBe(true);                       // cursor 仍是 B 的,不是 A 的 2
  });

  it('F2:慢回的舊資料夾 search 不得覆蓋當前資料夾的 photos 與 hitCount', async () => {
    // A 的 tree 先就緒,但 count/search 故意不解析(在途)
    void store.applyUrl(1, '', '');
    await flush();
    api.treeCalls.at(-1)!.d.resolve(node('root1'));
    await flush();
    const staleCount = api.countCalls.at(-1)!;
    const staleSearch = api.searchCalls.at(-1)!;

    // 切到 B,B 全部解析
    void store.applyUrl(2, '', '');
    await flush();
    api.treeCalls.at(-1)!.d.resolve(node('root2'));
    await flush();
    api.countCalls.at(-1)!.d.resolve({ total: 5 });
    api.searchCalls.at(-1)!.d.resolve({ items: [{ id: 50, fileHash: '' }], nextCursor: 50 });
    await flush();
    expect(store.photos().map((p) => p.id)).toEqual([50]);
    expect(store.hitCount()).toBe(5);

    // A 的舊 search 此刻才回 —— 不可覆蓋 B
    staleCount.d.resolve({ total: 999 });
    staleSearch.d.resolve({ items: [{ id: 1, fileHash: '' }], nextCursor: 1 });
    await flush();

    expect(store.photos().map((p) => p.id)).toEqual([50]);   // 不是 [1]
    expect(store.hitCount()).toBe(5);                         // 不是 999
  });
});
