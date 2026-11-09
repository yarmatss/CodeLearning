import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
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
    // Guards read the current user synchronously, so the session is restored before the first
    // navigation. `/me` is asked first; the refresh token is rotated only if that answers 401.
    provideAppInitializer(() => firstValueFrom(inject(AuthService).restoreSession(), { defaultValue: undefined }))
  ]
};
