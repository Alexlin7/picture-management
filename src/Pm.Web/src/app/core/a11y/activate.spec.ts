import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Activate } from './activate';

@Component({
  standalone: true,
  imports: [Activate],
  template: `<div class="target" (pmActivate)="onAct($event)">點我</div>`,
})
class Host {
  readonly events = signal<Event[]>([]);
  onAct(e: Event): void {
    this.events.update((a) => [...a, e]);
  }
}

describe('pmActivate directive', () => {
  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const el = fixture.nativeElement.querySelector('.target') as HTMLElement;
    return { fixture, el };
  }

  it('補 role=button 與 tabindex=0,讓元素鍵盤可達', () => {
    const { el } = setup();
    expect(el.getAttribute('role')).toBe('button');
    expect(el.getAttribute('tabindex')).toBe('0');
  });

  it('滑鼠 click 觸發 pmActivate', () => {
    const { fixture, el } = setup();
    el.click();
    expect(fixture.componentInstance.events().length).toBe(1);
  });

  it('Enter 鍵觸發 pmActivate', () => {
    const { fixture, el } = setup();
    el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(fixture.componentInstance.events().length).toBe(1);
  });

  it('Space 鍵觸發 pmActivate 並擋掉頁面捲動(preventDefault)', () => {
    const { fixture, el } = setup();
    const ev = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true });
    el.dispatchEvent(ev);
    expect(fixture.componentInstance.events().length).toBe(1);
    expect(ev.defaultPrevented).toBe(true);
  });

  it('click 不呼叫 preventDefault(滑鼠不需擋預設)', () => {
    const { el } = setup();
    const ev = new MouseEvent('click', { bubbles: true, cancelable: true });
    el.dispatchEvent(ev);
    expect(ev.defaultPrevented).toBe(false);
  });
});
