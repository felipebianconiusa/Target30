import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {
  PlaidItemSummary,
  PlaidService,
  Transaction,
  TransactionsQueryFilter,
} from '../plaid.service';
import { LOCALE_BY_LANG } from '../i18n/translations';
import { TransactionTable } from '../shared/transaction-table/transaction-table';
import { ALL_CATEGORY_CODES, translateCategory } from '../shared/category-labels';
import { MultiSelect, MultiSelectOption } from '../shared/multi-select/multi-select';
import * as dateRanges from '../shared/date-ranges';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';

type DatePreset = '7d' | '30d' | 'current-month' | 'previous-month' | 'custom' | 'all';

const PAGE_SIZE = 25;
const SEARCH_DEBOUNCE_MS = 300;

@Component({
  selector: 'app-transactions',
  imports: [TransactionTable, MultiSelect, TranslatePipe],
  templateUrl: './transactions.html',
  styleUrl: './transactions.scss',
})
export class Transactions implements OnInit, OnDestroy {
  protected readonly transactions = signal<Transaction[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = PAGE_SIZE;
  protected readonly loading = signal(true);
  protected readonly syncing = signal(false);
  protected readonly errorMessage = signal('');

  protected readonly totalIncome = signal(0);
  protected readonly totalExpenses = signal(0);
  protected readonly net = computed(() => this.totalIncome() - this.totalExpenses());

  protected readonly search = signal('');
  protected readonly categories = signal<string[]>([]);
  protected readonly institutions = signal<string[]>([]);
  protected readonly dateFrom = signal('');
  protected readonly dateTo = signal('');
  protected readonly activePreset = signal<DatePreset>('all');

  protected readonly institutionOptions = signal<MultiSelectOption[]>([]);

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));

  protected readonly categoryOptions = computed<MultiSelectOption[]>(() => {
    const lang = this.translationService.lang();
    return ALL_CATEGORY_CODES.map((value) => ({ value, label: translateCategory(value, lang) })).sort(
      (a, b) => a.label.localeCompare(b.label),
    );
  });

  private searchDebounce?: ReturnType<typeof setTimeout>;

  constructor(
    private readonly plaidService: PlaidService,
    private readonly route: ActivatedRoute,
    protected readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    const institutionFromQuery = this.route.snapshot.queryParamMap.get('institution');
    if (institutionFromQuery) this.institutions.set([institutionFromQuery]);

    this.loadInstitutionOptions();
    this.loadPage();
    this.loadTotals();
  }

  ngOnDestroy(): void {
    clearTimeout(this.searchDebounce);
  }

  protected refresh(): void {
    this.syncing.set(true);
    this.plaidService.syncTransactions().subscribe({
      next: () => {
        this.syncing.set(false);
        this.loadPage();
        this.loadTotals();
      },
      error: () => {
        this.syncing.set(false);
        this.errorMessage.set(this.translationService.t('transactions.syncError'));
      },
    });
  }

  protected formatCurrency(value: number): string {
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    return new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD' }).format(value);
  }

  protected onSearchInput(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchDebounce);
    this.searchDebounce = setTimeout(() => this.onFilterChange(), SEARCH_DEBOUNCE_MS);
  }

  protected onCategoriesChange(values: string[]): void {
    this.categories.set(values);
    this.onFilterChange();
  }

  protected onInstitutionsChange(values: string[]): void {
    this.institutions.set(values);
    this.onFilterChange();
  }

  protected applyPreset(preset: DatePreset): void {
    this.activePreset.set(preset);
    switch (preset) {
      case '7d':
        this.setRange(dateRanges.last7Days());
        break;
      case '30d':
        this.setRange(dateRanges.last30Days());
        break;
      case 'current-month':
        this.setRange(dateRanges.currentMonth());
        break;
      case 'previous-month':
        this.setRange(dateRanges.previousMonth());
        break;
      case 'all':
        this.dateFrom.set('');
        this.dateTo.set('');
        break;
    }
    this.onFilterChange();
  }

  protected setDateFrom(value: string): void {
    this.dateFrom.set(value);
    this.activePreset.set('custom');
    this.onFilterChange();
  }

  protected setDateTo(value: string): void {
    this.dateTo.set(value);
    this.activePreset.set('custom');
    this.onFilterChange();
  }

  protected clearFilters(): void {
    this.search.set('');
    this.categories.set([]);
    this.institutions.set([]);
    this.dateFrom.set('');
    this.dateTo.set('');
    this.activePreset.set('all');
    this.onFilterChange();
  }

  protected onCategoryChange(event: { transaction: Transaction; category: string | null }): void {
    this.plaidService.updateTransactionCategory(event.transaction.transactionId, event.category).subscribe({
      next: (updated) => {
        this.transactions.update((list) =>
          list.map((t) => (t.transactionId === updated.transactionId ? updated : t)),
        );
      },
    });
  }

  protected goToPage(target: number): void {
    if (target < 1 || target > this.totalPages() || target === this.page()) return;
    this.page.set(target);
    this.loadPage();
  }

  private setRange(range: dateRanges.DateRange): void {
    this.dateFrom.set(range.from);
    this.dateTo.set(range.to);
  }

  private onFilterChange(): void {
    this.page.set(1);
    this.loadPage();
    this.loadTotals();
  }

  private currentQueryFilter(): TransactionsQueryFilter {
    return {
      search: this.search() || undefined,
      categories: this.categories().length ? this.categories() : undefined,
      institutions: this.institutions().length ? this.institutions() : undefined,
      dateFrom: this.dateFrom() || undefined,
      dateTo: this.dateTo() || undefined,
    };
  }

  private loadTotals(): void {
    this.plaidService.getTransactionTotals(this.currentQueryFilter()).subscribe({
      next: (totals) => {
        this.totalIncome.set(totals.totalIncome);
        this.totalExpenses.set(totals.totalExpenses);
      },
    });
  }

  private loadInstitutionOptions(): void {
    this.plaidService.getItems().subscribe({
      next: (items: PlaidItemSummary[]) => {
        const names = [...new Set(items.map((i) => i.institutionName).filter((n): n is string => !!n))];
        this.institutionOptions.set(
          names.map((value) => ({ value, label: value })).sort((a, b) => a.label.localeCompare(b.label)),
        );
      },
    });
  }

  private loadPage(): void {
    this.loading.set(true);
    this.plaidService
      .getTransactionsPage({
        ...this.currentQueryFilter(),
        page: this.page(),
        pageSize: this.pageSize,
      })
      .subscribe({
        next: (result) => {
          this.transactions.set(result.items);
          this.total.set(result.total);
          this.loading.set(false);
        },
        error: () => {
          this.errorMessage.set(this.translationService.t('transactions.error'));
          this.loading.set(false);
        },
      });
  }
}
