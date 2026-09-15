import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface LinkTokenResponse {
  linkToken: string;
}

export interface ExchangeTokenResponse {
  itemId: string;
}

@Injectable({ providedIn: 'root' })
export class PlaidService {
  private readonly baseUrl = '/api/plaid';

  constructor(private readonly http: HttpClient) {}

  createLinkToken(): Observable<LinkTokenResponse> {
    return this.http.post<LinkTokenResponse>(`${this.baseUrl}/link-token`, {});
  }

  exchangePublicToken(publicToken: string): Observable<ExchangeTokenResponse> {
    return this.http.post<ExchangeTokenResponse>(`${this.baseUrl}/exchange-token`, {
      publicToken,
    });
  }
}
