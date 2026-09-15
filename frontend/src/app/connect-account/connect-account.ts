import { Component, signal } from '@angular/core';
import { PlaidService } from '../plaid.service';
import type { PlaidLinkOnSuccessMetadata } from '../plaid-link';

type ConnectStatus = 'idle' | 'loading' | 'connected' | 'error';

@Component({
  selector: 'app-connect-account',
  imports: [],
  templateUrl: './connect-account.html',
  styleUrl: './connect-account.scss',
})
export class ConnectAccount {
  protected readonly status = signal<ConnectStatus>('idle');
  protected readonly errorMessage = signal('');
  protected readonly institutionName = signal('');

  constructor(private readonly plaidService: PlaidService) {}

  connect(): void {
    this.status.set('loading');
    this.errorMessage.set('');

    this.plaidService.createLinkToken().subscribe({
      next: ({ linkToken }) => this.openPlaidLink(linkToken),
      error: () => this.fail('Não foi possível gerar o link token. Verifique o backend.'),
    });
  }

  private openPlaidLink(linkToken: string): void {
    const handler = window.Plaid.create({
      token: linkToken,
      onSuccess: (publicToken, metadata) => this.exchangeToken(publicToken, metadata),
      onExit: (error) => {
        if (error) this.fail('Conexão cancelada ou falhou no Plaid Link.');
        else this.status.set('idle');
      },
    });
    handler.open();
  }

  private exchangeToken(publicToken: string, metadata: PlaidLinkOnSuccessMetadata): void {
    this.plaidService.exchangePublicToken(publicToken).subscribe({
      next: () => {
        this.institutionName.set(metadata.institution?.name ?? 'sua instituição');
        this.status.set('connected');
      },
      error: () => this.fail('Falha ao trocar o token com o backend.'),
    });
  }

  private fail(message: string): void {
    this.errorMessage.set(message);
    this.status.set('error');
  }
}
