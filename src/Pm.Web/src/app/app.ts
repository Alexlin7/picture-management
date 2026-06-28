import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastHost } from '@core/ui/toast';
import { ConfirmHost } from '@core/ui/confirm';
import { MergeDialogHost } from '@core/ui/merge-dialog';
import { LightboxHost } from '@core/ui/lightbox';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastHost, ConfirmHost, MergeDialogHost, LightboxHost],
  template: `
    <router-outlet />
    <app-toast-host />
    <app-confirm-host />
    <app-merge-dialog-host />
    <app-lightbox-host />
  `,
})
export class App {}
