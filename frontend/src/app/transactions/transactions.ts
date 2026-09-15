import { Component, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { PlaidService, Transaction } from '../plaid.service';
import { TransactionTable } from '../shared/transaction-table/transaction-table';

@Component({
  selector: 'app-transactions',
  imports: [TransactionTable],
  templateUrl: './transactions.html',
  styleUrl: './transactions.scss',
})
export class Transactions implements OnInit {
  protected readonly transactions = signal<Transaction[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal('');

  protected readonly search = signal('');
  protected readonly category = signal('');
  protected readonly institution = signal('');
  protected readonly dateFrom = signal('');
  protected readonly dateTo = signal('');

  protected readonly categories = computed(() =>
    [...new Set(this.transactions().map((t) => t.category ?? 'Outros'))].sort(),
  );

  protected readonly institutions = computed(() =>
    [...new Set(this.transactions().map((t) => t.institutionName ?? 'Instituição sem nome'))].sort(),
  );

  protected readonly filteredTransactions = computed(() => {
    const search = this.search().trim().toLowerCase();
    const category = this.category();
    const institution = this.institution();
    const dateFrom = this.dateFrom();
    const dateTo = this.dateTo();

    return this.transactions().filter((t) => {
      if (search) {
        const haystack = `${t.name} ${t.merchantName ?? ''}`.toLowerCase();
        if (!haystack.includes(search)) return false;
      }
      if (category && (t.category ?? 'Outros') !== category) return false;
      if (institution && (t.institutionName ?? 'Instituição sem nome') !== institution)
        return false;
      if (dateFrom && t.date < dateFrom) return false;
      if (dateTo && t.date > dateTo) return false;
      return true;
    });
  });

  constructor(
    private readonly plaidService: PlaidService,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    const institutionFromQuery = this.route.snapshot.queryParamMap.get('institution');
    if (institutionFromQuery) this.institution.set(institutionFromQuery);

    this.plaidService.getAllTransactions().subscribe({
      next: (transactions) => {
        this.transactions.set(transactions);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Não foi possível carregar as transações.');
        this.loading.set(false);
      },
    });
  }

  protected clearFilters(): void {
    this.search.set('');
    this.category.set('');
    this.institution.set('');
    this.dateFrom.set('');
    this.dateTo.set('');
  }
}
