import type { Page } from '@playwright/test';

/**
 * Counts full page loads after the call. framenavigated would also count same-document hash changes
 * (switching tabs updates #hash), so this listens for new documents instead.
 */
export function countNavigations(page: Page) {
  const box = { count: 0 };
  page.on('domcontentloaded', () => { box.count++; });
  return box;
}
