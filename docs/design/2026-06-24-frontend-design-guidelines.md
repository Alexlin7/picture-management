---
status: active
last-reviewed: 2026-06-29
supersedes: []
superseded-by: []
related: [2026-06-24-ui-style-system-design]
---

# 前端 Design 準則(sus-picture-management)

## 用途與適用範圍

本文是 **sus-picture-management** 前端(Angular 22 + Tailwind v4 + @angular/cdk、暗色 OLED 工作台)的**設計準則(design guidelines)**。讀者是日後維護或擴充 UI 的工程師(含 AI agent)。它不是逐元件規格,而是「該怎麼決策」的上層準則:取色、排版、間距、元件複用、互動態、無障礙、文案,遇到選擇時依此判斷。

**與既有文件的關係:**

- **`docs/design/2026-06-24-ui-style-system-design.md`(Spec 1,樣式系統地基)** 是 **token / primitive 的實作地基** —— `@theme` 有哪些 token、全域 `@layer components` 有哪些 primitive、命名與落腳檔案。本文是其**上層的設計準則**:Spec 1 提供「有哪些零件」,本文規定「什麼情境用哪個、不准怎麼用」。兩者衝突時,以本文的準則為意圖、以 Spec 1 為實作真相,並回頭修正不一致的一方。
- **`CLAUDE.md`** 已有前端慣例摘要(繁中溝通、token 分層、元件樣式隔離、commit 前驗證)。本文不重複,只在必要處引用。
- **`docs/design/2026-06-21-picture-management-design.md` §6** 與 mockup `docs/mockups/ui-preview.html` 是 UI/UX 的原始設計意圖來源。

**範圍邊界:** 本文談前端視覺與互動準則,不涉後端、資料模型、ML。所有準則皆以「單人本機、暗色密集工作台」為前提 —— 不是面向公眾的響應式網站。

---

## 設計原則(north star)

整個前端的取捨,收斂到以下五條總綱。下游每一節準則都是它們的展開;遇到本文未明列的情境,回到這五條判斷。

1. **密集工作台優先(dense workbench first)。** 這是單人、本機、長時間盯著看的專業工具,不是行銷頁。資訊密度、可掃視性、鍵盤效率 > 留白美學與動畫炫技。版面為「把更多有用資訊穩定呈現」服務,留白用來分組,不用來撐場面。

2. **Token 是唯一真相(token as single source of truth)。** 顏色、間距、字級、圓角、時長、緩動一律從 `@theme` token 取值。元件不寫裸 hex、裸 px 間距、裸字級、裸毫秒。任何「落不進 token 的值」先質疑是否真的需要;真的需要就先補 token,再使用。

3. **鍵盤可達是底線(keyboard-reachable by default)。** 任何可點的東西必須能用鍵盤到達、操作、看見焦點。原生 `<button>`/`<a>` 優先於 `<div (click)>`。這是功能正確性,不是加分項。

4. **暗色高對比(dark, high-contrast, OLED-friendly)。** 暗色是預設且唯一主題,深色階梯(canvas→panel→raised)要拉得出層次,正文文字對背景高對比,小字與次要色要過 AA。cyan 是唯一品牌/互動識別色。

5. **友善繁中、人話優先(plain Traditional Chinese)。** 對使用者一律繁體中文台灣用語、完整人話。不外洩後端欄位名與內部術語,不堆英文縮寫,不留預設樣板殘留。同一概念全站一個詞。

---

## 已定案、勿回退

以下是已拍板的約束。**不要在沒有明確新決策的情況下回退或繞過**;新程式碼一律遵守,既有違反處列入文末 gap 表逐步收斂。

**主題與色彩**

