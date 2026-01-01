import { Injectable, signal, computed, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { User, RegisterRequest, LoginRequest, AuthResponse } from '../models/auth.model';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly API_URL = '/api/auth';

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
      })
    );
  }

  logout(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.API_URL}/logout`, {}).pipe(
      tap(() => {
        this.currentUser.set(null);
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
        this.currentUser.set({
          userId: response.userId,
          email: response.email,
          firstName: response.firstName,
          lastName: response.lastName,
          role: response.role
        });
      })
    );
  }
}
