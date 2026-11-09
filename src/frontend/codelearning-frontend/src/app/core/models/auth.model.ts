export interface User {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  role: 'Student' | 'Teacher';
}

export interface RegisterRequest {
  email: string;
  password: string;
  confirmPassword: string;
  firstName: string;
  lastName: string;
  role: 'Student' | 'Teacher';
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthResponse {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  role: 'Student' | 'Teacher';
  message: string;
  accessTokenExpiresAt?: string;
}

/** GET /api/auth/me. `accessTokenExpiresAt` is optional: newer servers send it so a reloaded page can schedule its refresh. */
export interface CurrentUserResponse extends User {
  accessTokenExpiresAt?: string;
}
