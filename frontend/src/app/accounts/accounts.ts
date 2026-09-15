import { Component, OnInit, signal } from '@angular/core';
import { ConnectAccount } from '../connect-account/connect-account';
import { PlaidItemSummary, PlaidService, PlaidTransaction } from '../plaid.service';

@Component({
  selector: 'app-accounts',
  imports: [ConnectAccount],
  templateUrl: './accounts.html',
  styleUrl: './accounts.scss',
})
export class Accounts implements OnInit {
  protected readonly items = signal<PlaidItemSummary[]>([]);
  protected readonly selectedItemId = signal<string | null>(null);
  protected readonly transactions = signal<PlaidTransaction[]>([]);
  protected readonly transactionsLoading = signal(false);
  protected readonly transactionsError = signal('');

  constructor(private readonly plaidService: PlaidService) {}

  ngOnInit(): void {
    this.loadItems();
  }

  protected loadItems(): void {
    this.plaidService.getItems().subscribe({
      next: (items) => this.items.set(items),
    });
  }

  protected selectItem(itemId: string): void {
    this.selectedItemId.set(itemId);
    this.transactions.set([]);
    this.transactionsError.set('');
    this.transactionsLoading.set(true);

    this.plaidService.getTransactions(itemId).subscribe({
      next: (response) => {
        this.transactions.set(
          [...response.added].sort((a, b) => (a.date < b.date ? 1 : -1)),
        );
        this.transactionsLoading.set(false);
      },
      error: () => {
        this.transactionsError.set('Não foi possível carregar as transações.');
        this.transactionsLoading.set(false);
      },
    });
  }

  protected formatAmount(transaction: PlaidTransaction): string {
    const currency = transaction.iso_currency_code ?? 'USD';
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency }).format(
      transaction.amount,
    );
  }
}
