import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, beforeEach, expect, it, vi } from 'vitest';
import { TransactionTable } from './transaction-table';
import { Transaction } from '../../plaid.service';
import { TranslationService } from '../../i18n/translation.service';

function makeTransaction(overrides: Partial<Transaction> = {}): Transaction {
  return {
    transactionId: 't1',
    accountId: 'a1',
    itemId: 'i1',
    institutionName: 'Bank',
    amount: 10,
    isoCurrencyCode: 'USD',
    date: '2026-01-01',
    name: 'Test',
    merchantName: 'Test',
    pending: false,
    category: 'GENERAL_MERCHANDISE',
    isCategoryCustom: false,
    ...overrides,
  };
}

describe('TransactionTable', () => {
  let fixture: ComponentFixture<TransactionTable>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TransactionTable],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(TransactionTable);
    TestBed.inject(TranslationService).setLang('pt');
    fixture.componentInstance.transactions = [makeTransaction()];
    fixture.componentInstance.editableCategory = true;
    fixture.detectChanges();
  });

  // Regressão: o <select> de categoria não estava pré-selecionando a categoria atual da
  // transação (mostrava sempre a primeira opção da lista) — corrigido usando [selected] em
  // cada <option> em vez de [value] no <select>.
  it('pre-selects the transaction current category when editing starts', () => {
    const button: HTMLButtonElement = fixture.nativeElement.querySelector(
      '.transaction-table__category-button',
    );
    button.click();
    fixture.detectChanges();

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('select');
    expect(select.value).toBe('GENERAL_MERCHANDISE');
  });

  it('does not emit categoryChange when the selected value matches the current category', () => {
    const listener = vi.fn();
    fixture.componentInstance.categoryChange.subscribe(listener);

    (fixture.componentInstance as any).onCategorySelected(makeTransaction(), 'GENERAL_MERCHANDISE');

    expect(listener).not.toHaveBeenCalled();
  });

  it('emits categoryChange with the new category when it differs', () => {
    const listener = vi.fn();
    fixture.componentInstance.categoryChange.subscribe(listener);
    const transaction = makeTransaction();

    (fixture.componentInstance as any).onCategorySelected(transaction, 'ENTERTAINMENT');

    expect(listener).toHaveBeenCalledWith({ transaction, category: 'ENTERTAINMENT' });
  });

  it('inverts the Plaid sign so a positive stored amount displays as an outflow', () => {
    const formatted: string = (fixture.componentInstance as any).formatAmount(
      makeTransaction({ amount: 25, isoCurrencyCode: 'USD' }),
    );
    expect(formatted).toContain('25');
    expect(formatted.trim().startsWith('-')).toBe(true);
  });
});
