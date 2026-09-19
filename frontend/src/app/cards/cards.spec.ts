import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
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
});
