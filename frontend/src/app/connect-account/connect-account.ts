import { Component, EventEmitter, Output, signal } from '@angular/core';
import { PlaidService } from '../plaid.service';
import type { PlaidLinkOnSuccessMetadata } from '../plaid-link';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';

type ConnectStatus = 'idle' | 'loading' | 'connected' | 'error';

@Component({
  selector: 'app-connect-account',
  imports: [TranslatePipe],
  templateUrl: './connect-account.html',
  styleUrl: './connect-account.scss',
})
export class ConnectAccount {
  @Output() connected = new EventEmitter<void>();

  protected readonly status = signal<ConnectStatus>('idle');
  protected readonly errorMessage = signal('');
  protected readonly institutionName = signal('');

  constructor(
    private readonly plaidService: PlaidService,
    protected readonly translationService: TranslationService,
  ) {}

  connect(): void {
    this.status.set('loading');
    this.errorMessage.set('');

    this.plaidService.createLinkToken().subscribe({
      next: ({ linkToken }) => this.openPlaidLink(linkToken),
      error: () => this.fail(this.translationService.t('accounts.errorLinkToken')),
    });
  }

  private openPlaidLink(linkToken: string): void {
    const handler = window.Plaid.create({
      token: linkToken,
      onSuccess: (publicToken, metadata) => this.exchangeToken(publicToken, metadata),
      onExit: (error) => {
        if (error) this.fail(this.translationService.t('accounts.errorExit'));
        else this.status.set('idle');
      },
    });
    handler.open();
  }

  private exchangeToken(publicToken: string, metadata: PlaidLinkOnSuccessMetadata): void {
    const institutionName = metadata.institution?.name ?? null;

    this.plaidService.exchangePublicToken(publicToken, institutionName).subscribe({
      next: () => {
        this.institutionName.set(institutionName ?? '');
        this.status.set('connected');
        this.connected.emit();
      },
      error: () => this.fail(this.translationService.t('accounts.errorExchange')),
    });
  }

  private fail(message: string): void {
    this.errorMessage.set(message);
    this.status.set('error');
  }
}
