import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { vi } from 'vitest';
import { Thumb } from './thumb';

describe('Thumb', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Thumb],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  function make(photoId = 1) {
    const fixture = TestBed.createComponent(Thumb);
    fixture.componentRef.setInput('photoId', photoId);
    fixture.detectChanges();
    return fixture;
  }

  it('starts in loading state with skeleton', () => {
    const fixture = make();
    expect(fixture.componentInstance.state()).toBe('loading');
    expect(fixture.nativeElement.querySelector('.skeleton')).toBeTruthy();
  });

  it('goes to loaded on img load and removes skeleton', () => {
    const fixture = make();
    fixture.componentInstance.onLoad();
    fixture.detectChanges();
    expect(fixture.componentInstance.state()).toBe('loaded');
    expect(fixture.nativeElement.querySelector('.skeleton')).toBeFalsy();
  });

  it('retries on error then falls back to broken after exhausting retries', () => {
    vi.useFakeTimers();
    const fixture = make();
    const cmp = fixture.componentInstance;
    // 初次 + 5 次重試 = 第 6 次 error 才轉 broken
    for (let i = 0; i < 6; i++) {
      cmp.onError();
      vi.runAllTimers();
    }
    fixture.detectChanges();
    expect(cmp.state()).toBe('broken');
    expect(fixture.nativeElement.querySelector('.broken')).toBeTruthy();
    vi.useRealTimers();
  });

  // 重用同一實例切換 photoId(如 inspector 預覽切第二張圖):src 必須指向新圖、回到 loading。
  it('reloads when photoId changes on a reused instance', () => {
    const fixture = make(1);
    const cmp = fixture.componentInstance;
    cmp.onLoad(); // 第一張載入完成
    fixture.detectChanges();
    expect(cmp.state()).toBe('loaded');
    expect(cmp.src()).toBe('/api/photos/1/thumb');

    // 切到第二張(重用同一 app-thumb 實例)
    fixture.componentRef.setInput('photoId', 2);
    fixture.detectChanges();
    expect(cmp.state()).toBe('loading');
    expect(cmp.src()).toBe('/api/photos/2/thumb');
  });
});
