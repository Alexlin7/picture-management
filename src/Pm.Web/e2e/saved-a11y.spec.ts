import { test, expect } from '@playwright/test';
import { installApiMock } from './fixtures';

// 守住「收藏的搜尋」刪除鈕的 a11y 修正(frontend gap P2 唯一真實鍵盤缺口):
// 舊版刪除鈕是 hover-only 的 <span role="button">,且巢在卡片 <button> 內 → 純鍵盤不可達。
// 改為:卡片是 <div> 容器,主動作與刪除為兩個並排的真 <button>,刪除鈕常駐 DOM、
// 由 CSS :hover / :focus-within 顯示。本檔斷言這些 a11y 不變量。
const SAVED = [
  { id: 1, name: '藍色頭髮', queryJson: '[]', createdAt: '2026-06-01' },
  { id: 2, name: '貓耳少女', queryJson: '[]', createdAt: '2026-06-02' },
];

test.describe('收藏的搜尋 — 刪除鈕 a11y', () => {
  test.beforeEach(async ({ page }) => {
    await installApiMock(page);
    // 覆寫 saved-searches(後註冊者優先):集合 GET 回卡片,DELETE by id 回 200。
    await page.route('**/api/saved-searches', async (route) => {
      if (route.request().method() === 'GET') {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(SAVED) });
      } else {
        await route.fallback();
      }
    });
    await page.route(/\/api\/saved-searches\/\d+$/, (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{}' }),
    );
    await page.goto('/saved');
  });

  test('刪除鈕常駐 DOM、是鍵盤可達的真 button(非 hover-only span)', async ({ page }) => {
    const del = page.getByRole('button', { name: /刪除收藏.*藍色頭髮/ });
    await expect(del).toBeAttached(); // 常駐:舊版 @if(hovered()) 時根本不在 DOM
    await del.focus();
    await expect(del).toBeFocused(); // 可聚焦 = 鍵盤可達
  });

  test('主動作與刪除是兩個並排的獨立 button(解巢狀)', async ({ page }) => {
    await expect(page.getByRole('button', { name: /藍色頭髮/ }).first()).toBeVisible(); // 主動作
    await expect(page.getByRole('button', { name: /刪除收藏.*藍色頭髮/ })).toBeAttached(); // 刪除(sibling)
  });

  test('鍵盤啟動刪除鈕 → 該卡移除、另一卡仍在', async ({ page }) => {
    await page.getByRole('button', { name: /刪除收藏.*藍色頭髮/ }).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: /藍色頭髮/ })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /貓耳少女/ }).first()).toBeVisible();
  });
});
