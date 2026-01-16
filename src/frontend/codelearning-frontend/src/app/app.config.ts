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
          next: () => {
            console.log('[APP_INIT] User loaded, calling refreshToken...');
            // Użytkownik załadowany - odśwież token aby dostać exp i zaplanować timer
            authService.refreshToken().subscribe({
              next: () => {
                console.log('[APP_INIT] Token refreshed successfully');
                resolve();
              },
              error: (err) => {
                console.error('[APP_INIT] Token refresh failed:', err);
                resolve(); // Token refresh failed, authErrorInterceptor will handle it
              }
            });
          },
          error: (err) => {
            console.log('[APP_INIT] User not logged in:', err.status);
            // Użytkownik nie jest zalogowany
            resolve();
          }
        });
      });
    })
  ]
};
