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
        loadComponent: () => import('./features/gallery/gallery-view/gallery-view').then((m) => m.GalleryView),
      },
      {
        path: 'import',
        loadComponent: () => import('./features/manage/import-confirm/import-confirm').then((m) => m.ImportConfirm),
      },
      {
        path: 'reconcile',
        loadComponent: () => import('./features/manage/reconcile/reconcile').then((m) => m.Reconcile),
      },
      {
        path: 'saved',
        loadComponent: () => import('./features/manage/saved-searches/saved-searches').then((m) => m.SavedSearches),
      },
      {
        path: 'roots',
        loadComponent: () => import('./features/manage/roots/roots').then((m) => m.Roots),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