- **cyan 是唯一品牌/互動識別色,不是綠。** accent = cyan(`--color-accent`)。互動焦點、選中、主行動一律 cyan 系。logo 不得用混入分類色的全色環漸層稀釋 cyan。
- **狀態語意色與 booru 分類色是兩套軸,不可互借。** 狀態用 `--color-success / --color-warning / --color-danger / --color-accent`;分類用 `--color-t-*`(character / copyright / general / meta / …)。character 綠 ≠ success 綠,meta 黃 ≠ warning 黃,即使色值接近也不可借用。
- **`@theme` 是色彩唯一真相源。** 不維護第二份手抄 hex 色表(現存 `tag-color.ts` 的 `TAG_COLOR` 屬待收斂的雙真相源)。
- **暗色是唯一主題。** 不做亮色主題;深色階梯 canvas/panel/raised/raised-2 與 text/muted/faint 階層固定。

**字體與排版**

- **三字體角色固定:`--font-display`(標題/大數字)、`--font-body`(Inter,內文)、`--font-mono`(JetBrains Mono,機器值:路徑/hash/計數/confidence%/tag token)。** 不新增第四字體角色,不錯位使用(內文不用 mono、機器值不用 body)。
- **type scale 是封閉階。** 字級只能從 type-scale token 取;不得出現 .5px 字級或階外字級。

**技術約束**

- **使用 `@angular/cdk`** 作為 overlay / a11y / 鍵盤導航等行為基礎,不自造同類輪子。
- **元件 CSS 不得用 `@apply`。** Angular 元件樣式隔離下 `@apply` 取不到全域 Tailwind layer。元件內要用 token,直接寫 `var(--…)`(CSS 變數能穿透樣式隔離);要共用 primitive,走全域 `@layer components` 類別。
- **跨元件共用 primitive 放全域 `@layer components`,元件引用,不得逐檔複製貼上。** 若樣式隔離擋到,用受控的全域類別或既定例外機制,不是各檔重抄一份。
- **`localhost`-only、單人、無認證** 的前提不變(來自 CLAUDE.md 鐵則 8);UI 不需要、也不要加帳號 / 權限相關介面。

**既存正面項(勿回退)**

- 全域 `:focus-visible` ring(`styles.css`)、`prefers-reduced-motion` 凍結 transition/animation、裝飾縮圖 `alt=""`、combobox 浮層選項用 `<button>` 非 `<div>`、autocomplete 已支援 Esc / 上下鍵、`<h1>`/`<nav>`/`<main>` landmark、reduced-motion 真覆蓋 —— 這些是已達標項,改動時不得弄壞。

---

## 版面與空間系統

**容器寬度與置中**

- DO:所有單欄 manage 頁(roots / reconcile / saved-searches / import-confirm / tags)套用統一內容容器 —— `max-width` 約 `880–960px` + `margin:0 auto`,以 `tag-manager` 為基準收斂。寬螢幕內容置中、邊緣留白對稱。
- DO:**清單型**頁面(roots、reconcile 的列)在限寬容器內維持單欄;**卡片型**(saved-searches)用 `repeat(auto-fill, minmax(260px, 1fr))` 讓寬度自動填欄。
- DONT:不要讓 `.panel-pad` / `<table>` / 列容器在無 `max-width` 下吃滿全寬 —— 這是 roots 頁「擠左上 + 右側大片空白」的直接成因。
- DONT:同一導覽層級(manage)的頁面不要一頁置中、其餘全寬並存。

**間距 scale**

- DO:間距只從 `@theme` 的 spacing token 取值(4px 基準:`--space-1:4 / -2:8 / -3:12 / -4:16 / -5:20 / -6:24 / -8:32 / -10:40`)。所有 padding / gap / margin 由此挑。
- DO:**頁面層級水平內距全站統一為單一值**(收斂為 `24px`),讓 gallery chrome(topbar/toolbar/masonry)與 manage 的 panel-pad 對齊,消除切頁時左緣跳動。
- DONT:不要再新增任意 px 字面值(`11 / 13 / 15 / 18px` 之類)做間距。落不進 scale 的值,先問是否真的需要。

**頁首與區塊節奏**

- DO:頁首節奏(`.vhead` 約 `20/24/4`、`.panel-pad` 約 `16/24/40`)固化為共用 layout primitive(`.page-head` / `.page-body`),所有 manage 頁引用同一份。
- DONT:不要在各 feature css 各自複製 vhead / panel-pad / note 規則(目前 4–5 檔重複,易漂移)。

