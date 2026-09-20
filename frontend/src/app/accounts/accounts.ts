import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConnectAccount } from '../connect-account/connect-account';
import { PlaidItemFreshness, PlaidItemSummary, PlaidService } from '../plaid.service';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { LOCALE_BY_LANG } from '../i18n/translations';

@Component({
  selector: 'app-accounts',
  imports: [ConnectAccount, RouterLink, TranslatePipe],
  templateUrl: './accounts.html',
  styleUrl: './accounts.scss',
})
export class Accounts implements OnInit {
  protected readonly items = signal<PlaidItemSummary[]>([]);
  protected readonly removingItemId = signal<string | null>(null);
  protected readonly freshness = signal<Record<string, PlaidItemFreshness>>({});
  protected readonly refreshingItemId = signal<string | null>(null);
  protected readonly refreshMessages = signal<Record<string, string>>({});

  constructor(
    private readonly plaidService: PlaidService,
    protected readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    this.loadItems();
  }

  protected loadItems(): void {
    this.plaidService.getItems().subscribe({
      next: (items) => {
        this.items.set(items);
        this.loadFreshness();
      },
    });
  }

  // Informativo (o Plaid pode demorar ou falhar): se não vier, a tela só não mostra a linha.
  private loadFreshness(): void {
    this.plaidService.getItemsFreshness().subscribe({
      next: (list) => this.freshness.set(Object.fromEntries(list.map((f) => [f.itemId, f]))),
      error: () => undefined,
    });
  }

  // O Plaid falhou depois da última atualização bem-sucedida: os dados podem estar defasados.
  protected lastAttemptFailed(f: PlaidItemFreshness | undefined): boolean {
    if (!f?.plaidLastFailedUpdate) return false;
    if (!f.plaidLastSuccessfulUpdate) return true;
    return new Date(f.plaidLastFailedUpdate) > new Date(f.plaidLastSuccessfulUpdate);
  }

  protected refresh(item: PlaidItemSummary): void {
    const institution =
      item.institutionName ?? this.translationService.t('accounts.unnamedInstitution');
    // Custa dinheiro a cada chamada no Plaid: sempre confirma antes.
    if (!confirm(this.translationService.t('accounts.refreshConfirm', { institution }))) return;

    this.refreshingItemId.set(item.itemId);
    this.setRefreshMessage(item.itemId, '');
    this.plaidService.refreshItem(item.itemId).subscribe({
      next: (result) => {
        this.refreshingItemId.set(null);
        this.setRefreshMessage(
          item.itemId,
          this.translationService.t(result.updated ? 'accounts.refreshUpdated' : 'accounts.refreshPending'),
        );
        this.loadItems();
      },
      error: (err: HttpErrorResponse) => {
        this.refreshingItemId.set(null);
        const seconds = err.status === 429 ? Number(err.error?.retryAfterSeconds) : NaN;
        this.setRefreshMessage(
          item.itemId,
          Number.isFinite(seconds)
            ? this.translationService.t('accounts.refreshCooldown', { minutes: Math.ceil(seconds / 60) })
            : this.translationService.t('accounts.refreshError'),
        );
      },
    });
  }

  private setRefreshMessage(itemId: string, message: string): void {
    this.refreshMessages.update((m) => ({ ...m, [itemId]: message }));
  }

  protected formatDateTime(value: string | null): string {
    if (!value) return '';
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    return new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeStyle: 'short' }).format(
      new Date(value),
    );
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
