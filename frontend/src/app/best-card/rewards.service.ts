import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export const BASE_REWARD_CATEGORY = 'BASE';

export interface RewardRate {
  category: string;
  ratePercent: number;
}

export interface CardRewards {
  accountId: string;
  rates: RewardRate[];
}

export interface RewardRanking {
  accountId: string;
  name: string;
  nickname: string | null;
  institutionName: string | null;
  owner: string | null;
  ratePercent: number;
  tier: 'recommended' | 'alternative' | 'caution' | null;
  daysUntilClosing: number | null;
  nextClosingDate: string | null;
  availableCredit: number | null;
  isBest: boolean;
}

@Injectable({ providedIn: 'root' })
export class RewardsService {
  constructor(private readonly http: HttpClient) {}

  getRates(): Observable<CardRewards[]> {
    return this.http.get<CardRewards[]>('/api/rewards');
  }

  setRates(accountId: string, rates: RewardRate[]): Observable<CardRewards> {
    return this.http.put<CardRewards>(`/api/rewards/${accountId}`, { rates });
  }

  bestFor(category: string): Observable<RewardRanking[]> {
    return this.http.get<RewardRanking[]>('/api/rewards/best-for', {
      params: new HttpParams().set('category', category),
    });
  }
}
