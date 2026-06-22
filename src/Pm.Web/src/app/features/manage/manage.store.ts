import { Injectable, signal } from '@angular/core';
import {
  MOCK_ROOTS,
  MOCK_IMPORT,
  MOCK_IMPORT_SOURCE,
  MOCK_RECON,
  MOCK_RECON_RELOCATED,
  MOCK_SAVED,
} from '@testing/mock-data';

// Manage feature 的 signal store:
// 把「假資料」與「元件」之間插一層,未來換成 @core/api 的真實 API 只需改這個檔。
// 唯一 import @testing/mock-data 的地方;元件改 inject(ManageStore) 取資料。
@Injectable({ providedIn: 'root' })
export class ManageStore {
  // 圖庫來源
  private readonly _roots = signal(MOCK_ROOTS);
  readonly roots = this._roots.asReadonly();

  // 匯入確認:路徑段 → tag
  private readonly _importRows = signal(MOCK_IMPORT);
  readonly importRows = this._importRows.asReadonly();
  private readonly _importSource = signal(MOCK_IMPORT_SOURCE);
  readonly importSource = this._importSource.asReadonly();

  // 失蹤待辦匣
  private readonly _reconRows = signal(MOCK_RECON);
  readonly reconRows = this._reconRows.asReadonly();
  private readonly _relocated = signal(MOCK_RECON_RELOCATED);
  readonly relocated = this._relocated.asReadonly();

  // 收藏的搜尋
  private readonly _saved = signal(MOCK_SAVED);
  readonly saved = this._saved.asReadonly();
}

// 對外暴露資料的型別(從 @testing/mock-data re-export),好讓元件只從 store 取型別。
export type {
  RootRow,
  ImportRow,
  ReconRow,
  SavedSearch,
} from '@testing/mock-data';
export type { TagKind } from '@core/tag-color';