**icon rail(activity bar)**

- DO:rail 寬度(58)、按鈕尺寸(40)、active 指示條偏移以 token / 對齊 spacing scale 表達;active bar 用對齊 padding 的值,取代 `left:-12px` magic number。
- DO:rail 內部 `gap` 與 `padding` 採與其他面板同一節奏(8 / 12),不要 rail 用 4、面板用 8/12。

**RWD 斷點**

- DO:定義全站命名斷點階(沿用 gallery 既有臨界值收斂):`≥1500`、`1180–1500`、`<1180` 三段,寫進準則供所有頁共用。
- DO:窄視窗(`<1180`)時 manage 頁水平內距可降一階(24→16),`import-confirm` 表格允許橫向捲動或欄折疊。
- DONT:不要把斷點當 gallery 私有的兩條 inline 規則。本機桌面窄視窗風險低,可低優先,但準則需明列「目前 manage 無斷點」為已知 gap,不是「不需要」。

---

## 色彩與主題

(總綱見「已定案」中的 cyan / 雙軸 / 單一真相源三條,此處給可執行細則。)

**取色**

- DO:以 `@theme` 為色彩唯一真相源;元件一律用 `var(--color-*)` 或 Tailwind utility(`bg-raised` / `text-muted`),不直接寫 hex。
- DO:tag kind 色由 `@theme` 的 `--color-t-*` 單向供給;TS 端若必須取色,從 CSS 變數讀(`getComputedStyle`)或由建置流程從同一份 token 生成,**不維護第二份手抄 hex**。
- DO:為高頻半透明變體建 token —— `--color-accent-soft`(hover 底)、`--color-accent-ring`(focus / 選中陰影)、`--color-hair-hover`(取代散落的 `#3b4150`),取代手寫 rgba magic number。
- DONT:不要在元件 CSS / 模板寫裸 `#hex`,或裸 `rgba(34,211,238,…)`(accent)、`rgba(251,191,36,…)`(meta / warning)。這些必須是 token。

**語意分軸(呼應已定案)**

- DO:狀態語意用 `--color-success / --color-warning / --color-danger / --color-accent`;toast 成功改用 `--color-success`、saved-search special 改用 `--color-warning`。
- DO:`accent-ink`(accent 上的深色墨水)全站同一個值(收斂掉現存 `#06323b` 與 `#04222a` 兩值並存)。
- DONT:不要用 booru 分色當狀態色(character 綠 ≠ success、meta 黃 ≠ warning),即使色值接近也不可借。
- DONT:不要寫 `var(--未定義 token, 退場 hex)` —— token 拼錯時退場值會靜默生效造成色偏(現存 `--color-ink` 應為 `--color-text`、`var(--color-t-character, #f0616d)` 綠 token 退紅屬筆誤)。token 名一律對齊 `@theme`。

**對比**

- DO:正文文字維持對 canvas 高對比;`muted` / `faint` 用於次要文字時逐處核對對比 —— `faint`(`#6b7280`)用於 placeholder 與次要圖示尚可,但用於正文小字對 raised-2 會逼近 AA 下限,需避免或改用 muted。
- DONT:不要在小字正文濫用 `faint`。

---

## 字體與層級

**type scale(封閉階)**

- DO:在 `@theme` 建封閉 type-scale token,字級只准取這幾階(建議 7 階,每階綁定建議 line-height):`--text-h1: 21px / --text-h2: 16px / --text-title: 14px / --text-body: 13px / --text-sm: 12px / --text-xs: 11px / --text-2xs: 10px`。
- DONT:不要再出現 .5px 字級或階外字級(`8 / 9 / 9.5 / 15 / 22` 一律收斂進最近的階)。新元素一律從 token 挑,不手填 px。
- DONT:不要用 0.5px 差製造層級;層級差至少跨一個 token 階,否則改用 weight / color 區分。

**三角色(display / body / mono)**

