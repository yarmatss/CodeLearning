import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { authErrorInterceptor } from './auth-error.interceptor';
import { AuthService } from '../services/auth.service';

describe('authErrorInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authErrorInterceptor])),
        provideHttpClientTesting()
      ]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    auth.currentUser.set({ userId: '1', email: 'a@b.c', firstName: 'A', lastName: 'B', role: 'Student' });
  });

  afterEach(() => backend.verify());

  it('shares one refresh between concurrent 401s and retries both requests', () => {
    const results: string[] = [];
    http.get<string>('/api/a').subscribe(r => results.push(r));
    http.get<string>('/api/b').subscribe(r => results.push(r));

    backend.expectOne('/api/a').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/b').flush(null, { status: 401, statusText: 'Unauthorized' });

    backend.expectOne('/api/auth/refresh').flush({ accessTokenExpiresAt: undefined });

    backend.expectOne('/api/a').flush('a');
    backend.expectOne('/api/b').flush('b');
    expect(results.sort()).toEqual(['a', 'b']);
  });

  it('fails every waiting request at once and ends the session when the refresh is rejected', () => {
    const errors: number[] = [];
    http.get('/api/a').subscribe({ error: e => errors.push(e.status) });
    http.get('/api/b').subscribe({ error: e => errors.push(e.status) });

    backend.expectOne('/api/a').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/b').flush(null, { status: 401, statusText: 'Unauthorized' });

    backend.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });
    // Another tab may have rotated the cookie: the session is probed once before giving up
    backend.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(errors).toEqual([401, 401]);
    expect(auth.currentUser()).toBeNull();
  });

  it('keeps the session when the refresh is rejected but another tab already renewed it', () => {
    const results: string[] = [];
    http.get<string>('/api/a').subscribe(r => results.push(r));

    backend.expectOne('/api/a').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/auth/me').flush({ userId: '1', email: 'a@b.c', firstName: 'A', lastName: 'B', role: 'Student' });
    backend.expectOne('/api/a').flush('a');

    expect(results).toEqual(['a']);
    expect(auth.currentUser()).not.toBeNull();
  });
});
