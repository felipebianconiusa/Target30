import { Component, OnInit, effect, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './auth/auth.service';
import { GoogleLoginButton } from './auth/google-login-button/google-login-button';
import { TranslationService } from './i18n/translation.service';
import { TranslatePipe } from './i18n/translate.pipe';
import { ThemeService } from './theme/theme.service';
import { BillingService } from './billing/billing.service';
import { Landing } from './landing/landing';

interface NavItem {
  path: string;
  labelKey: string;
  icon: string;
}

const MENU_COLLAPSED_KEY = 'target30.menuCollapsed';
const NARROW_SCREEN_PX = 768;

// Lembra a escolha do usuário; sem escolha guardada, começa recolhido em tela estreita (celular).
function initialCollapsed(): boolean {
  try {
    const stored = localStorage.getItem(MENU_COLLAPSED_KEY);
    if (stored !== null) return stored === '1';
  } catch {
    // storage indisponível (janela privada, bloqueado): cai no padrão por largura
  }
  return typeof window !== 'undefined' && window.innerWidth < NARROW_SCREEN_PX;
}

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, GoogleLoginButton, TranslatePipe, Landing],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App implements OnInit {
  protected readonly navItems: NavItem[] = [
    { path: '/', labelKey: 'nav.dashboard', icon: '🏠' },
    { path: '/accounts', labelKey: 'nav.accounts', icon: '🏦' },
    { path: '/transactions', labelKey: 'nav.transactions', icon: '🧾' },
    { path: '/cards', labelKey: 'nav.cards', icon: '💳' },
    { path: '/best-card', labelKey: 'nav.bestCard', icon: '⭐' },
    { path: '/cashflow', labelKey: 'nav.cashflow', icon: '💸' },
    { path: '/billing', labelKey: 'nav.billing', icon: '💎' },
    { path: '/settings', labelKey: 'nav.settings', icon: '⚙️' },
  ];

  protected readonly menuCollapsed = signal(initialCollapsed());

  // Termos e Política de Privacidade abrem também sem login (o Plaid e o Stripe exigem link público).
  protected readonly onLegalPage = signal(false);

  constructor(
    protected readonly authService: AuthService,
    protected readonly translationService: TranslationService,
    private readonly themeService: ThemeService,
    protected readonly billingService: BillingService,
    private readonly router: Router,
  ) {
    const isLegal = (url: string) => /^\/(terms|privacy)(\/|\?|#|$)/.test(url);
    this.onLegalPage.set(isLegal(this.router.url));
    this.router.events.subscribe((e) => {
      if (e instanceof NavigationEnd) this.onLegalPage.set(isLegal(e.urlAfterRedirects));
    });

    // Assim que há usuário logado, carrega a situação da assinatura (menu e aviso de acesso).
    effect(() => {
      if (this.authService.user()) this.billingService.loadStatus().subscribe({ error: () => undefined });
    });
  }

  ngOnInit(): void {
    this.authService.checkSession();
  }

  protected toggleMenu(): void {
    const collapsed = !this.menuCollapsed();
    this.menuCollapsed.set(collapsed);
    try {
      localStorage.setItem(MENU_COLLAPSED_KEY, collapsed ? '1' : '0');
    } catch {
      // só perde a preferência entre sessões
    }
  }
}
