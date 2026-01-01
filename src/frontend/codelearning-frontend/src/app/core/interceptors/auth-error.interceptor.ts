import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError, switchMap } from 'rxjs';
import { AuthService } from '../services/auth.service';

let isRefreshing = false;

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
          authService.currentUser.set(null);
          router.navigate(['/login']);
        }
        return throwError(() => error);
      }

      // Prevent multiple simultaneous refresh attempts
      if (isRefreshing) {
        return throwError(() => error);
      }

      isRefreshing = true;

      // Try to refresh the token
      return authService.refreshToken().pipe(
        switchMap(() => {
          isRefreshing = false;
          // Retry the original request with new token from cookie
          return next(req);
        }),
        catchError((refreshError) => {
          isRefreshing = false;
          // Refresh failed - logout user
          authService.currentUser.set(null);
          router.navigate(['/login']);
          return throwError(() => refreshError);
        })
      );
    })
  );
};
