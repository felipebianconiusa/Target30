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

export interface Transaction {
  transactionId: string;
  accountId: string;
  itemId: string;
  institutionName: string | null;
  amount: number;
  isoCurrencyCode: string | null;
  date: string;
  name: string;
  merchantName: string | null;
  pending: boolean;
  category: string | null;
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

  getItemTransactions(itemId: string): Observable<Transaction[]> {
    return this.http.get<Transaction[]>(`${this.baseUrl}/items/${itemId}/transactions`);
  }

  getAllTransactions(): Observable<Transaction[]> {
    return this.http.get<Transaction[]>(`${this.baseUrl}/transactions`);
  }
}
