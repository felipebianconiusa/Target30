import { Component, OnInit, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PlaidService, Transaction } from '../plaid.service';
import { TransactionTable } from '../shared/transaction-table/transaction-table';
import { CATEGORY_FALLBACK_CODE, translateCategory } from '../shared/category-labels';
import { TranslationService } from '../i18n/translation.service';
import { LOCALE_BY_LANG } from '../i18n/translations';
import { TranslatePipe } from '../i18n/translate.pipe';

interface CategoryTotal {
  category: string;
  total: number;
  percentOfMax: number;
}

@Component({
  selector: 'app-dashboard',
  imports: [TransactionTable, RouterLink, TranslatePipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  protected readonly transactions = signal<Transaction[]>([]);
  protected readonly loading = signal(true);
  protected readonly syncing = signal(false);
  protected readonly errorMessage = signal('');

  protected readonly totalExpenses = computed(() =>
    this.transactions()
      .filter((t) => t.amount > 0)
      .reduce((sum, t) => sum + t.amount, 0),
  );

  protected readonly totalIncome = computed(() =>
    this.transactions()
      .filter((t) => t.amount < 0)
      .reduce((sum, t) => sum + Math.abs(t.amount), 0),
  );

  protected readonly net = computed(() => this.totalIncome() - this.totalExpenses());

  protected readonly recentTransactions = computed(() => this.transactions().slice(0, 8));

  protected readonly categoryTotals = computed<CategoryTotal[]>(() => {
    const totals = new Map<string, number>();
    for (const t of this.transactions()) {
      if (t.amount <= 0) continue;
      const key = t.category ?? CATEGORY_FALLBACK_CODE;
      totals.set(key, (totals.get(key) ?? 0) + t.amount);
    }
    const entries = [...totals.entries()].sort((a, b) => b[1] - a[1]).slice(0, 6);
    const max = entries.length > 0 ? entries[0][1] : 1;
    const lang = this.translationService.lang();
    return entries.map(([category, total]) => ({
      category: translateCategory(category, lang),
      total,
      percentOfMax: (total / max) * 100,
    }));
  });

  constructor(
    private readonly plaidService: PlaidService,
    protected readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    this.loadTransactions();
  }

  protected refresh(): void {
    this.syncing.set(true);
    this.plaidService.syncTransactions().subscribe({
      next: () => {
        this.syncing.set(false);
        this.loadTransactions();
      },
      error: () => {
        this.syncing.set(false);
        this.errorMessage.set(this.translationService.t('transactions.syncError'));
      },
    });
  }

  private loadTransactions(): void {
    this.plaidService.getAllTransactions().subscribe({
      next: (transactions) => {
        this.transactions.set(transactions);
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
