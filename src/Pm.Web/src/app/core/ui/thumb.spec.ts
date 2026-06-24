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
});
