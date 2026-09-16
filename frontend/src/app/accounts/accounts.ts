import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConnectAccount } from '../connect-account/connect-account';
import { PlaidItemSummary, PlaidService } from '../plaid.service';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';

@Component({
  selector: 'app-accounts',
  imports: [ConnectAccount, RouterLink, TranslatePipe],
  templateUrl: './accounts.html',
  styleUrl: './accounts.scss',
})
export class Accounts implements OnInit {
  protected readonly items = signal<PlaidItemSummary[]>([]);
  protected readonly removingItemId = signal<string | null>(null);

  constructor(
    private readonly plaidService: PlaidService,
    protected readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    this.loadItems();
  }

  protected loadItems(): void {
    this.plaidService.getItems().subscribe({
      next: (items) => this.items.set(items),
    });
  }

  protected remove(item: PlaidItemSummary): void {
    const institution =
      item.institutionName ?? this.translationService.t('accounts.unnamedInstitution');
    const confirmed = confirm(
      this.translationService.t('accounts.removeConfirm', { institution }),
    );
    if (!confirmed) return;

    this.removingItemId.set(item.itemId);
    this.plaidService.removeItem(item.itemId).subscribe({
      next: () => {
        this.removingItemId.set(null);
        this.loadItems();
      },
      error: () => this.removingItemId.set(null),
    });
  }
}
