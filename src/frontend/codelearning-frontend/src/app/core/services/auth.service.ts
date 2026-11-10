import { Injectable, signal, computed, inject } from '@angular/core';
import { HttpClient, HttpContext, HttpContextToken, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import {
  Observable,
  catchError,
  defer,
  finalize,
  from,
  map,
  of,
  shareReplay,
  switchMap,
  tap,
  throwError
} from 'rxjs';
import { User, RegisterRequest, LoginRequest, AuthResponse, CurrentUserResponse } from '../models/auth.model';
import { CourseService } from './course.service';
import { LanguageService } from './language.service';
import { DialogService } from './dialog.service';

/**
 * Marks a request the auth interceptor must pass through untouched: the refresh call itself and
 * the session probes made while a refresh is being decided. Retrying those on 401 would make the
 * refresh wait for itself.
 */
export const SKIP_AUTH_REFRESH = new HttpContextToken<boolean>(() => false);

// Web Locks name shared by every tab of the app, so only one tab rotates the refresh cookie at a time
const REFRESH_LOCK = 'cl-refresh';

// Refresh this long before the access token expires
const REFRESH_MARGIN_MS = 5000;

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly courseService = inject(CourseService);
  private readonly languageService = inject(LanguageService);
  private readonly dialogService = inject(DialogService);

  private readonly API_URL = '/api/auth';
  private refreshTimer?: ReturnType<typeof setTimeout>;
  private refreshInFlight$: Observable<void> | null = null;

  readonly currentUser = signal<User | null>(null);
  readonly isAuthenticated = computed(() => !!this.currentUser());
  readonly isTeacher = computed(() => this.currentUser()?.role === 'Teacher');
  readonly isStudent = computed(() => this.currentUser()?.role === 'Student');

  register(data: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.API_URL}/register`, data).pipe(
      tap(response => this.startSession(response))
    );
  }

  login(data: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.API_URL}/login`, data).pipe(
      tap(response => this.startSession(response))
    );
  }

  logout(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.API_URL}/logout`, {}).pipe(
      // The local session ends even if the request fails (e.g. network error); the server
      // clears the cookies and revokes the refresh token whenever the request reaches it
      catchError(() => of({ message: 'Logged out' })),
      finalize(() => {
        this.endSession();
        this.router.navigate(['/login']);
      })
    );
  }

  getCurrentUser(): Observable<User> {
    return this.http.get<CurrentUserResponse>(`${this.API_URL}/me`).pipe(
      map(response => this.applyCurrentUser(response))
    );
  }

  /**
   * Restores the session at start-up without rotating tokens needlessly: asks `/me` first and
   * refreshes only when the access cookie is missing or expired. Never errors; an anonymous
   * visitor simply ends up with no current user.
   */
  restoreSession(): Observable<void> {
    return this.probeCurrentUser().pipe(
      map(() => undefined),
      catchError((error: unknown) => {
        if (!isUnauthorized(error)) {
          return of(undefined);
        }
        return this.refresh$().pipe(
          switchMap(() => (this.currentUser() ? of(undefined) : this.probeCurrentUser().pipe(map(() => undefined)))),
          catchError(() => {
            this.endSession();
            return of(undefined);
          })
        );
      })
    );
  }

  /**
   * Refreshes the access token. While a refresh is in flight every caller (the timer, the
   * interceptor, start-up) shares it instead of sending its own; the shared observable is dropped
   * once it settles, so the next call starts a fresh refresh.
   *
   * Errors when the session cannot be renewed. It does not end the session itself: the caller
   * decides whether to sign the user out and where to send them.
   */
  refresh$(): Observable<void> {
    if (!this.refreshInFlight$) {
      this.refreshInFlight$ = defer(() => this.refreshAcrossTabs()).pipe(
        finalize(() => {
          this.refreshInFlight$ = null;
        }),
        shareReplay({ bufferSize: 1, refCount: false })
      );
    }
    return this.refreshInFlight$;
  }

  /**
   * Clears every trace of the signed-in user kept in the browser: the user, the refresh timer,
   * cached root-level data and any queued dialog. Navigation is left to the caller.
   */
  endSession(): void {
    this.clearRefreshTimer();
    this.currentUser.set(null);
    this.courseService.reset();
    this.languageService.reset();
    this.dialogService.reset();
  }

  private startSession(response: AuthResponse): void {
    this.currentUser.set({
      userId: response.userId,
      email: response.email,
      firstName: response.firstName,
      lastName: response.lastName,
      role: response.role
    });
    this.scheduleTokenRefresh(response.accessTokenExpiresAt);
  }

  private probeCurrentUser(): Observable<User> {
    return this.http
      .get<CurrentUserResponse>(`${this.API_URL}/me`, { context: skipAuthRefresh() })
      .pipe(map(response => this.applyCurrentUser(response)));
  }

  /**
   * Stores the user from `/me` and, when the server says when the access token expires, schedules
   * the refresh, so a reloaded page refreshes ahead of expiry instead of waiting for the first 401.
   */
  private applyCurrentUser(response: CurrentUserResponse): User {
    const user: User = {
      userId: response.userId,
      email: response.email,
      firstName: response.firstName,
      lastName: response.lastName,
      role: response.role
    };
    this.currentUser.set(user);
    if (response.accessTokenExpiresAt) {
      this.scheduleTokenRefresh(response.accessTokenExpiresAt);
    }
    return user;
  }

  private refreshAcrossTabs(): Observable<void> {
    const attempt = () =>
      this.http.post<AuthResponse>(`${this.API_URL}/refresh`, {}, { context: skipAuthRefresh() }).pipe(
        tap(response => this.scheduleTokenRefresh(response.accessTokenExpiresAt)),
        map(() => undefined as void),
        catchError((error: unknown) => {
          if (!isUnauthorized(error)) {
            return throwError(() => error);
          }
          // Another tab (or device sharing the cookie jar) may have rotated the refresh token a
          // moment ago, so the cookie this request carried was already spent. If the session
          // still answers `/me`, the cookies are valid again and the session lives on.
          return this.probeCurrentUser().pipe(
            map(() => undefined as void),
            catchError(() => throwError(() => error))
          );
        })
      );

    const locks = typeof navigator !== 'undefined' ? navigator.locks : undefined;
    if (!locks?.request) {
      return attempt();
    }

    // Holding the lock for the whole request keeps two tabs from presenting the same refresh
    // token: the second tab waits and then sends the cookie the first one received
    // (the lock is released when the callback's promise settles; request() resolves with its value)
    const locked = locks.request(REFRESH_LOCK, () => new Promise<void>((resolve, reject) => {
      attempt().subscribe({ next: () => resolve(), error: reject });
    })) as Promise<unknown>;
    return from(locked).pipe(map(() => undefined as void));
  }

  private scheduleTokenRefresh(accessTokenExpiresAt: string | undefined): void {
    this.clearRefreshTimer();
    if (!accessTokenExpiresAt) {
      return;
    }

    const expiresAt = new Date(accessTokenExpiresAt).getTime();
    if (Number.isNaN(expiresAt)) {
      return;
    }

    // An expiry already inside the margin is left to the interceptor, which refreshes on the
    // first 401
    const refreshIn = expiresAt - Date.now() - REFRESH_MARGIN_MS;
    if (refreshIn <= 0) {
      return;
    }

    this.refreshTimer = setTimeout(() => {
      this.refreshTimer = undefined;
      this.refresh$().subscribe({
        error: () => {
          this.endSession();
          this.router.navigate(['/login'], { queryParams: returnUrlParams(this.router.url) });
        }
      });
    }, refreshIn);
  }

  private clearRefreshTimer(): void {
    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = undefined;
    }
  }
}

function skipAuthRefresh(): HttpContext {
  return new HttpContext().set(SKIP_AUTH_REFRESH, true);
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 401;
}

/** Query params that bring the user back to `url` after signing in, unless it is a guest page. */
export function returnUrlParams(url: string): { returnUrl?: string } {
  if (!url || url === '/' || url.startsWith('/login') || url.startsWith('/register')) {
    return {};
  }
  return { returnUrl: url };
}
