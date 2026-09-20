import { describe, expect, it } from 'vitest';
import { Card } from './cards.service';
import { cardSetupNeeds } from './card-setup';

function card(overrides: Partial<Card> = {}): Card {
  return {
    accountId: 'a', name: 'C', officialName: null, institutionName: 'B', currentBalance: 0,
    creditLimit: 1000, isoCurrencyCode: 'USD', utilizationPercent: 0, targetPercent: 30,
    targetIsCustom: false, amountToPay: 0, statementClosingDay: 10, nextClosingDate: '2026-10-10',
    paymentDeadline: null, daysUntilPaymentDeadline: null, nextPaymentDueDate: '2026-10-20',
    minimumPaymentAmount: null, isOverdue: null, needsAlert: false, manualCreditLimit: null,
    manualNextPaymentDueDate: null, lastAlertSentDate: null, nickname: null,
    ...overrides,
  };
}

describe('cardSetupNeeds', () => {
  it('is empty for a fully configured card', () => {
    expect(cardSetupNeeds(card())).toEqual([]);
  });

  it('asks for the limit when the bank did not report one (Capital One)', () => {
    expect(cardSetupNeeds(card({ creditLimit: 0 }))).toEqual(['limit']);
  });

  it('asks for the closing day and due date when Plaid has no liabilities (BofA, OnePay)', () => {
    expect(cardSetupNeeds(card({ statementClosingDay: null, nextPaymentDueDate: null }))).toEqual([
      'closingDay',
      'dueDate',
    ]);
  });

  it('lists everything that is missing', () => {
    expect(
      cardSetupNeeds(card({ statementClosingDay: null, creditLimit: 0, nextPaymentDueDate: null })),
    ).toEqual(['closingDay', 'limit', 'dueDate']);
  });
});
