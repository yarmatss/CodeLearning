import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { loginUrlTree } from './auth.guard';

export const teacherGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return loginUrlTree(router, state.url);
  }
  return authService.isTeacher() ? true : router.createUrlTree(['/dashboard']);
};

export const studentGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return loginUrlTree(router, state.url);
  }
  return authService.isStudent() ? true : router.createUrlTree(['/dashboard']);
};
