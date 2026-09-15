import { Component, Input } from '@angular/core';
import { Transaction } from '../../plaid.service';
import { translateCategory } from '../category-labels';

@Component({
  selector: 'app-transaction-table',
  imports: [],
  templateUrl: './transaction-table.html',
  styleUrl: './transaction-table.scss',
})
export class TransactionTable {
  @Input({ required: true }) transactions: Transaction[] = [];
  @Input() showInstitution = false;

  protected readonly translateCategory = translateCategory;

  protected formatAmount(transaction: Transaction): string {
    const currency = transaction.isoCurrencyCode ?? 'USD';
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency }).format(
      transaction.amount,
    );
  }
}