- DO:把三角色固化成可複用 utility / primitive(`.u-display` / `.u-mono`,或 Tailwind `font-display` / `font-mono`),元件掛 class,不再逐處寫 `font-family: var(--font-mono)`。
- DO:mono 只用於機器值 —— 絕對路徑、SHA / hash、計數、confidence%、tag token 輸入、技術 badge。display 只用於頁標題與大數字。其餘一律 body。
- DONT:不要在敘述性說明文字(`.vhead p`、note)用 display 或 mono。

**頁標題與 vhead**

- DO:`.vhead` / `.vhead h1` / `.vhead p` 抽成全域 primitive,所有 manage 頁與 tag-manager 共用一份:`h1 = --text-h1 / font-display / weight 600 / line-height 1.25`;`p = --text-body / muted / line-height 1.5`。
- DONT:不要讓頁標題字級在 21 / 22 / 16 之間各自漂移 —— 頁標題一律 `--text-h1`,面板內標題一律 `--text-h2`。

**行高**

- DO:line-height 跟字級綁定:標題(≥16px)收緊 1.2–1.3,內文 1.5,單行 badge / pill 用 1(置中靠 padding)。寫進 type-scale token 註解。
- DONT:不要讓 21px 標題吃預設 1.5(過鬆),也不要對多行內文用 1。

**數字(tabular)**

- DO:會在欄位中對齊或會即時變動的數字加 `font-variant-numeric: tabular-nums`(建議做 `.u-num` primitive):側欄 facet 計數、grid 張數 / 總數、confidence%、reconcile / import 計數。
- DONT:純散文裡的數量不必 tabular,避免全站濫加。

**小標題(eyebrow)**

- DO:section / eyebrow 小標一律沿用既有 `.facet-t` 範式(11px / 600 / uppercase / tracking 0.08em / muted),需要時抽成共用 `.eyebrow`,不各頁各寫一份。

---

## 元件與樣式 pattern

本節最高槓桿的系統性問題是:**primitive 已存在,卻因元件樣式隔離被各檔複製貼上**。準則的核心是「引用,不複製」。

**按鈕**

- DO:全站只用一套 `.btn` 家族 —— `.btn`(預設=次要)、`.btn-primary`(主行動,accent 實心)、`.btn-ghost`(無底)、`.btn-danger`(破壞性);尺寸用 `.btn-sm` 修飾子。新按鈕一律加 class。
- DO:命名統一用連字號修飾子 `.btn-primary`,不要 `.btn.primary`(複合選擇器)。
- DONT:不要在 feature 檔重新發明平行按鈕(`.b` / `.sel-mini` / `.mini` / `.addtag-btn` / `.btn-del`)。要更小尺寸就加 `.btn-sm`,不另起爐灶。
- DONT:danger 色一律 `var(--color-danger)`,絕不用 `var(--color-t-*)`(那是 booru 分類軸)。

**輸入**

- DO:文字輸入一律 `.input`(小尺寸加 `.input-sm`);focus 統一交給全域 `:focus-visible` ring,不各頁自刻 box-shadow。
- DONT:不要再新增 `.q` / `.af-in` / `.addtag-in` / 裸 `input` 各刻 padding / border / focus。

**資料表**

- DO:選一種 table 實作為準(語意 `<table>` 給靜態列、CSS Grid 給需 inline 編輯 / 選取的列),抽出共用 `.thead` / 列 hover / 右對齊數字欄 / 分隔線的 token 化規則。
- DONT:不要每個列表頁各刻 uppercase 11px muted 表頭與 hover 規則(現 import-confirm `<table>` 與 tag-manager grid 假表各寫一遍)。

**empty-state / note / callout**

- DO:分清三種語意各給一個 primitive —— `.note`(藍 / accent info 條)、`.empty-state`(置中、可選 icon + 主文 + sub,給「真的沒資料」)、`.callout-warn` / `.callout-danger`(警示)。空狀態用 `.empty-state`,不拿藍 `.note` 充當。
- DONT:不要在 reconcile / import / saved 各複製一份 `.note` 的 `rgba(34,211,238,…)` + `#bfeff7`。

