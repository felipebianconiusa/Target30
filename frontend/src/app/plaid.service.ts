import { HttpClient, HttpParams } from '@angular/common/http';
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

export interface TransactionsFilter {
  page: number;
  pageSize: number;
  search?: string;
  categories?: string[];
  institutions?: string[];
  dateFrom?: string;
  dateTo?: string;
}

export interface PagedTransactions {
  items: Transaction[];
  total: number;
  page: number;
  pageSize: number;
}

export interface TransactionsSummary {
  totalIncome: number;
  totalExpenses: number;
  categoryTotals: { category: string; total: number }[];
  recentTransactions: Transaction[];
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

  removeItem(itemId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/items/${itemId}`);
  }

  syncTransactions(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/sync`, {});
  }

  getTransactionsPage(filter: TransactionsFilter): Observable<PagedTransactions> {
    let params = new HttpParams().set('page', filter.page).set('pageSize', filter.pageSize);
    if (filter.search) params = params.set('search', filter.search);
    if (filter.categories?.length) params = params.set('categories', filter.categories.join(','));
    if (filter.institutions?.length)
      params = params.set('institutions', filter.institutions.join(','));
    if (filter.dateFrom) params = params.set('dateFrom', filter.dateFrom);
    if (filter.dateTo) params = params.set('dateTo', filter.dateTo);

    return this.http.get<PagedTransactions>(`${this.baseUrl}/transactions`, { params });
  }

  getSummary(): Observable<TransactionsSummary> {
    return this.http.get<TransactionsSummary>(`${this.baseUrl}/summary`);
  }
}
