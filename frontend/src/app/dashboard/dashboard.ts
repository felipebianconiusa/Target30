import { Component, OnInit, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { PlaidService, Transaction, TransactionsSummary } from '../plaid.service';
import { Card, CardsService } from '../cards/cards.service';
import { TransactionTable } from '../shared/transaction-table/transaction-table';
import { translateCategory } from '../shared/category-labels';
import { TranslationService } from '../i18n/translation.service';
import { LOCALE_BY_LANG } from '../i18n/translations';
import { TranslatePipe } from '../i18n/translate.pipe';

interface CategoryTotal {
  category: string;
  total: number;
  percentOfMax: number;
}

const EMPTY_SUMMARY: TransactionsSummary = {
  totalIncome: 0,
  totalExpenses: 0,
  categoryTotals: [],
  recentTransactions: [],
};

@Component({
  selector: 'app-dashboard',
  imports: [TransactionTable, RouterLink, TranslatePipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  protected readonly summary = signal<TransactionsSummary>(EMPTY_SUMMARY);
  protected readonly itemCount = signal(0);
  protected readonly cardAlerts = signal<Card[]>([]);
  protected readonly loading = signal(true);
  protected readonly syncing = signal(false);
  protected readonly errorMessage = signal('');

  protected readonly net = computed(() => this.summary().totalIncome - this.summary().totalExpenses);

  protected readonly recentTransactions = computed<Transaction[]>(
    () => this.summary().recentTransactions,
  );

  protected readonly categoryTotals = computed<CategoryTotal[]>(() => {
    const lang = this.translationService.lang();
    const totals = this.summary().categoryTotals;
    const max = totals.length > 0 ? totals[0].total : 1;
    return totals.map((c) => ({
      category: translateCategory(c.category, lang),
      total: c.total,
      percentOfMax: (c.total / max) * 100,
    }));
  });

  constructor(
    private readonly plaidService: PlaidService,
    private readonly cardsService: CardsService,
    protected readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    this.load();
  }

  protected refresh(): void {
    this.syncing.set(true);
    this.plaidService.syncTransactions().subscribe({
      next: () => {
        this.syncing.set(false);
        this.load();
      },
      error: () => {
        this.syncing.set(false);
        this.errorMessage.set(this.translationService.t('transactions.syncError'));
      },
    });
  }

  private load(): void {
    forkJoin({
      summary: this.plaidService.getSummary(),
      items: this.plaidService.getItems(),
      cards: this.cardsService.getCards(),
    }).subscribe({
      next: ({ summary, items, cards }) => {
        this.summary.set(summary);
        this.itemCount.set(items.length);
        this.cardAlerts.set(cards.filter((c) => c.needsAlert));
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set(this.translationService.t('dashboard.error'));
        this.loading.set(false);
      },
    });
  }

  protected formatCurrency(value: number): string {
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    return new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD' }).format(value);
  }
}
