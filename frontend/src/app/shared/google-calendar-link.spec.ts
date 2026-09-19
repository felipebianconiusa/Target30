import { describe, expect, it } from 'vitest';
import { buildGoogleCalendarLink } from './google-calendar-link';

describe('buildGoogleCalendarLink', () => {
  it('builds an all-day event spanning the given date to the next day', () => {
    const url = buildGoogleCalendarLink({
      title: 'Pay Card',
      date: '2026-10-02',
      details: 'Pay $100',
    });

    expect(url).toContain('action=TEMPLATE');
    expect(url).toContain('dates=20261002%2F20261003');
  });

  it('rolls over to the next month correctly at month end', () => {
    const url = buildGoogleCalendarLink({ title: 'x', date: '2026-01-31', details: 'y' });
    expect(url).toContain('dates=20260131%2F20260201');
  });

  it('rolls over on a leap-year February 29th', () => {
    const url = buildGoogleCalendarLink({ title: 'x', date: '2028-02-29', details: 'y' });
    expect(url).toContain('dates=20280229%2F20280301');
  });

  it('URL-encodes the title and details', () => {
    const url = buildGoogleCalendarLink({
      title: 'Pagar Cartão',
      date: '2026-01-01',
      details: 'Valor: R$ 100,00',
    });

    expect(url).toContain('text=Pagar+Cart%C3%A3o');
  });
});
