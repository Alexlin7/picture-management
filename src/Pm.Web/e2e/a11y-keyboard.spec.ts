// a11y 鍵盤可達 e2e(Phase 1 遷移自 a11y-keyboard-smoke.mjs)。
// 對齊 docs/design/2026-06-29-e2e-test-hardening.md §二 八條鐵則:
//   web-first 斷言(expect 輪詢 / toBeFocused)、禁硬等、locator 優先序(getByRole 優先)、
//   禁 ElementHandle、禁 force、URL 走 baseURL、測試隔離、單一行為、mock 即真相。
//
// 來源 .mjs 用 waitForTimeout(:115/120/130)與 stall<10 補頁脆弱迴圈(:128-133)等待焦點/補頁;
// 本檔一律改成 web-first 等待:
//   · 焦點檢查 → await expect(locator).toBeFocused()(不讀 document.activeElement 一次比較)。
//   · 方向鍵補頁 → expect.poll 反覆 ArrowDown,等 active 格 data-i 超過第一頁筆數(對齊 browse.spec 補頁等待法)。
//
// 覆蓋(依來源 a11y 主題拆成多支聚焦 test):
//   shell landmark:skip-link 可聚焦且指向 #main-content、<nav aria-label> landmark。
//   roving tabindex:圖牆是單一 Tab 停駐點(只有 active tile tabindex=0,其餘 -1)。
//   方向鍵在 tile 間移動焦點(ArrowRight),Enter / Space 觸發選圖。
//   combobox/listbox/option ARIA(夾內再篩 autocomplete)。
//   方向鍵移動到底自動補頁(active data-i 超過第一頁 60)。
import { test, expect } from '@playwright/test';
import { installApiMock } from './fixtures';

// viewport 沿用來源 .mjs 桌面寬 1440×900(MOBILE 門檻 768 以上,不進抽屜模式)。
const DESKTOP = { width: 1440, height: 900 };

// 主測頁(對齊來源:root=1、path=Pixiv)。
const BROWSE_URL = '/browse?root=1&path=Pixiv';

// 來源 mock 用 fixtures.searchPage 預設:首頁 60 筆(data-i 0..59),之後分頁補滿 ~130 張。
const FIRST_PAGE_SIZE = 60;

// roving 格:active 格 tabindex=0(整個圖牆的唯一 Tab 停駐點),data-i 為其在圖牆中的索引。
const masonryItem = '[data-testid="masonry-item"]';
const activeCell = `${masonryItem}[tabindex="0"]`;

test.use({ viewport: DESKTOP });

// 全程 mock /api(鐵則 #5 mock 即真相):不依賴真實圖庫 / 不碰原圖。
test.beforeEach(async ({ page }) => {
  await installApiMock(page);
});

test.describe('shell a11y landmark', () => {
  test('skip-link 可聚焦(首個 Tab 停駐點)且指向 #main-content', async ({ page }) => {
    await page.goto(BROWSE_URL);

    const skip = page.getByRole('link', { name: '跳到主內容' });
    await expect(skip).toHaveAttribute('href', '#main-content');

    // 平時 translateY(-150%) 移出畫面,Tab 聚焦才現身;此處驗「可聚焦」= 首個 Tab 停駐點。
    await page.keyboard.press('Tab');
    await expect(skip).toBeFocused();

    // skip-link 指向的 <main id="main-content"> landmark 確實存在。
    await expect(page.getByRole('main')).toHaveAttribute('id', 'main-content');
  });

  test('<nav> landmark 有 aria-label=主導覽', async ({ page }) => {
    await page.goto(BROWSE_URL);
    await expect(page.getByRole('navigation', { name: '主導覽' })).toBeVisible();
  });
});

