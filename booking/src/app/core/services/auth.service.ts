import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { ToastService } from './toast.service';
import { AppDataService } from './app-data.service';
import { ApiResponse } from '../models/api-response.model';
import { environment } from '../../../environments/environment';
import { LS_AUTH_TOKEN, LS_AUTH_EXPIRES_AT } from '../constants/local-storage-keys';

interface LoginResponse {
  success: boolean;
  token?: string;
  expiresAt?: string;
  message?: string;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly AUTH_KEY = LS_AUTH_TOKEN;
  private readonly EXPIRY_KEY = LS_AUTH_EXPIRES_AT;
  private readonly apiUrl = `${environment.apiUrl}/auth`;
  
  // Reactive signal for login state
  isAuthenticated = signal<boolean>(this.checkAuth());

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

  login(username: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/login`, { username, password });
  }

  changePassword(currentPassword: string, newPassword: string): Observable<ApiResponse<unknown>> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/change-password`, { currentPassword, newPassword });
  }

  handleLoginResponse(res: LoginResponse): void {
    if (!res.success || !res.token || !res.expiresAt) {
      this.toast.show(res.message || 'تعذر تسجيل الدخول', 'error');
      return;
    }
    localStorage.setItem(this.AUTH_KEY, res.token);
    localStorage.setItem(this.EXPIRY_KEY, res.expiresAt);
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
    this.router.navigate(['/login']);
  }

  private clearSession(): void {
    localStorage.removeItem(this.AUTH_KEY);
    localStorage.removeItem(this.EXPIRY_KEY);
  }
}
