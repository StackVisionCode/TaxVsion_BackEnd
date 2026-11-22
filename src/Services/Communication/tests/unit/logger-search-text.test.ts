import { describe, expect, it } from 'vitest';
import { maskSearchText } from '../../src/infrastructure/logger/logger.js';

describe('logger search text', () => {
  it('masks the search text of a logged url', () => {
    expect(maskSearchText('/communication/notifications?page=2&q=Smith%20Jr&category=Billing')).toBe(
      '/communication/notifications?page=2&q=[REDACTED]&category=Billing',
    );
    expect(maskSearchText('/communication/notifications?q=INV-2026')).toBe(
      '/communication/notifications?q=[REDACTED]',
    );
  });

  it('leaves other parameters alone', () => {
    expect(maskSearchText('/communication/notifications?unreadOnly=true&faq=1')).toBe(
      '/communication/notifications?unreadOnly=true&faq=1',
    );
  });
});
