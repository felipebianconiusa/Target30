import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface MonthlyComparisonRow {
  category: string;
  currentMonthTotal: number;
  previousMonthTotal: number;
  changePercent: number | null;
}

export interface HealthScore {
  score: number;
  cardComponent: number;
  spendingComponent: number;
  label: 'great' | 'good' | 'fair' | 'poor';
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  constructor(private readonly http: HttpClient) {}

  getMonthlyComparison(): Observable<MonthlyComparisonRow[]> {
    return this.http.get<MonthlyComparisonRow[]>('/api/dashboard/monthly-comparison');
  }

  getHealthScore(): Observable<HealthScore> {
    return this.http.get<HealthScore>('/api/dashboard/health-score');
  }
}
