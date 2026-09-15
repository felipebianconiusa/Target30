import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface LinkTokenResponse {
  linkToken: string;
}

export interface ExchangeTokenResponse {
  itemId: string;
}

export interface PlaidItemSummary {
  itemId: string;
  institutionName: string | null;
  connectedAt: string;
}

export interface PlaidTransaction {
  transaction_id: string;
  account_id: string;
  amount: number;
  iso_currency_code: string | null;
  date: string;
  name: string;
  merchant_name: string | null;
  pending: boolean;
  category: string[] | null;
}

export interface TransactionsSyncResponse {
  added: PlaidTransaction[];
  modified: PlaidTransaction[];
  removed: unknown[];
  hasMore: boolean;
}

@Injectable({ providedIn: 'root' })
export class PlaidService {
  private readonly baseUrl = '/api/plaid';

  constructor(private readonly http: HttpClient) {}

  createLinkToken(): Observable<LinkTokenResponse> {
    return this.http.post<LinkTokenResponse>(`${this.baseUrl}/link-token`, {});
  }

  exchangePublicToken(
    publicToken: string,
    institutionName: string | null,
  ): Observable<ExchangeTokenResponse> {
    return this.http.post<ExchangeTokenResponse>(`${this.baseUrl}/exchange-token`, {
      publicToken,
      institutionName,
    });
  }

  getItems(): Observable<PlaidItemSummary[]> {
    return this.http.get<PlaidItemSummary[]>(`${this.baseUrl}/items`);
  }

  getTransactions(itemId: string): Observable<TransactionsSyncResponse> {
    return this.http.get<TransactionsSyncResponse>(
      `${this.baseUrl}/items/${itemId}/transactions`,
    );
  }
}
