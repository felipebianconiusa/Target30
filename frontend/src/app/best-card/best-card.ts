import { Component, OnInit, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BestCard as BestCardItem, BestCardResponse, CardsService } from '../cards/cards.service';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { LOCALE_BY_LANG } from '../i18n/translations';
import { cardLabel } from '../shared/card-label';
import { RewardsPanel } from './rewards-panel/rewards-panel';

type Tier = 'recommended' | 'alternative' | 'caution';

// Espelham BestCardPicker.RecommendedMinDays / AlternativeMinDays no backend (só pro texto explicativo).
const RECOMMENDED_MIN_DAYS = 15;
const ALTERNATIVE_MIN_DAYS = 7;

@Component({
  selector: 'app-best-card',
  imports: [TranslatePipe, RouterLink, RewardsPanel],
  templateUrl: './best-card.html',
  styleUrl: './best-card.scss',
})
export class BestCard implements OnInit {
  protected readonly loading = signal(true);
  protected readonly errored = signal(false);
  protected readonly data = signal<BestCardResponse | null>(null);

  protected readonly recommendedMinDays = RECOMMENDED_MIN_DAYS;
  protected readonly alternativeMinDays = ALTERNATIVE_MIN_DAYS;
  protected readonly cardLabel = cardLabel;

  protected readonly tiers: Tier[] = ['recommended', 'alternative', 'caution'];

  protected readonly isEmpty = computed(() => {
    const d = this.data();
    return !d || (d.ranking.length === 0 && d.excluded.length === 0);
  });

  constructor(
    private readonly cardsService: CardsService,
    private readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    this.cardsService.getBestCardToday().subscribe({
      next: (data) => {
        this.data.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.errored.set(true);
        this.loading.set(false);
      },
    });
  }

  protected inTier(tier: Tier): BestCardItem[] {
    return this.data()?.ranking.filter((c) => c.tier === tier) ?? [];
  }

  protected formatCurrency(value: number): string {
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    return new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD' }).format(value);
  }
}
