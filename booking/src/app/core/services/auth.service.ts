import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { ToastService } from './toast.service';
import { AppDataService } from './app-data.service';
import { ApiResponse } from '../models/api-response.model';
import { environment } from '../../../environments/environment';
import { LS_AUTH_TOKEN, LS_AUTH_EXPIRES_AT } from '../constants/local-storage-keys';

interface LoginPayload {
  success: boolean;
  token?: string;
  expiresAt?: string;
  message?: string;
  tenantId?: number;
  isGuest?: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly AUTH_KEY = LS_AUTH_TOKEN;
  private readonly EXPIRY_KEY = LS_AUTH_EXPIRES_AT;
  private readonly IS_GUEST_KEY = 'is_guest_account';
  private readonly apiUrl = `${environment.apiUrl}/auth`;
  
  // Reactive signal for login state & account type
  isAuthenticated = signal<boolean>(this.checkAuth());
  isGuestAccount = signal<boolean>(this.checkGuestState());

  constructor(
    private router: Router,
    private toast: ToastService,
    private http: HttpClient,
    private appData: AppDataService
  ) {
    if (this.isAuthenticated()) {
      this.appData.loadAuthenticatedData();
    }
  }

  private checkAuth(): boolean {
    const token = localStorage.getItem(this.AUTH_KEY);
    const expiresAt = localStorage.getItem(this.EXPIRY_KEY);

    if (!token || !expiresAt) {
      return false;
    }

    if (new Date(expiresAt).getTime() <= Date.now()) {
      this.clearSession();
      return false;
    }

    return true;
  }

  private checkGuestState(): boolean {
    const isGuestStr = localStorage.getItem(this.IS_GUEST_KEY);
    if (isGuestStr !== null) {
      return isGuestStr === 'true';
    }
    const token = localStorage.getItem(this.AUTH_KEY);
    if (!token) return false;
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return payload.IsGuest === 'true' || payload.isGuest === 'true' || payload.TenantId === '2';
    } catch {
      return false;
    }
  }

  login(username: string, password: string): Observable<ApiResponse<LoginPayload>> {
    return this.http.post<ApiResponse<LoginPayload>>(`${this.apiUrl}/login`, { username, password });
  }

  changePassword(currentPassword: string, newPassword: string): Observable<ApiResponse<unknown>> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/change-password`, { currentPassword, newPassword });
  }

  resetGuestData(): Observable<ApiResponse<unknown>> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/reset-guest`, {});
  }

  handleLoginResponse(res: ApiResponse<LoginPayload>): void {
    const payload = res.data;
    if (!res.success || !payload?.success || !payload.token || !payload.expiresAt) {
      this.toast.show(res.message || payload?.message || 'تعذر تسجيل الدخول', 'error');
      return;
    }
    localStorage.setItem(this.AUTH_KEY, payload.token);
    localStorage.setItem(this.EXPIRY_KEY, payload.expiresAt);
    const isGuest = payload.isGuest || payload.tenantId === 2;
    localStorage.setItem(this.IS_GUEST_KEY, isGuest ? 'true' : 'false');
    this.isGuestAccount.set(isGuest);

    this.isAuthenticated.set(true);
    this.appData.loadAuthenticatedData();
    const returnUrl = sessionStorage.getItem('returnUrl') || '/single-page';
    sessionStorage.removeItem('returnUrl');
    this.router.navigate([returnUrl]);
  }

  getToken(): string | null {
    const isValid = this.checkAuth();
    this.isAuthenticated.set(isValid);
    return isValid ? localStorage.getItem(this.AUTH_KEY) : null;
  }

  logout() {
    this.clearSession();
    this.isAuthenticated.set(false);
    this.isGuestAccount.set(false);
    this.router.navigate(['/login']);
  }

  private clearSession(): void {
    localStorage.removeItem(this.AUTH_KEY);
    localStorage.removeItem(this.EXPIRY_KEY);
    localStorage.removeItem(this.IS_GUEST_KEY);
  }
}
