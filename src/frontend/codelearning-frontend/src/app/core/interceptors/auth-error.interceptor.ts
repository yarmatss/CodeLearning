import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService, SKIP_AUTH_REFRESH, returnUrlParams } from '../services/auth.service';

// Endpoints whose 401 means "wrong credentials" or "no session", never "access token expired"
const AUTH_ENDPOINTS = ['/api/auth/login', '/api/auth/register', '/api/auth/refresh', '/api/auth/logout'];

/**
 * On a 401 from an expired access token, refreshes the session once and retries the request.
 * Concurrent requests share the single refresh in flight (see `AuthService.refresh$`), so they all
 * retry when it succeeds and all fail at once when it does not.
 */
export const authErrorInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.context.get(SKIP_AUTH_REFRESH) || AUTH_ENDPOINTS.some(path => req.url.startsWith(path))) {
    return next(req);
  }

  const authService = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return authService.refresh$().pipe(
        catchError((refreshError: unknown) => {
          // Only a rejected refresh token ends the session; a network failure or server error
          // leaves the user signed in so a later request can try again
          if (refreshError instanceof HttpErrorResponse && refreshError.status === 401) {
            const wasSignedIn = authService.isAuthenticated();
            authService.endSession();
            if (wasSignedIn) {
              router.navigate(['/login'], { queryParams: returnUrlParams(router.url) });
            }
          }
          return throwError(() => error);
        }),
        switchMap(() => next(req))
      );
    })
  );
};
