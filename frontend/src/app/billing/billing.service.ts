import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

export interface BillingStatus {
  enabled: boolean;
  hasAccess: boolean;
  reason: string;
  status: 'trial' | 'active' | 'past_due' | 'canceled' | 'none';
  trialEndsAt: string | null;
  currentPeriodEnd: string | null;
  canManage: boolean;
  checkoutAvailable: boolean;
  priceLabel: string | null;
}

@Injectable({ providedIn: 'root' })
export class BillingService {
  // Último status carregado (o menu e o Dashboard usam pra avisar quando o acesso acabou).
  readonly status = signal<BillingStatus | null>(null);

  constructor(private readonly http: HttpClient) {}

  loadStatus(): Observable<BillingStatus> {
    return this.http.get<BillingStatus>('/api/billing/status').pipe(tap((s) => this.status.set(s)));
  }

  checkout(): Observable<{ url: string }> {
    return this.http.post<{ url: string }>('/api/billing/checkout', {});
  }

  portal(): Observable<{ url: string }> {
    return this.http.post<{ url: string }>('/api/billing/portal', {});
  }
}
