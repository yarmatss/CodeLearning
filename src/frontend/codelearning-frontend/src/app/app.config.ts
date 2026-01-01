import { ApplicationConfig, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './core/services/auth.service';

import { routes } from './app.routes';
import { credentialsInterceptor } from './core/interceptors/credentials.interceptor';
import { authErrorInterceptor } from './core/interceptors/auth-error.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([credentialsInterceptor, authErrorInterceptor])
    ),
    provideAppInitializer(() => {
      const authService = inject(AuthService);
      return new Promise<void>((resolve) => {
        authService.getCurrentUser().subscribe({
          next: () => resolve(),
          error: () => resolve() // Silent fail - user not logged in
        });
      });
    })
  ]
};
