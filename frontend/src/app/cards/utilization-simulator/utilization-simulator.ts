import { Component, OnInit, signal } from '@angular/core';
import { CardsService, UtilizationPlan } from '../cards.service';
import { TranslationService } from '../../i18n/translation.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { LOCALE_BY_LANG } from '../../i18n/translations';
import { cardLabel } from '../../shared/card-label';

// Faixas usuais de utilização reportada: abaixo de 30% (limite "saudável"), 10% (ótimo) e 9%
// (a faixa de 1–9% que muita gente mira para o melhor score).
export const UTILIZATION_BANDS = [30, 10, 9] as const;

@Component({
  selector: 'app-utilization-simulator',
  imports: [TranslatePipe],
  templateUrl: './utilization-simulator.html',
  styleUrl: './utilization-simulator.scss',
})
export class UtilizationSimulator implements OnInit {
  protected readonly bands = UTILIZATION_BANDS;
  protected readonly cardLabel = cardLabel;
  protected readonly band = signal<number>(30);
  protected readonly plan = signal<UtilizationPlan | null>(null);
  protected readonly loading = signal(false);

  constructor(
    private readonly cardsService: CardsService,
    private readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    this.load();
  }

  protected setBand(value: number): void {
    this.band.set(value);
    this.load();
  }

  protected formatCurrency(value: number): string {
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    return new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD' }).format(value);
  }

  private load(): void {
    this.loading.set(true);
    this.cardsService.getUtilizationPlan(this.band()).subscribe({
      next: (plan) => {
        this.plan.set(plan);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
