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
  lastSyncedAt: string | null;
}

export interface PlaidItemFreshness {
  itemId: string;
  plaidLastSuccessfulUpdate: string | null;
  plaidLastFailedUpdate: string | null;
  errorCode: string | null;
  lastRefreshRequestedAt: string | null;
  status: 'ok' | 'stale' | 'attention';
  institutionName: string | null;
}

export interface PlaidRefreshResult {
  updated: boolean;
  plaidLastSuccessfulUpdate: string | null;
}

export interface CategoryRule {
  id: number;
  merchantKey: string;
  category: string;
}

export interface AutoBackupStatus {
  enabled: boolean;
  directory: string;
  lastBackupUtc: string | null;
  count: number;
  keepCount: number;
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
  isCategoryCustom: boolean;
  isInternalTransfer?: boolean;
}

export interface TransactionsQueryFilter {
  search?: string;
  categories?: string[];
  institutions?: string[];
  dateFrom?: string;
  dateTo?: string;
}

export interface TransactionsFilter extends TransactionsQueryFilter {
  page: number;
  pageSize: number;
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

export interface TransactionTotals {
  totalIncome: number;
  totalExpenses: number;
}

function buildFilterParams(filter: TransactionsQueryFilter): HttpParams {
  let params = new HttpParams();
  if (filter.search) params = params.set('search', filter.search);
  if (filter.categories?.length) params = params.set('categories', filter.categories.join(','));
  if (filter.institutions?.length)
    params = params.set('institutions', filter.institutions.join(','));
  if (filter.dateFrom) params = params.set('dateFrom', filter.dateFrom);
  if (filter.dateTo) params = params.set('dateTo', filter.dateTo);
  return params;
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

  getItemsFreshness(): Observable<PlaidItemFreshness[]> {
    return this.http.get<PlaidItemFreshness[]>(`${this.baseUrl}/items/freshness`);
  }

  // Cobrado por chamada pelo Plaid (a API também limita a frequência por item).
  refreshItem(itemId: string): Observable<PlaidRefreshResult> {
    return this.http.post<PlaidRefreshResult>(`${this.baseUrl}/items/${itemId}/refresh`, {});
  }

  removeItem(itemId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/items/${itemId}`);
  }

  syncTransactions(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/sync`, {});
  }

  getTransactionsPage(filter: TransactionsFilter): Observable<PagedTransactions> {
    const params = buildFilterParams(filter).set('page', filter.page).set('pageSize', filter.pageSize);
    return this.http.get<PagedTransactions>(`${this.baseUrl}/transactions`, { params });
  }

  getTransactionTotals(filter: TransactionsQueryFilter): Observable<TransactionTotals> {
    const params = buildFilterParams(filter);
    return this.http.get<TransactionTotals>(`${this.baseUrl}/transactions/totals`, { params });
  }

  getSummary(): Observable<TransactionsSummary> {
    return this.http.get<TransactionsSummary>(`${this.baseUrl}/summary`);
  }

  getAutoBackupStatus(): Observable<AutoBackupStatus> {
    return this.http.get<AutoBackupStatus>('/api/backup/auto-status');
  }

  downloadBackup(): Observable<Blob> {
    return this.http.get('/api/backup/export', { responseType: 'blob' });
  }

  // applyToMerchant: recategoriza todas as do mesmo estabelecimento e cria uma regra pras futuras.
  updateTransactionCategory(
    transactionId: string,
    category: string | null,
    applyToMerchant = false,
  ): Observable<Transaction> {
    return this.http.put<Transaction>(`${this.baseUrl}/transactions/${transactionId}/category`, {
      category,
      applyToMerchant,
    });
  }

  getCategoryRules(): Observable<CategoryRule[]> {
    return this.http.get<CategoryRule[]>('/api/category-rules');
  }

  deleteCategoryRule(id: number): Observable<void> {
    return this.http.delete<void>(`/api/category-rules/${id}`);
  }
}
