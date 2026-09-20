import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { TranslationService } from '../../i18n/translation.service';
import { UtilizationPlan } from '../cards.service';
import { UtilizationSimulator } from './utilization-simulator';

const plan = (overrides: Partial<UtilizationPlan> = {}): UtilizationPlan => ({
  targetPercent: 30,
  cards: [
    {
      accountId: 'a', name: 'Savor', nickname: 'Mercado', institutionName: 'Capital One', owner: null,
      balance: 500, limit: 1000, utilizationBefore: 50, toPay: 200, utilizationAfter: 30,
      nextClosingDate: '2026-10-10', payBy: '2026-10-09', daysUntilPayBy: 20,
    },
    {
      accountId: 'b', name: 'Quicksilver', nickname: null, institutionName: 'Capital One', owner: null,
      balance: 100, limit: 4000, utilizationBefore: 2.5, toPay: 0, utilizationAfter: 2.5,
      nextClosingDate: null, payBy: null, daysUntilPayBy: null,
    },
  ],
  skippedWithoutLimit: [{ accountId: 'c', name: 'Costco', nickname: null, institutionName: 'Citi' }],
  totalToPay: 200,
  overallBefore: 12,
  overallAfter: 8,
  ...overrides,
});

describe('UtilizationSimulator', () => {
  let fixture: ComponentFixture<UtilizationSimulator>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UtilizationSimulator],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    TestBed.inject(TranslationService).setLang('pt');
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(UtilizationSimulator);
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  const request = () => http.expectOne((r) => r.url === '/api/cards/utilization-plan');

  it('starts on the 30% band and shows what to pay per card, the total and the overall utilization', () => {
    const req = request();
    expect(req.request.params.get('target')).toBe('30');
    req.flush(plan());
    fixture.detectChanges();

    const rows = Array.from(el.querySelectorAll('tbody tr'));
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Mercado (Savor)');
    expect(rows[0].textContent).toContain('US$');
    expect(rows[0].textContent).toContain('50%');
    expect(rows[0].textContent).toContain('30%');
    expect(rows[1].textContent).toContain('—');
    const foot = el.querySelector('tfoot')?.textContent ?? '';
    expect(foot).toContain('12%');
    expect(foot).toContain('8%');
  });

  it('asks again for another band when a chip is clicked', () => {
    request().flush(plan());
    fixture.detectChanges();

    (el.querySelectorAll('.util-sim__band')[2] as HTMLButtonElement).click();

    expect(request().request.params.get('target')).toBe('9');
  });

  it('lists the cards left out for lack of a limit and the no-spend assumption', () => {
    request().flush(plan());
    fixture.detectChanges();

    expect(el.textContent).toContain('Fora da conta');
    expect(el.textContent).toContain('Costco');
    expect(el.textContent).toContain('não gaste mais');
  });

  it('says so when there is nothing to simulate', () => {
    request().flush(plan({ cards: [], totalToPay: 0, overallBefore: null, overallAfter: null }));
    fixture.detectChanges();

    expect(el.textContent).toContain('Nenhum cartão com limite conhecido');
    expect(el.querySelector('table')).toBeNull();
  });
});
