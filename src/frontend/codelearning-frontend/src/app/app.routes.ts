import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/guards/auth.guard';
import { studentGuard, teacherGuard } from './core/guards/role.guard';

// Lazy routes: each page, and heavy dependencies such as the Monaco editor on the learn page,
// load only when visited instead of in the initial bundle

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'login', loadComponent: () => import('./features/auth/login/login.component').then(m => m.LoginComponent), canActivate: [guestGuard] },
  { path: 'register', loadComponent: () => import('./features/auth/register/register.component').then(m => m.RegisterComponent), canActivate: [guestGuard] },
  { 
    path: 'dashboard', 
    loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent),
    canActivate: [authGuard]
  },
  {
    path: 'courses',
    loadComponent: () => import('./features/courses/course-list/course-list.component').then(m => m.CourseListComponent),
    canActivate: [authGuard]
  },
  {
    path: 'courses/create',
    loadComponent: () => import('./features/courses/create-course/create-course.component').then(m => m.CreateCourseComponent),
    canActivate: [authGuard, teacherGuard]
  },
  {
    path: 'courses/:id',
    loadComponent: () => import('./features/courses/course-detail/course-detail.component').then(m => m.CourseDetailComponent),
    canActivate: [authGuard]
  },
  {
    path: 'learn/:courseId',
    loadComponent: () => import('./features/learn/learn').then(m => m.Learn),
    canActivate: [authGuard, studentGuard]
  },
  {
    path: 'courses/:id/edit',
    loadComponent: () => import('./features/courses/course-editor/course-editor.component').then(m => m.CourseEditorComponent),
    canActivate: [authGuard, teacherGuard]
  },
  {
    path: 'courses/:courseId/chapters/:chapterId/edit',
    loadComponent: () => import('./features/courses/chapter-editor/chapter-editor').then(m => m.ChapterEditor),
    canActivate: [authGuard, teacherGuard]
  },
  {
    path: 'courses/:courseId/chapters/:chapterId/subchapters/:subchapterId/edit',
    loadComponent: () => import('./features/courses/subchapter-editor/subchapter-editor').then(m => m.SubchapterEditor),
    canActivate: [authGuard, teacherGuard]
  },
  {
    path: 'problems',
    loadComponent: () => import('./features/problems/problem-list/problem-list').then(m => m.ProblemList),
    canActivate: [authGuard, teacherGuard]
  },
  {
    path: 'problems/create',
    loadComponent: () => import('./features/problems/problem-editor/problem-editor').then(m => m.ProblemEditor),
    canActivate: [authGuard, teacherGuard]
  },
  {
    path: 'problems/:id',
    loadComponent: () => import('./features/problems/problem-detail/problem-detail').then(m => m.ProblemDetail),
    canActivate: [authGuard, teacherGuard]
  },
  {
    path: 'problems/:id/edit',
    loadComponent: () => import('./features/problems/problem-editor/problem-editor').then(m => m.ProblemEditor),
    canActivate: [authGuard, teacherGuard]
  }
];
