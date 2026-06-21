import { Component } from '@angular/core';
import { Workbench } from './shell/workbench';

@Component({
  selector: 'app-root',
  imports: [Workbench],
  template: `<app-workbench />`,
})
export class App {}