**badge / pill / chip**

- DO:抽一個 `.badge` 基底(小圓角 + 1px border + 半透明 currentColor 底 + 10–11px),用 `[style.color]` 注 kind 色;tag chip、來源徽章(src)、pill、dag、multi、dupflag 全建在它上面。
- DONT:不要每個小色塊重刻 `padding:2px 6px; border-radius:5px; font-size:10px`。

**primitive 散播機制(本維度最關鍵)**

- DO:跨元件共用 primitive(`.dot` / `.facet-t` / `.vhead` / `.note` / `.badge` / `.btn` / `.input`)放全域 `@layer components`,元件透過全域類別共用;若樣式隔離擋到,用受控的全域樣式 / 既定例外機制,不是各檔重抄。
- DONT:看到「這個 8px 圓點 / 這個 uppercase 小標 / 這個頁首」第 N 次出現就 copy 一份 —— 它已是 primitive,缺的是引用路徑。

---

## 互動態與動效

**狀態四態(hover / active / focus-visible / disabled)**

- DO:任何可點元件(button、`role=button`、`.frow` / `.saved` / `.seg button` / `.act` / `.cat-item` / `.combo-row` 等)四態齊備:至少 hover + active + disabled,focus 交給全域 `:focus-visible`。
- DO:自訂輸入 / select 若 `outline:none`,**必須**自繪等效焦點指示(border-color + `box-shadow:0 0 0 3px` accent 0.12 光暈),且務必用 `:focus-visible`(不是裸 `:focus`)。
- DONT:不要只寫 hover 就交件(目前多數元件的現況)。
- DONT:不要用裸 `:focus` 取代 `:focus-visible`(滑鼠點擊會殘留 ring);若用 `:focus` 是為了內容態(combobox 開啟)需明確註記。

**loading / skeleton**

- DO:列表 / 卡片 / 網格首次載入(`loading() && length===0`)走 `.skeleton` 佔位塊維持版面骨架;只有「載入更多」這種已知版面的增量載入才用文字。
- DONT:不要讓 `.skeleton` 繼續當死碼 —— 要嘛接上,要嘛刪掉,別留「定義了沒用」。

**transition 時長與緩動**

- DO:transition 時長只用 `--dur-fast`(150ms,微互動)、`--dur-base`(200ms,浮層 / 位移),緩動一律 `var(--ease-out)`。元件 CSS 無法 `@apply`,但可直接 `transition: … var(--dur-fast) var(--ease-out)`(CSS 變數穿透隔離)。
- DONT:不要再出現 `0.13s / 0.14s / 0.16s / 0.18s / 0.3s` 手寫魔術數字與裸 ease。

**reduced-motion**

- DO:保留全域 `prefers-reduced-motion` 凍結 transition / animation 的規則;新增動畫不得繞過它(不在 inline style / JS animations 另開一套不受約束的動效)。
- DONT(可選強化):reduced-motion 下,hover 的 `transform` 終態仍會瞬跳,可額外 `transform:none` / 移除位移,做到「無動」而非「瞬移」。

**layout-shift**

- DO:hover / active 位移只用 `transform`(translate / scale)、`opacity`、`box-shadow`、`border-color`(維持目前 `.tile` 做法)。
- DONT:不要對 `width / height / top / left / margin / padding` 做 transition。

---

## 無障礙(a11y)

**圖示與名稱**

- DO:任何 icon-only 互動元素(nav `.act`、檢視切換、token ×、刪除)必須有持久可及名稱 —— 用 `aria-label`,不只靠 `title` 或 hover tooltip。
- DO:純裝飾 `<svg>` 一律加 `aria-hidden="true"`(或 `focusable="false"`);svg 若是按鈕唯一內容,名稱放父按鈕 `aria-label`。
- DONT:不要用 `opacity:0` / `pointer-events:none` 的 hover-only `.lbl` 當作元素唯一可及名稱 —— hover ≠ focus,鍵盤 / AT 拿不到。

**鍵盤可達(呼應 north star 第 3 條)**

