import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Adds the CSRF header the API requires and bounces to the login page when the session expires. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api')) return next(req);

  const auth = inject(AuthService);
  const router = inject(Router);
  const isAuthCall = req.url.startsWith('/api/auth/');

  return next(req.clone({ setHeaders: { 'X-Lynx-Request': '1' } })).pipe(
    catchError((err) => {
      if (err.status === 401 && !isAuthCall) {
        auth.markSignedOut();
        router.navigateByUrl('/login');
      }
      return throwError(() => err);
    }),
  );
};
