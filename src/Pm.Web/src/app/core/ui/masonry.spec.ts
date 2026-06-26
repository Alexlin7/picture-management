// src/Pm.Web/src/app/core/ui/masonry.spec.ts
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Masonry } from './masonry';

@Component({
  standalone: true,
  imports: [Masonry],
  template: `
    <div style="width:600px">
      <app-masonry [items]="items" [aspect]="aspect" [minColWidth]="180" [gap]="12">
        <ng-template let-item><div class="cell">{{ item }}</div></ng-template>
      </app-masonry>
    </div>`,
})
class Host { items = [1, 2, 3]; aspect = () => 1; }

describe('app-masonry', () => {
  it('renders one wrapper per item with projected content', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const cells = fixture.nativeElement.querySelectorAll('.cell');
    expect(cells.length).toBe(3);
    const wrappers = fixture.nativeElement.querySelectorAll('.m-item');
    expect(wrappers.length).toBe(3);
  });

  // 驗證 items signal input 的反應性:append 新項目後 @for 與 items() signal 皆須更新。
  // 使用 componentRef.setInput() 直接驅動 signal input(不經 Host 模板繫結);
  // 這與 gallery.store.ts 的 _photos.update() 觸發父層 @for binding 更新信號的語意等價。
  // 真實瀏覽器中額外保證:layout() computed 追蹤 items() signal → 重算 boxes,消除 boxes[$index]=undefined。
  // (JSDOM width=0 → computeMasonryLayout 一律回空 boxes,無法在此環境驗證 boxes 長度。)
  it('updates item count and items() signal when items are appended (infinite-scroll reactivity)', () => {
    const fixture = TestBed.createComponent(Masonry);
    fixture.componentRef.setInput('items', [1, 2, 3]);
    fixture.componentRef.setInput('aspect', () => 1);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.m-item').length).toBe(3);
    // items() is only callable because we converted to signal input
    expect(fixture.componentInstance.items().length).toBe(3);

    // append 3 more items (simulates infinite-scroll load)
    fixture.componentRef.setInput('items', [1, 2, 3, 4, 5, 6]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.m-item').length).toBe(6);
    // signal input must have received the new value → layout() computed also reruns
    expect(fixture.componentInstance.items().length).toBe(6);
  });
});