- DO:任何 `(click)` 行為一律掛原生 `<button>`(或 `<a href>`):facet 列、photo tile、search token、saved 刪除一律改 `<button type="button">`。
- DONT:不要用 `<div (click)>` / `<span (click)>` / 無 tabindex 的 `<span role="button">` 當可點元素。退而求其次的 `role=button` span 至少要 `tabindex="0"` + keydown,但首選永遠是換成 `<button>`。
- DONT:不要把互動元素巢狀進另一個互動元素(button 內 button / span[role=button])。卡片若可點又有刪除鈕,改為「容器內含並列兩個 button」,不是 button 套 button。
- DONT:不要讓刪除 / 操作鈕只在 hover 時 render(`@if (hovered())`)—— 對純鍵盤永久不可達。改為常駐、用 CSS 控制視覺顯隱(`opacity` + `:focus-within` 補顯)。

**combobox / 浮層**

- DO:autocomplete 採 ARIA combobox pattern —— `<input role="combobox" aria-expanded aria-controls aria-activedescendant>`;浮層 `role="listbox"`,每列 `role="option"` + `aria-selected`,active 列 id 餵 `aria-activedescendant`。
- DO:保留 Esc 關閉、上下鍵移動(現已有),補上 active 變動時同步 `aria-activedescendant`。優先用 `@angular/cdk` 的 a11y / overlay 能力,不自造。

**焦點可見**

- DO:聚焦樣式統一交給全域 `:focus-visible` ring,元件不關掉它;要客製務必用 `:focus-visible` 且保留 ≥2px、≥3:1 對比的可見指示(box-shadow ring,全站統一)。
- DONT:不要在元件用 `outline:none` 搭裸 `:focus` 後僅以 `border-color` 取代(ring 消失或對比不足)。

**狀態語意**

- DO:互斥切換鈕(檢視 dense / large、排序方向)用 `aria-pressed` 或 `role="radio"` / `aria-selected` 表達當前狀態,不只靠 `.on` 視覺 class。

**結構**

- DO:提供「跳到主內容」skip-link(指向 `<main>`);`<nav>` 加 `aria-label`(如「主導覽」)區分 landmark。

---

## 文案與微文案

**術語(對齊 `tag-color.ts` 的 `KIND_LABEL` 為權威)**

- DO:對使用者一律稱「標籤」(名詞);介面文字不出現英文 `tag`(程式碼識別子不在此限)。
- DO:動作用完整動詞片語 —— 「加標籤 / 移除標籤 / 重新標註 / 改標籤類別」。
- DO:WD14 稱「自動標籤」,手動策展稱「我的標籤」,路徑來的稱「資料夾標籤」,與 `KIND_LABEL` 對齊。
- DONT:不要把「標籤」縮成單字「標」當名詞 / 動詞(禁「待標 / 套精準標 / 查無此標 / 不產標籤」)。

**一致性(同義不同詞)**

- DO:同一概念全站一個詞。「收藏的搜尋」與其動作對齊 —— 動作改「收藏此搜尋」、清單維持「收藏的搜尋」(或全用「儲存」,二選一,不可一頁收藏、一頁儲存)。
- DO:狀態回饋用「已+動詞」鏡像原動作的同一個詞(已收藏 / 已移到圖庫外 / 已標記刪除)。reconcile 已做到,推廣全站。

**empty state**

- DO:每個 empty state 一句「現況」+ 一個「下一步」(頁內 CTA,或明確指出去哪個頁做什麼)。
- DONT:不要只丟「沒有符合的圖片」這種死路;至少補「試著放寬篩選,或到『圖庫來源』重新掃描」。

**錯誤訊息**

- DO:錯誤一律加動作情境前綴(「載入失敗:」「儲存失敗:」),口吻一致;裸吐 `{{error()}}` 一律包前綴。
- DONT:不要把後端欄位名 / 內部術語(`taken_at`、hash 欄名、`source`)直接給使用者;要呈現就改人話。

**按鈕用詞**

