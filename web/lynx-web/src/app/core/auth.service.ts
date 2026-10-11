import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface AuthStatus {
  setupRequired: boolean;
  authenticated: boolean;
  email: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly _status = signal<AuthStatus | null>(null);

  readonly status = this._status.asReadonly();
  readonly email = computed(() => this._status()?.email ?? null);

  /** Loads the status once; pass force to re-check with the server. */
  async load(force = false): Promise<AuthStatus> {
    const current = this._status();
    if (current && !force) return current;
    const status = await firstValueFrom(this.http.get<AuthStatus>('/api/auth/status'));
    this._status.set(status);
    return status;
  }

  async setup(email: string, password: string): Promise<void> {
    this._status.set(await firstValueFrom(this.http.post<AuthStatus>('/api/auth/setup', { email, password })));
  }

  async login(email: string, password: string): Promise<void> {
    this._status.set(await firstValueFrom(this.http.post<AuthStatus>('/api/auth/login', { email, password })));
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/logout', null));
    this._status.update((s) => (s ? { ...s, authenticated: false, email: null } : s));
  }

  /** The session ended on the server; reflect that locally so the guard redirects. */
  markSignedOut(): void {
    this._status.update((s) => (s ? { ...s, authenticated: false, email: null } : s));
  }
}

/** Pulls a readable message out of the API's error shapes. */
export function errorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error;
    if (body?.error) return body.error;
    if (Array.isArray(body?.errors)) return body.errors.join(' ');
    // 0 = no connection; 5xx = dev proxy couldn't reach the API; 200 = got HTML instead of JSON (proxy not active).
    if (err.status === 0 || err.status >= 500 || err.status === 200)
      return 'Cannot reach the API. Is it running (dotnet run --project src/Lynx.Api --launch-profile http)? If you just updated, restart npm start too.';
    if (err.status === 429) return 'Too many attempts. Wait a minute and try again.';
  }
  return 'Something went wrong.';
}
