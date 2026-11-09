import { inject } from '@angular/core';
import { Router, CanActivateFn, UrlTree } from '@angular/router';
import { AuthService, returnUrlParams } from '../services/auth.service';

/** Sends an anonymous visitor to sign in, remembering where they were going. */
export function loginUrlTree(router: Router, returnUrl: string): UrlTree {
  return router.createUrlTree(['/login'], { queryParams: returnUrlParams(returnUrl) });
}

export const authGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  return authService.isAuthenticated() ? true : loginUrlTree(inject(Router), state.url);
};

/** Keeps signed-in users off the login and register pages. */
export const guestGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  return authService.isAuthenticated() ? inject(Router).createUrlTree(['/dashboard']) : true;
};
