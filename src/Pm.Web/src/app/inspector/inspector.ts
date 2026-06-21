import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-inspector',
  standalone: true,
  template: `<div style="padding:24px;color:var(--muted)">選一張圖看細節</div>`,
})
export class Inspector {
  @Input() photoId: number | null = null;
}
