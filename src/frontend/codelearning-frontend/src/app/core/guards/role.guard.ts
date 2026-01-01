import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const teacherGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isTeacher()) {
    return true;
  }

  router.navigate(['/dashboard']);
  return false;
};

export const studentGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isStudent()) {
    return true;
  }

  router.navigate(['/dashboard']);
  return false;
};
