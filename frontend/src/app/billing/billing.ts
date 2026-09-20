import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { LOCALE_BY_LANG } from '../i18n/translations';
import { BillingService } from './billing.service';

@Component({
  selector: 'app-billing',
  imports: [TranslatePipe],
  templateUrl: './billing.html',
  styleUrl: './billing.scss',
})
export class Billing implements OnInit {
  protected readonly loading = signal(true);
  protected readonly working = signal(false);
  protected readonly error = signal('');
  protected readonly justPaid = signal(false);

  constructor(
    protected readonly billing: BillingService,
    private readonly route: ActivatedRoute,
    private readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    // O Stripe volta pra cá com ?checkout=success depois do pagamento (o webhook pode demorar uns segundos).
    this.justPaid.set(this.route.snapshot.queryParamMap.get('checkout') === 'success');
    this.billing.loadStatus().subscribe({
      next: () => this.loading.set(false),
      error: () => {
        this.loading.set(false);
        this.error.set(this.translationService.t('billing.loadError'));
      },
    });
  }

  protected daysLeft(iso: string | null): number {
    if (!iso) return 0;
    return Math.max(Math.ceil((new Date(iso).getTime() - Date.now()) / 86_400_000), 0);
  }

  protected formatDate(iso: string | null): string {
    if (!iso) return '';
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(new Date(iso));
  }

  protected subscribe(): void {
    this.redirectTo(this.billing.checkout());
  }

  protected manage(): void {
    this.redirectTo(this.billing.portal());
  }

  private redirectTo(request: ReturnType<BillingService['checkout']>): void {
    this.working.set(true);
    this.error.set('');
    request.subscribe({
      next: ({ url }) => window.location.assign(url),
      error: () => {
        this.working.set(false);
        this.error.set(this.translationService.t('billing.actionError'));
      },
    });
  }
}
