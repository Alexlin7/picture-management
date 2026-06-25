import { describe, it, expect } from 'vitest';
import { breadcrumbFromPath, findNode, subfoldersOf } from './browse-tree';
import type { FolderNode } from '@core/api/pm-api';

const tree: FolderNode = {
  name: '圖庫', relPath: '', photoCount: 6,
  children: [
    { name: 'Pixiv', relPath: 'Pixiv', photoCount: 4, children: [
      { name: '2023', relPath: 'Pixiv/2023', photoCount: 1, children: null },
      { name: '2024', relPath: 'Pixiv/2024', photoCount: 3, children: [
        { name: 'sub', relPath: 'Pixiv/2024/sub', photoCount: 1, children: null },
      ] },
    ] },
    { name: 'Twitter', relPath: 'Twitter', photoCount: 1, children: null },
  ],
};

describe('breadcrumbFromPath', () => {
  it('根層只有 root 名', () => {
    expect(breadcrumbFromPath('圖庫', '')).toEqual([{ name: '圖庫', relPath: '' }]);
  });
  it('累積每層 relPath 前綴', () => {
    expect(breadcrumbFromPath('圖庫', 'Pixiv/2024')).toEqual([
      { name: '圖庫', relPath: '' },
      { name: 'Pixiv', relPath: 'Pixiv' },
      { name: '2024', relPath: 'Pixiv/2024' },
    ]);
  });
});

describe('findNode', () => {
  it('空 relPath 回 root', () => {
    expect(findNode(tree, '')?.name).toBe('圖庫');
  });
  it('深層節點', () => {
    expect(findNode(tree, 'Pixiv/2024')?.photoCount).toBe(3);
    expect(findNode(tree, 'Pixiv/2024/sub')?.photoCount).toBe(1);
  });
  it('不存在回 null', () => {
    expect(findNode(tree, 'Nope/x')).toBeNull();
  });
});

describe('subfoldersOf', () => {
  it('回該節點的直接子資料夾', () => {
    expect(subfoldersOf(tree, 'Pixiv').map((c) => c.name)).toEqual(['2023', '2024']);
  });
  it('葉節點回空陣列', () => {
    expect(subfoldersOf(tree, 'Pixiv/2024/sub')).toEqual([]);
  });
  it('找不到節點回空陣列', () => {
    expect(subfoldersOf(tree, 'Nope')).toEqual([]);
  });
});
