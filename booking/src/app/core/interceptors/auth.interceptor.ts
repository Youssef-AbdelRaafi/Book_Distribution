import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject, Injector } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { environment } from '../../../environments/environment';

const AUTH_KEY = 'auth_token';
const EXPIRY_KEY = 'auth_expires_at';

function readValidToken(): string | null {
  const token = localStorage.getItem(AUTH_KEY);
  const expiresAt = localStorage.getItem(EXPIRY_KEY);
  if (!token || !expiresAt) return null;
  if (new Date(expiresAt).getTime() <= Date.now()) return null;
  return token;
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const injector = inject(Injector);
  const token = readValidToken();
  const isApiRequest = req.url.startsWith(environment.apiUrl);

  const authedReq = (!token || !isApiRequest)
    ? req
    : req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });

  return next(authedReq).pipe(
    catchError((error: HttpErrorResponse) => {
      const isLoginRequest = req.url.includes('/auth/login');
      if (error.status === 401 && isApiRequest && !isLoginRequest) {
        injector.get(AuthService).logout();
      }
      return throwError(() => error);
    })
  );
};
