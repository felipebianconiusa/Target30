import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface Card {
  accountId: string;
  name: string;
  officialName: string | null;
  institutionName: string | null;
  currentBalance: number;
  creditLimit: number;
  isoCurrencyCode: string | null;
  utilizationPercent: number | null;
  targetPercent: number;
  targetIsCustom: boolean;
  amountToPay: number;
  statementClosingDay: number | null;
  nextClosingDate: string | null;
  paymentDeadline: string | null;
  daysUntilPaymentDeadline: number | null;
  nextPaymentDueDate: string | null;
  minimumPaymentAmount: number | null;
  isOverdue: boolean | null;
  needsAlert: boolean;
  manualCreditLimit: number | null;
  manualNextPaymentDueDate: string | null;
  lastAlertSentDate: string | null;
  nickname: string | null;
  owner?: string | null;
}

export interface UpdateCardRequest {
  statementClosingDay: number | null;
  targetUtilizationPercent: number | null;
  manualCreditLimit: number | null;
  manualNextPaymentDueDate: string | null;
  nickname: string | null;
  owner?: string | null;
}

export interface AppSettings {
  globalTargetUtilizationPercent: number;
  notifyDaysBeforeClosing: number;
  notificationsEnabled: boolean;
  weeklyDigestEnabled: boolean;
  email: string | null;
  lastDigestSentDate: string | null;
  lowBalanceThreshold: number;
}

export interface CardHistoryPoint {
  date: string;
  balance: number;
  limit: number | null;
  utilizationPercent: number | null;
}

export interface BestCard {
  accountId: string;
  name: string;
  nickname: string | null;
  institutionName: string | null;
  nextClosingDate: string | null;
  daysUntilClosing: number | null;
  currentBalance: number;
  creditLimit: number;
  availableCredit: number | null;
  utilizationPercent: number | null;
  exclusionReason: 'limit_reached' | 'no_closing_day' | null;
  tier: 'recommended' | 'alternative' | 'caution' | null;
  targetPercent: number;
  overTarget: boolean;
  owner?: string | null;
}

export interface BestCardResponse {
  recommended: BestCard | null;
  ranking: BestCard[];
  excluded: BestCard[];
}

export interface UtilizationPlanCard {
  accountId: string;
  name: string;
  nickname: string | null;
  institutionName: string | null;
  owner: string | null;
  balance: number;
  limit: number;
  utilizationBefore: number;
  toPay: number;
  utilizationAfter: number;
  nextClosingDate: string | null;
  payBy: string | null;
  daysUntilPayBy: number | null;
}

export interface UtilizationPlan {
  targetPercent: number;
  cards: UtilizationPlanCard[];
  skippedWithoutLimit: { accountId: string; name: string; nickname: string | null; institutionName: string | null }[];
  totalToPay: number;
  overallBefore: number | null;
  overallAfter: number | null;
}

export interface PayoffAllocation {
  accountId: string;
  name: string;
  nickname: string | null;
  institutionName: string | null;
  amountToPay: number;
  currentBalance: number;
  utilizationBefore: number | null;
  utilizationAfter: number | null;
  targetPercent: number;
  paymentDeadline: string | null;
  daysUntilPaymentDeadline: number | null;
  reason: string;
}

export interface PayoffPlan {
  availableAmount: number;
  allocatedTotal: number;
  remainingUnallocated: number;
  allocations: PayoffAllocation[];
}

@Injectable({ providedIn: 'root' })
export class CardsService {
  constructor(private readonly http: HttpClient) {}

  getCards(): Observable<Card[]> {
    return this.http.get<Card[]>('/api/cards');
  }

  updateCard(accountId: string, request: UpdateCardRequest): Observable<void> {
    return this.http.put<void>(`/api/cards/${accountId}`, request);
  }

  getSettings(): Observable<AppSettings> {
    return this.http.get<AppSettings>('/api/settings');
  }

  updateSettings(settings: AppSettings): Observable<AppSettings> {
    return this.http.put<AppSettings>('/api/settings', settings);
  }

  downloadReport(): Observable<Blob> {
    return this.http.get('/api/cards/report', { responseType: 'blob' });
  }

  getHistory(accountId: string): Observable<CardHistoryPoint[]> {
    return this.http.get<CardHistoryPoint[]>(`/api/cards/${accountId}/history`);
  }

  getBestCardToday(): Observable<BestCardResponse> {
    return this.http.get<BestCardResponse>('/api/cards/best-today');
  }

  getUtilizationPlan(target: number): Observable<UtilizationPlan> {
    return this.http.get<UtilizationPlan>('/api/cards/utilization-plan', { params: { target } });
  }

  getPayoffPlan(availableAmount: number): Observable<PayoffPlan> {
    return this.http.post<PayoffPlan>('/api/cards/payoff-plan', { availableAmount });
  }
}
