import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface CashFlowEntry {
  date: string;
  description: string;
  amount: number;
  balance: number;
  status: 'Done' | 'Pending';
}

export interface CashFlowResponse {
  startingBalance: number;
  currentBalance: number;
  entries: CashFlowEntry[];
}

export interface Bill {
  id: number;
  description: string;
  amount: number;
  dayOfMonth: number;
}

export interface BillRequest {
  description: string;
  amount: number;
  dayOfMonth: number;
}

export interface DetectedSubscription {
  merchantName: string;
  averageAmount: number;
  suggestedDayOfMonth: number;
  occurrences: number;
  lastDate: string;
  isPriceChange: boolean;
  previousAmount: number | null;
  existingBillId: number | null;
}

@Injectable({ providedIn: 'root' })
export class CashFlowService {
  constructor(private readonly http: HttpClient) {}

  getCashFlow(pastDays = 30, futureDays = 45): Observable<CashFlowResponse> {
    const params = new HttpParams()
      .set('pastDays', pastDays)
      .set('futureDays', futureDays);
    return this.http.get<CashFlowResponse>('/api/cashflow', { params });
  }

  downloadReport(pastDays = 30, futureDays = 45): Observable<Blob> {
    const params = new HttpParams()
      .set('pastDays', pastDays)
      .set('futureDays', futureDays);
    return this.http.get('/api/cashflow/report', { params, responseType: 'blob' });
  }

  getBills(): Observable<Bill[]> {
    return this.http.get<Bill[]>('/api/bills');
  }

  createBill(request: BillRequest): Observable<Bill> {
    return this.http.post<Bill>('/api/bills', request);
  }

  updateBill(id: number, request: BillRequest): Observable<Bill> {
    return this.http.put<Bill>(`/api/bills/${id}`, request);
  }

  deleteBill(id: number): Observable<void> {
    return this.http.delete<void>(`/api/bills/${id}`);
  }

  getDetectedSubscriptions(): Observable<DetectedSubscription[]> {
    return this.http.get<DetectedSubscription[]>('/api/bills/detected-subscriptions');
  }
}
