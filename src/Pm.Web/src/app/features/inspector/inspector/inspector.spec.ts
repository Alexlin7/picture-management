import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Inspector } from './inspector';
import { InspectorStore } from '../inspector.store';

describe('Inspector reprocess action', () => {
  function make(storeStub: Partial<InspectorStore>) {
    TestBed.configureTestingModule({
      imports: [Inspector],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: InspectorStore, useValue: storeStub },
      ],
    });
    return TestBed.createComponent(Inspector);
  }

  it('calls store.reprocess and shows no error on decoded', async () => {
    let called = 0;
    const fixture = make({
      reprocess: async () => { called++; return { decoded: true, thumbGenerated: true }; },
      photo: () => ({ id: 7 }) as any,
      detail: (() => null) as any,
      loading: (() => false) as any,
      error: (() => null) as any,
      suggestions: (() => []) as any,
      load: async () => {},
      clearSuggestions: () => {},
    } as any);
    fixture.componentInstance['photoId'] = (() => 7) as any;
    await fixture.componentInstance.reprocess();
    expect(called).toBe(1);
    expect(fixture.componentInstance.reprocessMsg()).toBe('');
  });

  it('shows failure message when not decoded', async () => {
    const fixture = make({
      reprocess: async () => ({ decoded: false, thumbGenerated: false }),
      photo: () => ({ id: 7 }) as any,
      detail: (() => null) as any,
      loading: (() => false) as any,
      error: (() => null) as any,
      suggestions: (() => []) as any,
      load: async () => {},
      clearSuggestions: () => {},
    } as any);
    fixture.componentInstance['photoId'] = (() => 7) as any;
    await fixture.componentInstance.reprocess();
    expect(fixture.componentInstance.reprocessMsg()).toContain('無法解碼');
  });
});
