import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { TranslationService } from '../../i18n/translation.service';
import { Card } from '../cards.service';
import { CardSetup } from './card-setup';

function card(overrides: Partial<Card> = {}): Card {
  return {
    accountId: 'a1', name: 'Adv Credit', officialName: null, institutionName: 'Bank of America',
    currentBalance: 0, creditLimit: 5000, isoCurrencyCode: 'USD', utilizationPercent: 0,
    targetPercent: 30, targetIsCustom: false, amountToPay: 0, statementClosingDay: null,
    nextClosingDate: null, paymentDeadline: null, daysUntilPaymentDeadline: null,
    nextPaymentDueDate: null, minimumPaymentAmount: null, isOverdue: null, needsAlert: false,
    manualCreditLimit: null, manualNextPaymentDueDate: null, lastAlertSentDate: null, nickname: null,
    ...overrides,
  };
}

describe('CardSetup', () => {
  let fixture: ComponentFixture<CardSetup>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CardSetup],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    TestBed.inject(TranslationService).setLang('pt');
    fixture = TestBed.createComponent(CardSetup);
    el = fixture.nativeElement as HTMLElement;
  });

  function render(cards: Card[]): void {
    fixture.componentRef.setInput('cards', cards);
    fixture.detectChanges();
  }

  it('renders nothing when every card is fully configured', () => {
    render([card({ statementClosingDay: 10, nextPaymentDueDate: '2026-10-20' })]);

    expect(el.querySelector('.card-setup')).toBeNull();
  });

  it('asks only for the fields that are missing', () => {
    render([card({ statementClosingDay: 10, nextPaymentDueDate: null })]);

    const labels = Array.from(el.querySelectorAll('label')).map((l) => l.textContent?.trim());
    expect(labels).toEqual(['Próximo vencimento']);
  });

  it('keeps Save disabled until something is filled in', () => {
    render([card()]);

    expect((el.querySelector('.card-setup__fields button') as HTMLButtonElement).disabled).toBe(true);
  });

  it('saves the typed value and re-sends the settings the card already had', () => {
    render([card({ nickname: 'Viagem', targetIsCustom: true, targetPercent: 20, manualCreditLimit: 5000 })]);
    const saved: unknown[] = [];
    fixture.componentInstance.saved.subscribe(() => saved.push(1));

    const closing = el.querySelector('input[type=number]') as HTMLInputElement;
    closing.value = '17';
    closing.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (el.querySelector('.card-setup__fields button') as HTMLButtonElement).click();

    const req = TestBed.inject(HttpTestingController).expectOne('/api/cards/a1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      statementClosingDay: 17,
      targetUtilizationPercent: 20,
      manualCreditLimit: 5000,
      manualNextPaymentDueDate: null,
      nickname: 'Viagem',
    });
    req.flush(null);
    expect(saved.length).toBe(1);
  });
});
