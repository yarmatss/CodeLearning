import { Injectable, signal, computed, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { User, RegisterRequest, LoginRequest, AuthResponse } from '../models/auth.model';
import { jwtDecode } from 'jwt-decode';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly API_URL = '/api/auth';
  private refreshTimer?: ReturnType<typeof setTimeout>;

  currentUser = signal<User | null>(null);
  isAuthenticated = computed(() => !!this.currentUser());
  isTeacher = computed(() => this.currentUser()?.role === 'Teacher');
  isStudent = computed(() => this.currentUser()?.role === 'Student');

  register(data: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.API_URL}/register`, data).pipe(
      tap(response => {
        this.currentUser.set({
          userId: response.userId,
          email: response.email,
          firstName: response.firstName,
          lastName: response.lastName,
          role: response.role
        });
        if (response.accessToken) {
          this.scheduleTokenRefresh(response.accessToken);
        }
      })
    );
  }

  login(data: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.API_URL}/login`, data).pipe(
      tap(response => {
        this.currentUser.set({
          userId: response.userId,
          email: response.email,
          firstName: response.firstName,
          lastName: response.lastName,
          role: response.role
        });
        if (response.accessToken) {
          this.scheduleTokenRefresh(response.accessToken);
        }
      })
    );
  }

  logout(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.API_URL}/logout`, {}).pipe(
      tap(() => {
        this.currentUser.set(null);
        this.clearRefreshTimer();
        this.router.navigate(['/login']);
      })
    );
  }

  getCurrentUser(): Observable<User> {
    return this.http.get<User>(`${this.API_URL}/me`).pipe(
      tap(user => this.currentUser.set(user))
    );
  }

  refreshToken(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.API_URL}/refresh`, {}).pipe(
      tap(response => {
        // Nie nadpisujemy currentUser - refresh służy tylko do odświeżenia tokena
        // currentUser jest już ustawiony przez getCurrentUser() lub login()/register()
        if (response.accessToken) {
          this.scheduleTokenRefresh(response.accessToken);
        }
      })
    );
  }

  private scheduleTokenRefresh(accessToken: string): void {
    try {
      const decoded: any = jwtDecode(accessToken);
      
      if (!decoded.exp) {
        console.warn('JWT token doesn\'t have exp claim');
        return;
      }

      // exp is in seconds, convert to milliseconds
      const expiresAt = decoded.exp * 1000;
      const now = Date.now();
      
      // Schedule refresh 5 seconds before expiration
      const refreshIn = expiresAt - now - 5000;

      if (refreshIn > 0) {
        this.clearRefreshTimer();
        this.refreshTimer = setTimeout(() => {
          console.log('Auto-refreshing token...');
          this.refreshToken().subscribe({
            error: (err) => console.error('Token refresh failed:', err)
          });
        }, refreshIn);
        
        const refreshDate = new Date(expiresAt - 5000);
        console.log(`Token refresh scheduled for: ${refreshDate.toLocaleString()}`);
      } else {
        console.warn('Token has already expired or will expire in less than 5 seconds');
      }
    } catch (error) {
      console.error('Error decoding JWT:', error);
    }
  }

  private clearRefreshTimer(): void {
    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = undefined;
    }
  }
}