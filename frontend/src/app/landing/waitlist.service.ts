import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class WaitlistService {
  constructor(private readonly http: HttpClient) {}

  join(email: string, note: string, language: string, website: string): Observable<void> {
    return this.http.post<void>('/api/waitlist', { email, note, language, website });
  }
}
