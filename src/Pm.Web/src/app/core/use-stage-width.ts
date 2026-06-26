import { DestroyRef, ElementRef, Signal, signal } from '@angular/core';

/** 量測 host 元素寬度的唯讀 signal;ResizeObserver + requestAnimationFrame debounce。 */
export function useStageWidth(host: ElementRef<HTMLElement>, destroyRef: DestroyRef): Signal<number> {
  const width = signal(host.nativeElement.getBoundingClientRect().width);
  let raf = 0;
  const ro = new ResizeObserver((entries) => {
    const w = entries[0]?.contentRect.width ?? 0;
    cancelAnimationFrame(raf);
    raf = requestAnimationFrame(() => width.set(w));
  });
  ro.observe(host.nativeElement);
  destroyRef.onDestroy(() => { cancelAnimationFrame(raf); ro.disconnect(); });
  return width.asReadonly();
}
