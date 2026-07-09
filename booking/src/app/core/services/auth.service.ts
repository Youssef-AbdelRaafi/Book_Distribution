import { Injectable, signal, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { ToastService } from './toast.service';
import { AppDataService } from './app-data.service';
import { environment } from '../../../environments/environment';

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
  private readonly AUTH_KEY = 'auth_token';
  private readonly EXPIRY_KEY = 'auth_expires_at';
  private readonly apiUrl = `${environment.apiUrl}/auth`;
  
  // Reactive signal for login state
  isAuthenticated = signal<boolean>(this.checkAuth());

  constructor(
    private router: Router,
    private toast: ToastService,
    private http: HttpClient,
    private appData: AppDataService
  ) {}

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

  login(username: string, password: string): void {
    this.http.post<LoginResponse>(`${this.apiUrl}/login`, { username, password }).subscribe({
      next: (response) => {
        if (!response.success || !response.token || !response.expiresAt) {
          this.toast.show(response.message || 'تعذر تسجيل الدخول', 'error');
          return;
        }

        localStorage.setItem(this.AUTH_KEY, response.token);
        localStorage.setItem(this.EXPIRY_KEY, response.expiresAt);
        this.isAuthenticated.set(true);
        this.appData.loadAuthenticatedData();
        this.router.navigate(['/single-page']);
      },
      error: (error) => {
        const message = error?.error?.message || 'اسم المستخدم أو كلمة المرور غير صحيحة';
        this.toast.show(message, 'error');
      }
    });
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
