import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

export interface AuthUser {
  id: string;
  email: string;
  name: string | null;
  picture: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly baseUrl = '/api/auth';

  readonly user = signal<AuthUser | null>(null);
  readonly checkedSession = signal(false);

  constructor(private readonly http: HttpClient) {}

  checkSession(): void {
    this.http.get<AuthUser>(`${this.baseUrl}/me`).subscribe({
      next: (user) => {
        this.user.set(user);
        this.checkedSession.set(true);
      },
      error: () => {
        this.user.set(null);
        this.checkedSession.set(true);
      },
    });
  }

  loginWithGoogle(idToken: string): Observable<AuthUser> {
    return this.http
      .post<AuthUser>(`${this.baseUrl}/google`, { idToken })
      .pipe(tap((user) => this.user.set(user)));
  }

  logout(): void {
    this.http.post(`${this.baseUrl}/logout`, {}).subscribe({
      next: () => this.user.set(null),
    });
  }
}
