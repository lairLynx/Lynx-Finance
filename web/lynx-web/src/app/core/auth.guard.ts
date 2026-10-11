import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  try {
    const status = await auth.load();
    return status.authenticated ? true : router.parseUrl('/login');
  } catch {
    // API unreachable: the login page shows the connection error.
    return router.parseUrl('/login');
  }
};
