import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DrawerPanel } from './drawer-panel';

@Component({
  standalone: true,
  imports: [DrawerPanel],
  template: `
    <app-drawer-panel [open]="open()" [side]="side()" title="測試抽屜" (close)="closed.set(closed() + 1)">
      <p class="proj">投影內容</p>
    </app-drawer-panel>
  `,
})
class Host {
  readonly open = signal(true);
  readonly side = signal<'left' | 'right'>('left');
  readonly closed = signal(0);
}

describe('DrawerPanel', () => {
  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root };
  }

  it('open=true 時渲染面板與投影內容', () => {
    const { root } = setup();
    expect(root.querySelector('.dp-panel')).toBeTruthy();
    expect(root.querySelector('.proj')?.textContent).toContain('投影內容');
    expect(root.querySelector('.dp-title')?.textContent).toContain('測試抽屜');
  });

  it('open=false 時不渲染', () => {
    const { fixture, root } = setup();
    fixture.componentInstance.open.set(false);
    fixture.detectChanges();
    expect(root.querySelector('.dp-scrim')).toBeNull();
  });

  it('side 對應 class(left / right)', () => {
    const { fixture, root } = setup();
    expect(root.querySelector('.dp-panel.left')).toBeTruthy();
    fixture.componentInstance.side.set('right');
    fixture.detectChanges();
    expect(root.querySelector('.dp-panel.right')).toBeTruthy();
    expect(root.querySelector('.dp-panel.left')).toBeNull(); // 切換後舊 class 應移除
  });

  it('點關閉 X 觸發一次 (close)', () => {
    const { fixture, root } = setup();
    (root.querySelector('.dp-close') as HTMLButtonElement).click();
    expect(fixture.componentInstance.closed()).toBe(1);
  });

  it('點 scrim(面板外)觸發 (close)', () => {
    const { fixture, root } = setup();
    (root.querySelector('.dp-scrim') as HTMLElement).click();
    expect(fixture.componentInstance.closed()).toBe(1);
  });

  it('點面板內部不觸發 (close)', () => {
    const { fixture, root } = setup();
    (root.querySelector('.dp-panel') as HTMLElement).click();
    expect(fixture.componentInstance.closed()).toBe(0);
  });

  it('Esc 觸發 (close)', () => {
    const { fixture, root } = setup();
    const scrim = root.querySelector('.dp-scrim') as HTMLElement;
    scrim.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(fixture.componentInstance.closed()).toBe(1);
  });
});
