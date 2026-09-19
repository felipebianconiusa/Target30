import { Component, EventEmitter, Input, Output } from '@angular/core';
import { Transaction } from '../../plaid.service';
import { ALL_CATEGORY_CODES, translateCategory } from '../category-labels';
import { TranslationService } from '../../i18n/translation.service';
import { LOCALE_BY_LANG } from '../../i18n/translations';
import { TranslatePipe } from '../../i18n/translate.pipe';

@Component({
  selector: 'app-transaction-table',
  imports: [TranslatePipe],
  templateUrl: './transaction-table.html',
  styleUrl: './transaction-table.scss',
})
export class TransactionTable {
  @Input({ required: true }) transactions: Transaction[] = [];
  @Input() showInstitution = false;
  @Input() editableCategory = false;
  @Output() categoryChange = new EventEmitter<{ transaction: Transaction; category: string | null }>();

  protected readonly categoryOptions = ALL_CATEGORY_CODES;
  protected readonly editingId = { value: null as string | null };

  constructor(protected readonly translationService: TranslationService) {}

  protected categoryLabel(code: string | null): string {
    return translateCategory(code, this.translationService.lang());
  }

  protected startEditCategory(transaction: Transaction): void {
    this.editingId.value = transaction.transactionId;
  }

  protected onCategorySelected(transaction: Transaction, value: string): void {
    this.editingId.value = null;
    if (value === (transaction.category ?? '')) return;
    this.categoryChange.emit({ transaction, category: value || null });
  }

  protected formatAmount(transaction: Transaction): string {
    const currency = transaction.isoCurrencyCode ?? 'USD';
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    // Convenção do Plaid é o oposto do que se lê naturalmente (negativo = entrada, sem sinal =
    // saída) — invertemos só na exibição: negativo = saída, sem sinal = entrada.
    return new Intl.NumberFormat(locale, { style: 'currency', currency }).format(
      -transaction.amount,
    );
  }
}
