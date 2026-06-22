import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PmApi } from './pm-api';

describe('PmApi', () => {
  let api: PmApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(PmApi);
    http = TestBed.inject(HttpTestingController);
  });

  it('posts a search and returns a page', async () => {
    const p = api.search({ all: ['vspo'] });
    const req = http.expectOne('/api/search');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.all).toEqual(['vspo']);
    req.flush({ items: [{ id: 1, fileHash: 'a' }], nextCursor: null });
    expect((await p).items.length).toBe(1);
  });

  it('builds thumb url', () => {
    expect(api.thumbUrl(7)).toBe('/api/photos/7/thumb');
  });
});
