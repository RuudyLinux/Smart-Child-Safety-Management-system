// Accessibility (axe-core, WCAG 2.1 A/AA) and page performance budgets.
import AxeBuilder from '@axe-core/playwright';
import { test, expect } from '../support/fixtures';

const PAGES = ['/Dashboard', '/Students', '/Students/Create', '/Guardians', '/Guardians/Create', '/Attendance', '/Journey',
  '/Verification', '/Verification/History', '/Transportation', '/Rfid', '/Reports', '/Profile', '/Settings'];

test.describe('Accessibility', () => {
  for (const url of PAGES) {
    test(`no serious or critical axe violations on ${url}`, async ({ page }) => {
      await page.goto(url);
      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
      const serious = results.violations.filter(v => v.impact === 'serious' || v.impact === 'critical');
      const summary = serious.map(v => `${v.id} (${v.impact}): ${v.help} — ${v.nodes.slice(0, 3).map(n => n.target.join(' ')).join(' | ')}`);
      expect(summary, `axe on ${url}`).toEqual([]);
    });
  }

  // Regression: at laptop widths the Transportation table overflowed its card and the
  // scroll was unreachable by keyboard (axe scrollable-region-focusable).
  test('every sideways-scrolling table is reachable by keyboard', async ({ page, data }) => {
    await data.bus();
    await page.setViewportSize({ width: 900, height: 800 });
    for (const url of ['/Transportation', '/Students', '/Attendance', '/Rfid', '/Verification/History']) {
      await page.goto(url);
      const unreachable = await page.locator('.cg-table-wrap').evaluateAll(wraps =>
        wraps.filter(w => w.scrollWidth > w.clientWidth + 1 && w.getAttribute('tabindex') !== '0').length);
      expect(unreachable, url).toBe(0);
      const results = await new AxeBuilder({ page }).withRules(['scrollable-region-focusable']).analyze();
      expect(results.violations.map(v => v.id), url).toEqual([]);
    }
  });

  test('sign-in page is accessible', async ({ browser }) => {
    const ctx = await browser.newContext({ storageState: { cookies: [], origins: [] } });
    const page = await ctx.newPage();
    await page.goto('/Account/Login');
    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
    expect(results.violations.filter(v => v.impact === 'serious' || v.impact === 'critical').map(v => v.id)).toEqual([]);
    await ctx.close();
  });

  test('keyboard: skip through the shell and reach the main content', async ({ page }) => {
    await page.goto('/Students');
    for (let i = 0; i < 40; i++) {
      await page.keyboard.press('Tab');
      if (await page.evaluate(() => !!document.activeElement?.closest('main'))) break;
    }
    expect(await page.evaluate(() => !!document.activeElement?.closest('main'))).toBe(true);
    // Focus is always visible.
    const outline = await page.evaluate(() => getComputedStyle(document.activeElement!).outlineStyle);
    expect(outline).not.toBe('none');
  });
});

test.describe('Performance', () => {
  for (const url of ['/Dashboard', '/Students', '/Journey', '/Reports', '/Verification']) {
    test(`${url} loads within budget`, async ({ page }) => {
      await page.goto(url, { waitUntil: 'load' });
      const t = await page.evaluate(() => {
        const n = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming;
        return { ttfb: n.responseStart, dcl: n.domContentLoadedEventEnd, load: n.loadEventEnd, transfer: n.transferSize };
      });
      test.info().annotations.push({ type: 'timing', description: JSON.stringify(t) });
      expect(t.ttfb, 'server response').toBeLessThan(1500);
      expect(t.load, 'page load').toBeLessThan(3000);
    });
  }

  test('no layout shift after load on the dashboard', async ({ page }) => {
    await page.goto('/Dashboard');
    await page.waitForTimeout(1500);
    const cls = await page.evaluate(() => new Promise<number>(resolve => {
      let total = 0;
      new PerformanceObserver(list => { for (const e of list.getEntries() as any[]) if (!e.hadRecentInput) total += e.value; })
        .observe({ type: 'layout-shift', buffered: true });
      setTimeout(() => resolve(total), 500);
    }));
    expect(cls).toBeLessThan(0.1);
  });

  test('heap stays stable across repeated navigation', async ({ page }) => {
    const heap = async () => (await page.evaluate(() => (performance as any).memory?.usedJSHeapSize ?? 0)) as number;
    await page.goto('/Dashboard');
    const start = await heap();
    for (let i = 0; i < 10; i++) { await page.goto('/Students'); await page.goto('/Dashboard'); }
    const end = await heap();
    test.skip(start === 0, 'performance.memory not available');
    expect(end).toBeLessThan(start * 3 + 5_000_000);
  });
});
