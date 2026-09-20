import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslationService } from '../i18n/translation.service';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, beforeEach, expect, it } from 'vitest';
import { Cards } from './cards';
import { Card } from './cards.service';

function makeCard(overrides: Partial<Card> = {}): Card {
  return {
    accountId: 'acc-1',
    name: 'Test Card',
    officialName: null,
    institutionName: 'Bank',
    currentBalance: 300,
    creditLimit: 1000,
    isoCurrencyCode: 'USD',
    utilizationPercent: 30,
    targetPercent: 30,
    targetIsCustom: false,
    amountToPay: 0,
    statementClosingDay: 10,
    nextClosingDate: '2026-02-10',
    paymentDeadline: '2026-02-10',
    daysUntilPaymentDeadline: 5,
    nextPaymentDueDate: '2026-02-20',
    minimumPaymentAmount: 25,
    isOverdue: false,
    needsAlert: false,
    manualCreditLimit: null,
    manualNextPaymentDueDate: null,
    lastAlertSentDate: null,
    nickname: null,
    ...overrides,
  };
}

describe('Cards', () => {
  let fixture: ComponentFixture<Cards>;
  let component: any;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Cards],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(Cards);
    component = fixture.componentInstance;
  });

  describe('utilizationState', () => {
    it('is "unknown" when there is no limit on file', () => {
      expect(component.utilizationState(makeCard({ utilizationPercent: null }))).toBe('unknown');
    });

    it('is "under" when utilization is at or below target', () => {
      expect(component.utilizationState(makeCard({ utilizationPercent: 30, targetPercent: 30 }))).toBe(
        'under',
      );
    });

    it('is "over" when utilization exceeds target', () => {
      expect(component.utilizationState(makeCard({ utilizationPercent: 45, targetPercent: 30 }))).toBe(
        'over',
      );
    });
  });

  describe('payment simulator', () => {
    it('subtracts the simulated payment from the current balance', () => {
      component.simulateAmount.set(100);
      const card = makeCard({ currentBalance: 300 });
      expect(component.simulatedBalance(card)).toBe(200);
    });

    it('never goes below zero even if the simulated payment exceeds the balance', () => {
      component.simulateAmount.set(500);
      const card = makeCard({ currentBalance: 300 });
      expect(component.simulatedBalance(card)).toBe(0);
    });

    it('computes the resulting utilization percent rounded to one decimal', () => {
      component.simulateAmount.set(100);
      const card = makeCard({ currentBalance: 300, creditLimit: 1000 });
      // (300-100)/1000*100 = 20.0
      expect(component.simulatedUtilization(card)).toBe(20);
    });

    it('returns null utilization when the card has no limit', () => {
      component.simulateAmount.set(100);
      const card = makeCard({ currentBalance: 300, creditLimit: 0 });
      expect(component.simulatedUtilization(card)).toBeNull();
    });
  });

  describe('googleCalendarLink', () => {
    it('produces a link containing the card name and amount', () => {
      const link = component.googleCalendarLink('Test Card', 150, '2026-02-10', 'USD');
      expect(link).toContain('calendar.google.com');
      expect(link).toContain('dates=20260210%2F20260211');
    });
  });

  describe('rendering', () => {
    function renderWith(cards: Card[]): HTMLElement {
      TestBed.inject(TranslationService).setLang('pt');
      fixture.detectChanges();
      const http = TestBed.inject(HttpTestingController);
      http.match('/api/settings').forEach((r) =>
        r.flush({ globalTargetUtilizationPercent: 30, notifyDaysBeforeClosing: 3 }),
      );
      http.match('/api/cards').forEach((r) => r.flush(cards));
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    it('shows the nickname as the title and always keeps the original name visible', () => {
      const el = renderWith([makeCard({ name: 'Savor', nickname: 'Mercado' })]);

      expect(el.querySelector('.card-item strong')?.textContent).toContain('Mercado');
      expect(el.querySelector('.card-item__original')?.textContent).toContain('Savor');
    });

    it('shows only the original name when there is no nickname', () => {
      const el = renderWith([makeCard({ name: 'Savor', nickname: null })]);

      expect(el.querySelector('.card-item strong')?.textContent).toContain('Savor');
      expect(el.querySelector('.card-item__original')).toBeNull();
    });

    it('offers a shortcut to set the limit when the bank did not report one', () => {
      const el = renderWith([makeCard({ creditLimit: 0, utilizationPercent: null })]);

      const button = el.querySelector('.card-item__link-button') as HTMLButtonElement;
      expect(button).not.toBeNull();
      button.click();
      fixture.detectChanges();

      // O atalho abre o painel de edição, onde fica o campo de limite manual.
      expect(el.querySelector('.card-item__edit')).not.toBeNull();
    });

    it('shows the limit (no shortcut) when the bank reported one', () => {
      const el = renderWith([makeCard({ creditLimit: 1000 })]);

      expect(el.textContent).toContain('1.000');
      expect(el.querySelector('.card-item__link-button')).toBeNull();
    });

    it('shows an owner badge and filters the list by owner', () => {
      const el = renderWith([
        makeCard({ accountId: 'a', name: 'Savor Layse', owner: 'Layse' }),
        makeCard({ accountId: 'b', name: 'Savor Felipe', owner: 'Felipe' }),
      ]);
      expect(el.querySelectorAll('.card-item').length).toBe(2);
      expect(Array.from(el.querySelectorAll('.card-item__owner')).map((e) => e.textContent?.trim())).toEqual(
        expect.arrayContaining(['Layse', 'Felipe']),
      );

      component.setOwnerFilter('Layse');
      fixture.detectChanges();

      expect(el.querySelectorAll('.card-item').length).toBe(1);
      expect(el.querySelector('.card-item strong')?.textContent).toContain('Savor Layse');
    });

    it('suggests as owners the last name word that repeats across cards', () => {
      renderWith([
        makeCard({ accountId: 'a', name: 'Savor Layse' }),
        makeCard({ accountId: 'b', name: 'Quicksilver Layse' }),
        makeCard({ accountId: 'c', name: 'Costco Anywhere Visa Citi' }),
      ]);

      expect(component.ownerSuggestions()).toEqual(['Layse']);
    });
  });
});