test.describe('roving tabindex 圖牆', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(BROWSE_URL);
    // 等首格出現代表 roving 圖牆已渲染、資料已載入,不靠 networkidle / 硬等。
    await expect(page.getByTestId('masonry-item').first()).toBeVisible();
  });

  test('圖牆是單一 Tab 停駐點(只有 active tile tabindex=0,其餘 -1)', async ({ page }) => {
    // roving 模式下每格是 role=button(pmActivate / Masonry 提供)。
    await expect(page.getByTestId('masonry-item').first()).toHaveAttribute('role', 'button');
    // 整個圖牆只有 1 個 tabindex=0(唯一 Tab 停駐點),其餘格為 -1。
    await expect(page.locator(activeCell)).toHaveCount(1);
    await expect(page.locator(`${masonryItem}[tabindex="-1"]`)).not.toHaveCount(0);
  });

  test('每格有可及名稱(role=button 不再無名;守 Lighthouse aria-command-name)', async ({ page }) => {
    // 首格 data-i=0 → 序位名「圖片 1」(itemLabel);走語意 locator 確認可及名稱存在。
    await expect(page.getByRole('button', { name: '圖片 1', exact: true })).toBeVisible();
    await expect(page.locator(`${masonryItem}[data-i="0"]`)).toHaveAttribute('aria-label', '圖片 1');
  });

  test('ArrowRight 在 tile 間移動焦點', async ({ page }) => {
    // 聚焦 active 格(data-i=0);.focus() 是 locator 動作(非 ElementHandle)。
    const cell0 = page.locator(`${masonryItem}[data-i="0"]`);
    await cell0.focus();
    await expect(cell0).toBeFocused();

    // ArrowRight = 閱讀順序下一格(gridNavTarget 'right' → index+1),焦點移到 data-i=1。
    await page.keyboard.press('ArrowRight');
    await expect(page.locator(`${masonryItem}[data-i="1"]`)).toBeFocused();
    await expect(cell0).not.toBeFocused();
  });

  test('Enter 觸發選圖', async ({ page }) => {
    await page.locator(`${masonryItem}[data-i="0"]`).focus();
    await page.keyboard.press('Enter');
    // 觸發 activate → store.select;被選格(role=button)設 aria-pressed=true 表達「已選取」語意。
    // 走語意 locator(鐵則 #3),不再依賴 .tile.sel CSS。
    await expect(page.locator(`${masonryItem}[aria-pressed="true"]`)).toHaveCount(1);
    await expect(page.locator(`${masonryItem}[data-i="0"]`)).toHaveAttribute('aria-pressed', 'true');
  });

  test('Space 觸發選圖', async ({ page }) => {
    // 先以 ArrowRight 把焦點移到 data-i=1,再 Space 觸發 —— 驗 Space 在非首格也能選取。
    await page.locator(`${masonryItem}[data-i="0"]`).focus();
    await page.keyboard.press('ArrowRight');
    await expect(page.locator(`${masonryItem}[data-i="1"]`)).toBeFocused();
    await page.keyboard.press(' ');
    // 選格 data-i=1 應為唯一 aria-pressed=true(語意 locator,不依賴 .tile.sel CSS)。
    await expect(page.locator(`${masonryItem}[aria-pressed="true"]`)).toHaveCount(1);
    await expect(page.locator(`${masonryItem}[data-i="1"]`)).toHaveAttribute('aria-pressed', 'true');
  });

  test('方向鍵移動到底自動補頁(active data-i 超過第一頁)', async ({ page }) => {
    const cell = page.locator(activeCell);
    await cell.focus();
    await expect(cell).toBeFocused();

    // 一路 ArrowDown:走到結尾附近時 Masonry 發 loadMore → 補下一頁,active 可續往更深的格移動。
    // 以 expect.poll 反覆按鍵 + 讀 active 格 data-i(web-first 輪詢,取代來源 stall<10 + waitForTimeout 迴圈);
    // 第一頁 60 筆(data-i 0..59),active 抵達 ≥ 60 即證明方向鍵到底有自動補頁。
    await expect
      .poll(
        async () => {
          await page.keyboard.press('ArrowDown');
          const di = await page.locator(activeCell).getAttribute('data-i');
          return Number(di);
        },
        { timeout: 20_000 },
      )
      .toBeGreaterThanOrEqual(FIRST_PAGE_SIZE);
  });
});

test.describe('夾內再篩 combobox / listbox / option ARIA', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(BROWSE_URL);
    await expect(page.getByTestId('masonry-item').first()).toBeVisible();
  });

  test('combobox 展開時 aria-expanded / listbox / option / aria-activedescendant 正確', async ({ page }) => {
    const combo = page.getByRole('combobox', { name: '夾內再篩標籤' });
    // 未展開:aria-expanded=false。
    await expect(combo).toHaveAttribute('aria-expanded', 'false');

    await combo.click();
    await combo.fill('a');

    // 浮層 role=listbox、項目 role=option(走 getByRole,鐵則 #3 優先序)。
    const listbox = page.getByRole('listbox');
    await expect(listbox).toBeVisible();
    await expect(combo).toHaveAttribute('aria-expanded', 'true');
    // 至少列出選項即可:此 combobox 的過濾由後端負責(mock 回整批 folder-tags),
    // 本測試重點是 option 的 role / ARIA 而非筆數。
    await expect(listbox.getByRole('option').first()).toBeVisible();

    // ArrowDown 移動高亮 → combobox aria-activedescendant 指向當前 option id。
    await page.keyboard.press('ArrowDown');
    await expect(combo).toHaveAttribute('aria-activedescendant', /itf-ac-\d+/);
  });
});
