import type { FolderNode } from '@core/api/pm-api';

// 資料夾瀏覽用純函式(無副作用,易測)。

// relPath → 麵包屑:root 名起頭,逐層累積前綴。
// "Pixiv/2024" → [{圖庫,""},{Pixiv,"Pixiv"},{2024,"Pixiv/2024"}]
export function breadcrumbFromPath(rootName: string, relPath: string): { name: string; relPath: string }[] {
  const crumbs = [{ name: rootName, relPath: '' }];
  if (!relPath) return crumbs;
  const parts = relPath.split('/').filter(Boolean);
  let acc = '';
  for (const p of parts) {
    acc = acc ? `${acc}/${p}` : p;
    crumbs.push({ name: p, relPath: acc });
  }
  return crumbs;
}

// 在樹中依 relPath 找節點(relPath==="" → root);找不到 → null。
export function findNode(tree: FolderNode, relPath: string): FolderNode | null {
  if (!relPath) return tree;
  const parts = relPath.split('/').filter(Boolean);
  let node: FolderNode | null = tree;
  for (const p of parts) {
    node = node.children?.find((c) => c.name === p) ?? null;
    if (!node) return null;
  }
  return node;
}

// 某 relPath 節點的直接子資料夾(找不到或葉節點 → [])。
export function subfoldersOf(tree: FolderNode, relPath: string): FolderNode[] {
  return findNode(tree, relPath)?.children ?? [];
}
