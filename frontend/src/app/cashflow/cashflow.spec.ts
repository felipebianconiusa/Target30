import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { TranslationService } from '../i18n/translation.service';
import { CashFlow } from './cashflow';

describe('CashFlow', () => {
  it('shows the balance before and after each line', async () => {
    await TestBed.configureTestingModule({
      imports: [CashFlow],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    TestBed.inject(TranslationService).setLang('pt');
    const fixture = TestBed.createComponent(CashFlow);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.match((r) => r.url === '/api/bills').forEach((r) => r.flush([]));
    http.match((r) => r.url === '/api/bills/detected-subscriptions').forEach((r) => r.flush([]));
    http.expectOne((r) => r.url === '/api/cashflow').flush({
      startingBalance: 1050,
      currentBalance: 1000,
      entries: [
        {
          date: '2026-09-18',
          description: 'Grocery Store',
          amount: -50,
          balance: 1000,
          balanceBefore: 1050,
          status: 'Done',
        },
      ],
    });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const headers = Array.from(el.querySelectorAll('th')).map((th) => th.textContent?.trim());
    expect(headers).toContain('Saldo antes');
    expect(headers).toContain('Saldo depois');

    const cells = Array.from(el.querySelectorAll('tbody td')).map((td) => td.textContent?.replace(/\s/g, ' '));
    expect(cells[2]).toContain('1.050,00');
    expect(cells[3]).toContain('50,00');
    expect(cells[4]).toContain('1.000,00');
  });

  it('highlights a projected low balance with the date and the entry that causes it', async () => {
    await TestBed.configureTestingModule({
      imports: [CashFlow],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    TestBed.inject(TranslationService).setLang('pt');
    const fixture = TestBed.createComponent(CashFlow);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.match((r) => r.url === '/api/bills').forEach((r) => r.flush([]));
    http.match((r) => r.url === '/api/bills/detected-subscriptions').forEach((r) => r.flush([]));
    http.expectOne((r) => r.url === '/api/cashflow').flush({
      startingBalance: 100,
      currentBalance: 100,
      entries: [],
      lowBalance: {
        date: '2026-09-25',
        balance: -400,
        description: 'Rent',
        alreadyBelow: false,
        minimumBalance: -400,
      },
      lowBalanceThreshold: 0,
    });
    fixture.detectChanges();

    const banner = (fixture.nativeElement as HTMLElement).querySelector('.cashflow-page__low-balance');
    expect(banner?.textContent).toContain('Saldo baixo');
    expect(banner?.textContent).toContain('2026-09-25');
    expect(banner?.textContent).toContain('Rent');
  });

  it('lists detected income in the manager and confirming one creates it', async () => {
    await TestBed.configureTestingModule({
      imports: [CashFlow],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    TestBed.inject(TranslationService).setLang('pt');
    const fixture = TestBed.createComponent(CashFlow);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.match((r) => r.url === '/api/bills').forEach((r) => r.flush([]));
    http.match((r) => r.url === '/api/income').forEach((r) => r.flush([]));
    http.match((r) => r.url === '/api/income/detected').forEach((r) => r.flush([]));
    http.expectOne((r) => r.url === '/api/cashflow').flush({
      startingBalance: 0, currentBalance: 0, entries: [], lowBalance: null, lowBalanceThreshold: 0,
    });
    fixture.detectChanges();

    (fixture.componentInstance as any).toggleBillsManager();
    http.match((r) => r.url === '/api/bills/detected-subscriptions').forEach((r) => r.flush([]));
    http.match((r) => r.url === '/api/income').forEach((r) => r.flush([]));
    http.match((r) => r.url === '/api/income/detected').forEach((r) =>
      r.flush([{ description: 'Zelle from Layse', amount: 850, frequency: 'weekly', lastDate: '2026-09-18', occurrences: 4 }]),
    );
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.bills-manager__income')?.textContent).toContain('Zelle from Layse');
    expect(el.querySelector('.bills-manager__income')?.textContent).toContain('Semanal');

    const add = Array.from(el.querySelectorAll('.bills-manager__income li button')).find((b) =>
      b.textContent?.includes('Adicionar'),
    ) as HTMLButtonElement;
    add.click();
    const req = http.expectOne((r) => r.url === '/api/income' && r.method === 'POST');
    expect(req.request.body).toEqual({
      description: 'Zelle from Layse', amount: 850, frequency: 'weekly', anchorDate: '2026-09-18',
    });
  });
});