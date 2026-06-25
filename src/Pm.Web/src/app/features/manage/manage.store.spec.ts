import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ManageStore } from './manage.store';

describe('ManageStore.createRoot', () => {
  let store: ManageStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    store = TestBed.inject(ManageStore);
    http = TestBed.inject(HttpTestingController);
  });

  it('returns the created root', async () => {
    const p = store.createRoot('My Lib', 'D:\\pics');

    const createReq = http.expectOne('/api/roots');           // POST 建立
    expect(createReq.request.method).toBe('POST');
    createReq.flush({ id: 42, name: 'My Lib', absPath: 'D:\\pics' });

    await new Promise((r) => setTimeout(r, 0));                // 讓 createRoot 續跑到 loadRoots
    const listReq = http.expectOne('/api/roots');             // GET 刷新清單
    expect(listReq.request.method).toBe('GET');
    listReq.flush([{ id: 42, name: 'My Lib', absPath: 'D:\\pics' }]);

    const root = await p;
    expect(root.id).toBe(42);
    expect(root.name).toBe('My Lib');
    http.verify();
  });
});
