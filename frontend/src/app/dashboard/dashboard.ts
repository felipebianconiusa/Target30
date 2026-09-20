import { Component, OnInit, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { PlaidItemFreshness, PlaidService, Transaction, TransactionsSummary } from '../plaid.service';
import { BestCard, Card, CardsService } from '../cards/cards.service';
import { Budget, BudgetsService } from './budgets.service';
import { DashboardService, HealthScore, MonthlyComparisonRow } from './dashboard.service';
import { cardLabel } from '../shared/card-label';
import { TransactionTable } from '../shared/transaction-table/transaction-table';
import { ALL_CATEGORY_CODES, translateCategory } from '../shared/category-labels';
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

  protected readonly budgets = signal<Budget[]>([]);
  protected readonly showAddBudget = signal(false);
  protected readonly newBudgetCategory = signal('');
  protected readonly newBudgetLimit = signal<number | null>(null);
  protected readonly savingBudget = signal(false);

  protected readonly cardLabel = cardLabel;
  protected readonly bestCard = signal<BestCard | null>(null);
  // Bancos cujos dados no Plaid estão velhos ou com erro (informativo: falha ao consultar = sem aviso).
  protected readonly staleBanks = signal<PlaidItemFreshness[]>([]);
  protected readonly healthScore = signal<HealthScore | null>(null);
  protected readonly monthlyComparison = signal<MonthlyComparisonRow[]>([]);
  protected readonly showComparison = signal(false);

  protected readonly categoryOptions = computed(() => {
    const lang = this.translationService.lang();
    const used = new Set(this.budgets().map((b) => b.category));
    return ALL_CATEGORY_CODES.filter((code) => !used.has(code))
      .map((value) => ({ value, label: translateCategory(value, lang) }))
      .sort((a, b) => a.label.localeCompare(b.label));
  });

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
    private readonly budgetsService: BudgetsService,
    private readonly dashboardService: DashboardService,
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

  protected categoryLabel(code: string): string {
    return translateCategory(code, this.translationService.lang());
  }

  protected toggleAddBudget(): void {
    this.showAddBudget.update((v) => !v);
  }

  protected setNewBudgetCategory(value: string): void {
    this.newBudgetCategory.set(value);
  }

  protected setNewBudgetLimit(value: string): void {
    this.newBudgetLimit.set(value ? Number(value) : null);
  }

  protected addBudget(): void {
    const category = this.newBudgetCategory();
    const monthlyLimit = this.newBudgetLimit();
    if (!category || monthlyLimit === null || monthlyLimit <= 0) return;

    this.savingBudget.set(true);
    this.budgetsService.createBudget({ category, monthlyLimit }).subscribe({
      next: () => {
        this.savingBudget.set(false);
        this.newBudgetCategory.set('');
        this.newBudgetLimit.set(null);
        this.loadBudgets();
      },
      error: () => this.savingBudget.set(false),
    });
  }

  protected deleteBudget(budget: Budget): void {
    this.budgetsService.deleteBudget(budget.id).subscribe(() => this.loadBudgets());
  }

  protected toggleComparison(): void {
    this.showComparison.update((v) => !v);
  }

  protected comparisonLabel(row: MonthlyComparisonRow): string {
    return translateCategory(row.category, this.translationService.lang());
  }

  private loadBudgets(): void {
    this.budgetsService.getBudgets().subscribe((budgets) => this.budgets.set(budgets));
  }

  private load(): void {
    this.loadBudgets();
    this.plaidService.getItemsFreshness().subscribe({
      next: (list) => this.staleBanks.set(list.filter((f) => f.status !== 'ok')),
      error: () => undefined,
    });
    this.cardsService.getBestCardToday().subscribe((best) => this.bestCard.set(best.recommended));
    this.dashboardService.getHealthScore().subscribe((score) => this.healthScore.set(score));
    this.dashboardService.getMonthlyComparison().subscribe((rows) => this.monthlyComparison.set(rows));
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
