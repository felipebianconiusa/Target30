import { Component, Input } from '@angular/core';
import { Transaction } from '../../plaid.service';
import { translateCategory } from '../category-labels';
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

  constructor(protected readonly translationService: TranslationService) {}

  protected categoryLabel(code: string | null): string {
    return translateCategory(code, this.translationService.lang());
  }

  protected formatAmount(transaction: Transaction): string {
    const currency = transaction.isoCurrencyCode ?? 'USD';
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    return new Intl.NumberFormat(locale, { style: 'currency', currency }).format(
      transaction.amount,
    );
  }
}
