import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './auth/auth.service';
import { BillingService } from './billing/billing.service';
import { TranslationService } from './i18n/translation.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('shows a loading state before the session check resolves', () => {
    const fixture = TestBed.createComponent(App);
    TestBed.inject(TranslationService).setLang('pt');
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Carregando');
  });

  describe('side menu', () => {
    function renderLoggedIn() {
      const fixture = TestBed.createComponent(App);
      TestBed.inject(TranslationService).setLang('pt');
      const auth = TestBed.inject(AuthService);
      auth.user.set({ id: 'u', email: 'a@b.c', name: 'Felipe', picture: null });
      auth.checkedSession.set(true);
      fixture.detectChanges();
      return fixture;
    }

    beforeEach(() => localStorage.removeItem('target30.menuCollapsed'));

    it('lists every page in the sidebar', () => {
      const el = renderLoggedIn().nativeElement as HTMLElement;

      const labels = Array.from(el.querySelectorAll('.sidebar__label')).map((s) => s.textContent?.trim());
      expect(labels).toEqual([
        'Dashboard',
        'Contas',
        'Transações',
        'Cartões',
        'Melhor cartão',
        'Fluxo de Caixa',
        'Configurações',
      ]);
    });

    it('collapses and expands with the arrow, remembering the choice', () => {
      localStorage.setItem('target30.menuCollapsed', '0');
      const fixture = renderLoggedIn();
      const el = fixture.nativeElement as HTMLElement;
      const layout = el.querySelector('.layout') as HTMLElement;
      const toggle = el.querySelector('.sidebar__toggle') as HTMLButtonElement;
      expect(layout.classList).not.toContain('layout--collapsed');

      toggle.click();
      fixture.detectChanges();
      expect(layout.classList).toContain('layout--collapsed');
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(localStorage.getItem('target30.menuCollapsed')).toBe('1');

      toggle.click();
      fixture.detectChanges();
      expect(layout.classList).not.toContain('layout--collapsed');
      expect(localStorage.getItem('target30.menuCollapsed')).toBe('0');
    });

    it('starts collapsed when the user left it collapsed last time', () => {
      localStorage.setItem('target30.menuCollapsed', '1');
      const el = renderLoggedIn().nativeElement as HTMLElement;

      expect(el.querySelector('.layout')?.classList).toContain('layout--collapsed');
    });

    it('shows the subscription entry and a no-access banner only when billing is on and access is lost', () => {
      const fixture = renderLoggedIn();
      const el = fixture.nativeElement as HTMLElement;
      const labels = () => Array.from(el.querySelectorAll('.sidebar__label')).map((s) => s.textContent?.trim());
      expect(labels()).not.toContain('Assinatura');
      expect(el.querySelector('.layout__banner')).toBeNull();

      TestBed.inject(BillingService).status.set({
        enabled: true, hasAccess: false, reason: 'trial_ended', status: 'trial', trialEndsAt: null,
        currentPeriodEnd: null, canManage: false, checkoutAvailable: true, priceLabel: null,
      });
      fixture.detectChanges();

      expect(labels()).toContain('Assinatura');
      expect(el.querySelector('.layout__banner')?.textContent).toContain('suspenso');
    });
  });
});