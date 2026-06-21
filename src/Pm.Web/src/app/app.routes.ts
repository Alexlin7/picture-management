import { Routes } from '@angular/router';

// Shell 常駐(活動列),各 view lazy 載入、獨立 code-split。
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      { path: '', redirectTo: 'gallery', pathMatch: 'full' },
      {
        path: 'gallery',
        loadComponent: () => import('./gallery/gallery-view').then((m) => m.GalleryView),
      },
      {
        path: 'import',
        loadComponent: () => import('./manage/import-confirm').then((m) => m.ImportConfirm),
      },
      {
        path: 'reconcile',
        loadComponent: () => import('./manage/reconcile').then((m) => m.Reconcile),
      },
      {
        path: 'saved',
        loadComponent: () => import('./manage/saved-searches').then((m) => m.SavedSearches),
      },
      {
        path: 'roots',
        loadComponent: () => import('./manage/roots').then((m) => m.Roots),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
