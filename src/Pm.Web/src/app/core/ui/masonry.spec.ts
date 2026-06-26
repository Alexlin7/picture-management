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
});
