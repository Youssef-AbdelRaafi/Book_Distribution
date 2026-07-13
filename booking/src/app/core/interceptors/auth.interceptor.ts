import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject, Injector } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { environment } from '../../../environments/environment';
import { LS_AUTH_TOKEN, LS_AUTH_EXPIRES_AT } from '../constants/local-storage-keys';

const AUTH_KEY = LS_AUTH_TOKEN;
const EXPIRY_KEY = LS_AUTH_EXPIRES_AT;

function readValidToken(): string | null {
  const token = localStorage.getItem(AUTH_KEY);
  const expiresAt = localStorage.getItem(EXPIRY_KEY);
  if (!token || !expiresAt) return null;
  if (new Date(expiresAt).getTime() <= Date.now()) return null;
  return token;
}

const SKIP_AUTH_HEADER = 'X-Skip-Auth-Interceptor';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.headers.has(SKIP_AUTH_HEADER)) {
    return next(req.clone({ headers: req.headers.delete(SKIP_AUTH_HEADER) }));
  }

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
