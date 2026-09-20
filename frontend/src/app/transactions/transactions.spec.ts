import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TranslationService } from '../i18n/translation.service';
import { Transaction } from '../plaid.service';
import { Transactions } from './transactions';

const TX: Transaction = {
  transactionId: 't1', accountId: 'a', itemId: 'i', institutionName: 'Bank', amount: 10,
  isoCurrencyCode: 'USD', date: '2026-09-01', name: 'COSTCO #99', merchantName: 'Costco',
  pending: false, category: 'GENERAL_MERCHANDISE', isCategoryCustom: false,
};

describe('Transactions category change', () => {
  afterEach(() => vi.restoreAllMocks());

  function setup() {
    TestBed.configureTestingModule({
      imports: [Transactions],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(TranslationService).setLang('pt');
    const fixture = TestBed.createComponent(Transactions);
    return { component: fixture.componentInstance as any, http: TestBed.inject(HttpTestingController) };
  }

  it('asks whether to apply to the whole merchant and sends the answer to the API', () => {
    const { component, http } = setup();
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);

    component.onCategoryChange({ transaction: TX, category: 'FOOD_AND_DRINK' });

    expect(confirmSpy.mock.calls[0][0]).toContain('Costco');
    const req = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/transactions/t1/category'));
    expect(req.request.body).toEqual({ category: 'FOOD_AND_DRINK', applyToMerchant: true });
  });

  it('applies to just that transaction when the user cancels', () => {
    const { component, http } = setup();
    vi.spyOn(window, 'confirm').mockReturnValue(false);

    component.onCategoryChange({ transaction: TX, category: 'FOOD_AND_DRINK' });

    const req = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/transactions/t1/category'));
    expect(req.request.body).toEqual({ category: 'FOOD_AND_DRINK', applyToMerchant: false });
  });
});
