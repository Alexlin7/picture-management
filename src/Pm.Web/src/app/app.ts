import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastHost } from '@core/ui/toast';
import { ConfirmHost } from '@core/ui/confirm';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastHost, ConfirmHost],
  template: `
    <router-outlet />
    <app-toast-host />
    <app-confirm-host />
  `,
})
export class App {}
