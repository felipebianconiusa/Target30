import { Component, OnInit, signal } from '@angular/core';
import { Card, CardsService } from '../../cards/cards.service';
import { TranslationService } from '../../i18n/translation.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { cardLabel } from '../../shared/card-label';
import { ALL_CATEGORY_CODES, translateCategory } from '../../shared/category-labels';
import {
  BASE_REWARD_CATEGORY,
  CardRewards,
  RewardRanking,
  RewardRate,
  RewardsService,
} from '../rewards.service';

// Categorias onde faz sentido comparar recompensa (compras, não entradas/transferências).
const SPENDING_CATEGORIES = ALL_CATEGORY_CODES.filter(
  (c) => !['INCOME', 'TRANSFER_IN', 'TRANSFER_OUT', 'LOAN_PAYMENTS'].includes(c),
);

interface RateRow {
  category: string;
  ratePercent: number | null;
}

interface CardDraft {
  base: number | null;
  rows: RateRow[];
}

// "Qual cartão pra essa compra?" (maior recompensa na categoria, com o prazo de fechamento como
// desempate/aviso) + o editor das taxas de cashback de cada cartão.
@Component({
  selector: 'app-rewards-panel',
  imports: [TranslatePipe],
  templateUrl: './rewards-panel.html',
  styleUrl: './rewards-panel.scss',
})
export class RewardsPanel implements OnInit {
  protected readonly cardLabel = cardLabel;
  protected readonly categories = SPENDING_CATEGORIES;

  protected readonly category = signal('FOOD_AND_DRINK');
  protected readonly ranking = signal<RewardRanking[]>([]);
  protected readonly loading = signal(false);

  protected readonly editing = signal(false);
  protected readonly cards = signal<Card[]>([]);
  protected readonly drafts = signal<Record<string, CardDraft>>({});
  protected readonly savingId = signal<string | null>(null);

  constructor(
    private readonly rewardsService: RewardsService,
    private readonly cardsService: CardsService,
    private readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    this.loadRanking();
  }

  protected categoryLabel(code: string): string {
    return translateCategory(code, this.translationService.lang());
  }

  protected setCategory(value: string): void {
    this.category.set(value);
    this.loadRanking();
  }

  protected toggleEditor(): void {
    const opening = !this.editing();
    this.editing.set(opening);
    if (!opening) return;

    this.cardsService.getCards().subscribe((cards) => this.cards.set(cards));
    this.rewardsService.getRates().subscribe((all) => this.drafts.set(this.toDrafts(all)));
  }

  protected draft(card: Card): CardDraft {
    return this.drafts()[card.accountId] ?? { base: null, rows: [] };
  }

  protected setBase(card: Card, value: string): void {
    this.patch(card, { base: value === '' ? null : Number(value) });
  }

  protected addRow(card: Card): void {
    const used = new Set(this.draft(card).rows.map((r) => r.category));
    const next = this.categories.find((c) => !used.has(c)) ?? this.categories[0];
    this.patch(card, { rows: [...this.draft(card).rows, { category: next, ratePercent: null }] });
  }

  protected setRowCategory(card: Card, index: number, category: string): void {
    this.patchRow(card, index, { category });
  }

  protected setRowRate(card: Card, index: number, value: string): void {
    this.patchRow(card, index, { ratePercent: value === '' ? null : Number(value) });
  }

  protected removeRow(card: Card, index: number): void {
    this.patch(card, { rows: this.draft(card).rows.filter((_, i) => i !== index) });
  }

  protected save(card: Card): void {
    const d = this.draft(card);
    const rates: RewardRate[] = [];
    if (d.base !== null) rates.push({ category: BASE_REWARD_CATEGORY, ratePercent: d.base });
    for (const row of d.rows) {
      if (row.ratePercent !== null) rates.push({ category: row.category, ratePercent: row.ratePercent });
    }

    this.savingId.set(card.accountId);
    this.rewardsService.setRates(card.accountId, rates).subscribe({
      next: () => {
        this.savingId.set(null);
        this.loadRanking();
      },
      error: () => this.savingId.set(null),
    });
  }

  private patch(card: Card, change: Partial<CardDraft>): void {
    this.drafts.update((all) => ({ ...all, [card.accountId]: { ...this.draft(card), ...change } }));
  }

  private patchRow(card: Card, index: number, change: Partial<RateRow>): void {
    const rows = this.draft(card).rows.map((r, i) => (i === index ? { ...r, ...change } : r));
    this.patch(card, { rows });
  }

  private toDrafts(all: CardRewards[]): Record<string, CardDraft> {
    const result: Record<string, CardDraft> = {};
    for (const card of all) {
      result[card.accountId] = {
        base: card.rates.find((r) => r.category === BASE_REWARD_CATEGORY)?.ratePercent ?? null,
        rows: card.rates
          .filter((r) => r.category !== BASE_REWARD_CATEGORY)
          .map((r) => ({ category: r.category, ratePercent: r.ratePercent })),
      };
    }
    return result;
  }

  private loadRanking(): void {
    this.loading.set(true);
    this.rewardsService.bestFor(this.category()).subscribe({
      next: (list) => {
        this.ranking.set(list);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
