import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TranslationService } from '../i18n/translation.service';
import { Accounts } from './accounts';

const ITEM = {
  itemId: 'item-1',
  institutionName: 'Bank of America',
  connectedAt: '2026-09-01T00:00:00Z',
  lastSyncedAt: '2026-09-19T20:00:00Z',
};

describe('Accounts', () => {
  let fixture: ComponentFixture<Accounts>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Accounts],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    TestBed.inject(TranslationService).setLang('pt');
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Accounts);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => vi.restoreAllMocks());

  function load(freshness: unknown[]): void {
    fixture.detectChanges();
    http.expectOne('/api/plaid/items').flush([ITEM]);
    http.expectOne('/api/plaid/items/freshness').flush(freshness);
    fixture.detectChanges();
  }

  const fresh = (overrides = {}) => ({
    itemId: 'item-1',
    plaidLastSuccessfulUpdate: '2026-09-19T14:48:00Z',
    plaidLastFailedUpdate: null,
    errorCode: null,
    lastRefreshRequestedAt: null,
    ...overrides,
  });

  it('shows when Plaid last fetched data from the bank', () => {
    load([fresh()]);

    expect(el.textContent).toContain('Plaid buscou dados novos do banco em:');
  });

  it('warns when the last Plaid attempt failed after the last success', () => {
    load([fresh({ plaidLastFailedUpdate: '2026-09-19T18:00:00Z' })]);

    expect(el.querySelector('.accounts__warning')?.textContent).toContain('falhou');
  });

  it('does not warn about an old failure that was followed by a success', () => {
    load([fresh({ plaidLastFailedUpdate: '2026-09-18T13:56:00Z' })]);

    expect(el.querySelector('.accounts__warning')).toBeNull();
  });

  it('still renders the list when the freshness call fails', () => {
    fixture.detectChanges();
    http.expectOne('/api/plaid/items').flush([ITEM]);
    http.expectOne('/api/plaid/items/freshness').flush('x', { status: 500, statusText: 'err' });
    fixture.detectChanges();

    expect(el.textContent).toContain('Bank of America');
  });

  it('does nothing when the user declines the paid-refresh confirmation', () => {
    load([fresh()]);
    vi.spyOn(window, 'confirm').mockReturnValue(false);

    (el.querySelector('.accounts__refresh') as HTMLButtonElement).click();

    http.expectNone('/api/plaid/items/item-1/refresh');
  });

  it('refreshes after confirmation and reports the result', () => {
    load([fresh()]);
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);

    (el.querySelector('.accounts__refresh') as HTMLButtonElement).click();
    expect(confirmSpy.mock.calls[0][0]).toContain('cobra');
    http.expectOne('/api/plaid/items/item-1/refresh').flush({ updated: true, plaidLastSuccessfulUpdate: null });
    http.expectOne('/api/plaid/items').flush([ITEM]);
    http.expectOne('/api/plaid/items/freshness').flush([fresh()]);
    fixture.detectChanges();

    expect(el.querySelector('.accounts__refresh-message')?.textContent).toContain('trouxe dados novos');
  });

  it('tells the user to wait when the API blocks a repeated refresh', () => {
    load([fresh()]);
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    (el.querySelector('.accounts__refresh') as HTMLButtonElement).click();
    http
      .expectOne('/api/plaid/items/item-1/refresh')
      .flush({ retryAfterSeconds: 420 }, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();

    expect(el.querySelector('.accounts__refresh-message')?.textContent).toContain('7 min');
  });
});
