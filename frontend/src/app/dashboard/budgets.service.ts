import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface Budget {
  id: number;
  category: string;
  monthlyLimit: number;
  currentSpend: number;
  percentUsed: number | null;
}

export interface BudgetRequest {
  category: string;
  monthlyLimit: number;
}

@Injectable({ providedIn: 'root' })
export class BudgetsService {
  constructor(private readonly http: HttpClient) {}

  getBudgets(): Observable<Budget[]> {
    return this.http.get<Budget[]>('/api/budgets');
  }

  createBudget(request: BudgetRequest): Observable<Budget> {
    return this.http.post<Budget>('/api/budgets', request);
  }

  updateBudget(id: number, request: BudgetRequest): Observable<Budget> {
    return this.http.put<Budget>(`/api/budgets/${id}`, request);
  }

  deleteBudget(id: number): Observable<void> {
    return this.http.delete<void>(`/api/budgets/${id}`);
  }
}