- DO:按鈕＝動詞開頭、結果可預期;成對動作用對稱詞(套用全部 / 略過全部)。
- DO:歧義詞自解釋 —— 「重標失敗」改「重試標註失敗的圖片」之類,不靠 title 救。

**placeholder / 微提示**

- DO:placeholder 只描述「該填什麼」(「搜尋標籤」「過濾標籤」「來源資料夾路徑」)。
- DONT:不要把鍵盤操作 / 格式規則 / 大小寫規則塞進 placeholder;教學移到 hint 列或 tooltip。

**標點與語氣**

- DO:省略號統一用 `…`;說明句用友善完整句,少用「→ / · / ↟」這類需腦補的符號做正文。
- DONT:不要留任何英文預設樣板文字或 emoji(清掉 `app.html` Angular 預設殘留)。

---

## 現況待補 gap(對照準則)

下表彙整七維度的 gap,依**槓桿 / 風險**排優先級。**成本** S/M/L;**來源** 標註是否已被既有計畫 / spec 涵蓋(`gallery-improvements` 計畫、`async-scan` spec、待寫的 a11y spec),或屬本次新發現。**最高槓桿先做的是「primitive 已存在卻被複製」這類純去重、零行為風險的項。**

### P0 — 純去重 / 一致性收斂(S/M,零或低行為風險,先做)

| 項目 | 違反準則 | 成本 | 來源 |
|---|---|---|---|
| ✅(2026-06-29)抽 `.vhead` / `.note` / `.dot` / `.facet-t` 為全域 primitive,刪 4–5 份複製 | primitive 散播;頁首節奏;eyebrow | M | 新發現(Spec 1 地基已備 primitive 機制) |
| ✅(2026-06-29)四 manage 頁 `.panel-pad` 補 `max-width + margin:auto`(roots 擠左+右空根因);tag-manager 880 對齊 | 容器寬度置中 | S | 新發現 |
| 頁面水平內距統一單值(gallery 18 vs manage 24),消除切頁左緣跳動 | 間距 scale | S | 新發現 |
| tag-manager `.b*` 按鈕收斂到全域 `.btn*`;順手修 danger 用錯 token(`--color-t-character`→`--color-danger`) | 按鈕;danger 色軸;退場 token | S | 新發現 |
| `.btn.primary`→`.btn-primary`,刪 photo-grid 手抄的 `.btn.primary` | 按鈕命名 | S | 新發現 |
| `accent-ink` 兩值(`#06323b` / `#04222a`)收斂為一 | 語意分軸 / token 唯一真相 | S | 新發現 |
| toast token 名漂移(`--color-ink`→`--color-text`、`--color-panel` fallback 對不上)修正 | 退場 token | S | 新發現 |
| danger 裸值(confirm / toast)token 化;`var(--color-t-character,#f0616d)` 筆誤修正 | danger 色軸;退場 token | S | 新發現 |
| ✅(2026-06-29)`#3b4150` 抽既有 token `--color-hair-strong`(.btn/.input/.sel-mini/.root-tab/.saved hover);`.note` 藍 rgba 改 `color-mix(accent)` + 文字 `--color-info-ink` | 取色 token 化 | S | 新發現 |
| ✅(2026-06-29)新增 `--text-h1`/`--text-h2` token;頁標題 vhead + tag-manager 統一 `var(--text-h1)`(21)。inspector(h2/16)仍用裸值,待 P1 type-scale 收斂 | 頁標題 | S | 新發現 |
| `.skeleton` 死碼:接上首屏 loading 或刪除(二選一) | loading / skeleton | M | 部分屬 `gallery-improvements` |
| `app.html` Angular 英文 + emoji 殘留清除 | 文案;勿留樣板 | S | 新發現 |

### P1 — 系統 token 化 / 機械替換(M,需逐處驗證視覺不變)

