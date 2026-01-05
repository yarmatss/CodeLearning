import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError, switchMap, filter, take, BehaviorSubject, timeout, of } from 'rxjs';
import { AuthService } from '../services/auth.service';

let isRefreshing = false;
let refreshTokenSubject: BehaviorSubject<boolean> = new BehaviorSubject<boolean>(false);

export const authErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      // Only handle 401 Unauthorized errors
      if (error.status !== 401) {
        return throwError(() => error);
      }

      // Don't retry on login, register, or refresh endpoints
      const url = req.url;
      if (url.includes('/auth/login') || 
          url.includes('/auth/register') || 
          url.includes('/auth/refresh')) {
        // If refresh fails, logout user
        if (url.includes('/auth/refresh')) {
          isRefreshing = false;
          refreshTokenSubject.next(false);
          authService.currentUser.set(null);
          router.navigate(['/login']);
        }
        return throwError(() => error);
      }

      // If already refreshing, wait for refresh to complete (max 10 seconds)
      if (isRefreshing) {
        return refreshTokenSubject.pipe(
          filter(completed => completed === true),
          take(1),
          timeout(10000),
          switchMap(() => {
            // Retry the original request after refresh completes
            return next(req);
          }),
          catchError((timeoutError) => {
            // Timeout or error waiting for refresh
            console.error('Timeout waiting for token refresh');
            isRefreshing = false;
            refreshTokenSubject.next(false);
            authService.currentUser.set(null);
            router.navigate(['/login']);
            return throwError(() => timeoutError);
          })
        );
      }

      // Start refresh process
      isRefreshing = true;
      refreshTokenSubject.next(false);

      // Try to refresh the token
      return authService.refreshToken().pipe(
        switchMap(() => {
          // Refresh successful
          isRefreshing = false;
          refreshTokenSubject.next(true);
          // Retry the original request with new token from cookie
          return next(req);
        }),
        catchError((refreshError) => {
          // Refresh failed - logout user
          isRefreshing = false;
          refreshTokenSubject.next(false);
          authService.currentUser.set(null);
          router.navigate(['/login']);
          return throwError(() => refreshError);
        })
      );
    })
  );
};
