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
  daysUntilClosing: number | null;
  nextPaymentDueDate: string | null;
  minimumPaymentAmount: number | null;
  isOverdue: boolean | null;
  needsAlert: boolean;
}

export interface UpdateCardRequest {
  statementClosingDay: number | null;
  targetUtilizationPercent: number | null;
}

export interface AppSettings {
  globalTargetUtilizationPercent: number;
  notifyDaysBeforeClosing: number;
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
}
