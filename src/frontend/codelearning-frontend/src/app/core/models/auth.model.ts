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
  accessToken?: string;
  refreshToken?: string;
}
