import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';

describe('AuthService.restoreSession', () => {
  let auth: AuthService;
  let backend: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    auth = TestBed.inject(AuthService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    vi.useRealTimers();
  });

  const me = { userId: '1', email: 'a@b.c', firstName: 'A', lastName: 'B', role: 'Student' };

  it('schedules the refresh from accessTokenExpiresAt sent by /me', () => {
    auth.restoreSession().subscribe();
    const expiresAt = new Date(Date.now() + 60_000).toISOString();
    backend.expectOne('/api/auth/me').flush({ ...me, accessTokenExpiresAt: expiresAt });

    expect(auth.currentUser()).toEqual(me);
    backend.expectNone('/api/auth/refresh');

    vi.advanceTimersByTime(60_000);
    backend.expectOne('/api/auth/refresh').flush({ ...me, message: 'ok' });
  });

  it('works without accessTokenExpiresAt (no timer)', () => {
    auth.restoreSession().subscribe();
    backend.expectOne('/api/auth/me').flush(me);

    expect(auth.currentUser()?.userId).toBe('1');
    vi.advanceTimersByTime(10 * 60_000);
    backend.expectNone('/api/auth/refresh');
  });
});
