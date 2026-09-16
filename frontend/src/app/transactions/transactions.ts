import { Component, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { PlaidService, Transaction } from '../plaid.service';
import { TransactionTable } from '../shared/transaction-table/transaction-table';
import { CATEGORY_FALLBACK_CODE, translateCategory } from '../shared/category-labels';
import { MultiSelect, MultiSelectOption } from '../shared/multi-select/multi-select';
import * as dateRanges from '../shared/date-ranges';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';

type DatePreset = '7d' | '30d' | 'current-month' | 'previous-month' | 'custom' | 'all';

@Component({
  selector: 'app-transactions',
  imports: [TransactionTable, MultiSelect, TranslatePipe],
  templateUrl: './transactions.html',
  styleUrl: './transactions.scss',
})
export class Transactions implements OnInit {
  protected readonly transactions = signal<Transaction[]>([]);
  protected readonly loading = signal(true);
  protected readonly syncing = signal(false);
  protected readonly errorMessage = signal('');

  protected readonly search = signal('');
  protected readonly categories = signal<string[]>([]);
  protected readonly institutions = signal<string[]>([]);
  protected readonly dateFrom = signal('');
  protected readonly dateTo = signal('');
  protected readonly activePreset = signal<DatePreset>('all');

  protected readonly categoryOptions = computed<MultiSelectOption[]>(() => {
    const lang = this.translationService.lang();
    const codes = new Set(this.transactions().map((t) => t.category ?? CATEGORY_FALLBACK_CODE));
    return [...codes]
      .map((value) => ({ value, label: translateCategory(value, lang) }))
      .sort((a, b) => a.label.localeCompare(b.label));
  });

  protected readonly institutionOptions = computed<MultiSelectOption[]>(() => {
    const unnamed = this.translationService.t('accounts.unnamedInstitution');
    const names = new Set(this.transactions().map((t) => t.institutionName ?? unnamed));
    return [...names].map((value) => ({ value, label: value })).sort((a, b) =>
      a.label.localeCompare(b.label),
    );
  });

  protected readonly filteredTransactions = computed(() => {
    const search = this.search().trim().toLowerCase();
    const categories = this.categories();
    const institutions = this.institutions();
    const dateFrom = this.dateFrom();
    const dateTo = this.dateTo();
    const unnamed = this.translationService.t('accounts.unnamedInstitution');

    return this.transactions().filter((t) => {
      if (search) {
        const haystack = `${t.name} ${t.merchantName ?? ''}`.toLowerCase();
        if (!haystack.includes(search)) return false;
      }
      if (categories.length > 0 && !categories.includes(t.category ?? CATEGORY_FALLBACK_CODE))
        return false;
      if (institutions.length > 0 && !institutions.includes(t.institutionName ?? unnamed))
        return false;
      if (dateFrom && t.date < dateFrom) return false;
      if (dateTo && t.date > dateTo) return false;
      return true;
    });
  });

  constructor(
    private readonly plaidService: PlaidService,
    private readonly route: ActivatedRoute,
    protected readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    const institutionFromQuery = this.route.snapshot.queryParamMap.get('institution');
    if (institutionFromQuery) this.institutions.set([institutionFromQuery]);

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
        this.errorMessage.set(this.translationService.t('transactions.error'));
        this.loading.set(false);
      },
    });
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
  }

  protected onCustomDateChange(): void {
    this.activePreset.set('custom');
  }

  protected setDateFrom(value: string): void {
    this.dateFrom.set(value);
    this.onCustomDateChange();
  }

  protected setDateTo(value: string): void {
    this.dateTo.set(value);
    this.onCustomDateChange();
  }

  private setRange(range: dateRanges.DateRange): void {
    this.dateFrom.set(range.from);
    this.dateTo.set(range.to);
  }

  protected clearFilters(): void {
    this.search.set('');
    this.categories.set([]);
    this.institutions.set([]);
    this.dateFrom.set('');
    this.dateTo.set('');
    this.activePreset.set('all');
  }
}
