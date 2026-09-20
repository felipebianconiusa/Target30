import { provideHttpClient, withInterceptors, HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { TranslationService } from '../i18n/translation.service';
import { paymentRequiredInterceptor } from '../auth/payment-required.interceptor';
import { Billing } from './billing';
import { BillingStatus } from './billing.service';

const status = (overrides: Partial<BillingStatus> = {}): BillingStatus => ({
  enabled: true, hasAccess: true, reason: 'trial', status: 'trial', trialEndsAt: new Date(Date.now() + 3 * 86_400_000).toISOString(),
  currentPeriodEnd: null, canManage: false, checkoutAvailable: true, priceLabel: 'US$ 9/mês', ...overrides,
});

describe('Billing page', () => {
  function render(s: BillingStatus): HTMLElement {
    TestBed.configureTestingModule({
      imports: [Billing],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(TranslationService).setLang('pt');
    const fixture = TestBed.createComponent(Billing);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/billing/status').flush(s);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the days left in the free trial and offers to subscribe', () => {
    const el = render(status());

    expect(el.textContent).toContain('Teste grátis: 3 dia(s)');
    expect(el.textContent).toContain('US$ 9/mês');
    expect(el.querySelector('.billing__primary')).not.toBeNull();
  });

  it('says billing is off on a personal installation', () => {
    const el = render(status({ enabled: false }));

    expect(el.textContent).toContain('cobrança está desligada');
    expect(el.querySelector('.billing__primary')).toBeNull();
  });

  it('shows the renewal date and the manage button for an active subscription, without a subscribe button', () => {
    const el = render(status({ status: 'active', reason: 'active', canManage: true, currentPeriodEnd: '2026-10-19T00:00:00Z' }));

    expect(el.textContent).toContain('Assinatura ativa');
    expect(el.textContent).toContain('Próxima renovação');
    expect(el.querySelector('.billing__primary')).toBeNull();
    expect(el.textContent).toContain('Gerenciar assinatura');
  });

  it('warns when the trial has ended and the user has no access', () => {
    const el = render(status({ hasAccess: false, reason: 'trial_ended', trialEndsAt: '2026-01-01T00:00:00Z' }));

    expect(el.textContent).toContain('teste grátis acabou');
  });

  it('does not offer to subscribe when Stripe is not configured', () => {
    const el = render(status({ checkoutAvailable: false }));

    expect(el.querySelector('.billing__primary')).toBeNull();
  });

  it('hides the pay controls for an exempt account', () => {
    const el = render(status({ status: 'none', reason: 'exempt' }));

    expect(el.textContent).toContain('conta isenta');
    expect(el.querySelector('.billing__actions')).toBeNull();
  });
});

describe('paymentRequiredInterceptor', () => {
  it('sends the user to the billing page on a 402 and still surfaces the error', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([paymentRequiredInterceptor])), provideHttpClientTesting(), provideRouter([])],
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const errors: number[] = [];

    TestBed.inject(HttpClient).post('/api/plaid/sync', {}).subscribe({ error: (e) => errors.push(e.status) });
    TestBed.inject(HttpTestingController).expectOne('/api/plaid/sync').flush({}, { status: 402, statusText: 'Payment Required' });

    expect(navigate).toHaveBeenCalledWith(['/billing']);
    expect(errors).toEqual([402]);
  });

  it('leaves other errors alone', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([paymentRequiredInterceptor])), provideHttpClientTesting(), provideRouter([])],
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    TestBed.inject(HttpClient).get('/api/x').subscribe({ error: () => undefined });
    TestBed.inject(HttpTestingController).expectOne('/api/x').flush({}, { status: 500, statusText: 'err' });

    expect(navigate).not.toHaveBeenCalled();
  });
});
