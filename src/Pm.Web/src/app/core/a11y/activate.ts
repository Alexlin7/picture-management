import { Directive, output } from '@angular/core';

/**
 * pmActivate —— 讓非 <button> 元素具備按鈕語意與鍵盤可達性。
 *
 * 掛上後自動補 role="button" + tabindex="0",並把 click / Enter / Space 三種觸發
 * 統一成單一 (pmActivate) 事件輸出。用於 facet 列、masonry tile、麵包屑、資料夾列、
 * 排序表頭等因版面或巢狀互動因素不適合直接換原生 <button> 的可點元素。
 *
 * 焦點環交給全域 :focus-visible(styles.css),不需元件自繪。
 * 純 icon 鈕(×、箭頭)仍優先用原生 <button>,不要套本 directive(避免巢狀互動)。
 */
@Directive({
  selector: '[pmActivate]',
  host: {
    role: 'button',
    tabindex: '0',
    '(click)': 'fire($event)',
    '(keydown.enter)': 'fire($event)',
    '(keydown.space)': 'fire($event)',
  },
})
export class Activate {
  readonly pmActivate = output<Event>();

  fire(e: Event): void {
    // 鍵盤觸發:擋掉 Space 捲動頁面與 Enter 預設行為;滑鼠 click 無此問題。
    if (e.type !== 'click') e.preventDefault();
    this.pmActivate.emit(e);
  }
}