| 項目 | 違反準則 | 成本 | 來源 |
|---|---|---|---|
| 建 spacing scale token,全站散落 px 間距逐處歸階 | 間距 scale | M | 新發現 |
| 建 type-scale token,16 個散落字級(含 .5px)逐處歸階 | type scale 封閉階 | L | 新發現 |
| 建三角色 utility(`.u-mono`/`.u-display`),60+ 處逐處替換 `font-family: var(--font-*)` | 三角色固化 | M | 新發現 |
| transition 時長 / 緩動走 `--dur-*` / `--ease-out`,~10 處魔術數字機械替換 | transition token | M | 新發現 |
| 半透明 cyan `rgba(34,211,238,…)` ×19 抽 `--color-accent-soft` / `-ring`,各透明階收斂 | 取色 token 化 | M | 新發現 |
| `TAG_COLOR` 雙真相源收斂(單向供給 + 補 `--color-t-expression`) | 色彩唯一真相源 | M | 新發現 |
| logo conic-gradient 混 4 分類色,重設計回 cyan 系識別 | cyan 唯一識別 | M | 新發現 |
| 加 `.u-num`(tabular-nums)並掛上計數點(facet / grid / confidence / reconcile / import) | 數字 tabular | S | 新發現 |
| 抽 `.eyebrow`,各頁 group 小標統一沿用 `.facet-t` 範式 | eyebrow | M | 新發現 |
| `.input` 收斂(`.q`/`.af-in`/`.addtag-in`/裸 input),focus 統一走 `:focus-visible` | 輸入 primitive | M | 新發現 |
| 可點元件補 `:active` / `:disabled`(`.frow`/`.ac-row`/`.combo-row`/`.cat-item`/`.seg`/`.act`/`.saved`/`.b`) | 四態齊備 | M | 部分屬 `gallery-improvements` |
| 文案正名:「標」縮寫全面改「標籤」;「收藏 vs 儲存」對齊;empty CTA;error 前綴;`taken_at` 改人話;placeholder 精簡 | 文案多條 | M | 新發現 |

### P2 — a11y 結構性改造(M/L,需專門 spec)

| 項目 | 違反準則 | 成本 | 來源 |
|---|---|---|---|
| facet 樹整列 / 展開鈕 `<div>/<span>(click)` 改 `<button>`(鍵盤完全不可達) | 鍵盤可達 | M | 待寫 a11y spec |
| photo tile 選圖、search token 改 `<button>`(顧 masonry 樣式) | 鍵盤可達 | M | 待寫 a11y spec |
| saved 刪除鈕重構:解巢狀雙 button + 常駐(非 hover-only) | 鍵盤可達 / 巢狀 / hover-only | M | 待寫 a11y spec |
| 搜尋 / 加標籤 combobox 補 `role=combobox/listbox/option` + `aria-*`(兩處同型) | combobox | M | 待寫 a11y spec |
| shell nav 六項加 `aria-label`;全站 `<svg>` 加 `aria-hidden`;skip-link;`<nav> aria-label` | 圖示名稱 / 結構 | S | 待寫 a11y spec |
| `:focus`+`outline:none` 四處改 `:focus-visible` / 補統一 ring(`.rename` 無焦點指示尤急) | 焦點可見 | S | 待寫 a11y spec |
| 檢視切換 `.seg` / 排序表頭加 `aria-pressed` / 改 button;import `.cat-item` 補 `aria-selected` | 狀態語意 / 鍵盤 | S–M | 待寫 a11y spec |

### gap 維護備註

- **RWD 已知缺口:** manage 頁全無斷點(僅 gallery 有兩條 inline 規則)。本機桌面窄視窗風險低,**列為已知 gap、低優先**,但準則要求新頁面至少在 `<1180` 有可預期行為。
- **對比待核:** `faint` 用於正文小字對 raised-2 逼近 AA 下限,屬 **L**(需逐處核對用途),不是單點修。
- 收斂順序建議:**P0 先清(立刻消最多「primitive 已存在卻被複製」的系統債,風險近零)→ P1 token 化(地基穩固後機械替換)→ P2 a11y(獨立 spec,改 DOM 結構需測鍵盤流程)**。每個切片 build + `ng build` 綠燈、手測視覺不變再 commit(對齊 CLAUDE.md 驗證約定)。