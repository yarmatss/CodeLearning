import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { teacherGuard } from './core/guards/role.guard';
import { LoginComponent } from './features/auth/login/login.component';
import { RegisterComponent } from './features/auth/register/register.component';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { CourseListComponent } from './features/courses/course-list/course-list.component';
import { CourseDetailComponent } from './features/courses/course-detail/course-detail.component';
import { CreateCourseComponent } from './features/courses/create-course/create-course.component';
import { CourseEditorComponent } from './features/courses/course-editor/course-editor.component';
import { ChapterEditor } from './features/courses/chapter-editor/chapter-editor';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { 
    path: 'dashboard', 
    component: DashboardComponent,
    canActivate: [authGuard]
  },
  {
    path: 'courses',
    component: CourseListComponent,
    canActivate: [authGuard]
  },
  {
    path: 'courses/create',
    component: CreateCourseComponent,
    canActivate: [authGuard, teacherGuard]
  },
  {
    path: 'courses/:id',
    component: CourseDetailComponent,
    canActivate: [authGuard]
  },
  {
    path: 'courses/:id/edit',
    component: CourseEditorComponent,
    canActivate: [authGuard, teacherGuard]
  },
  {
    path: 'courses/:courseId/chapters/:chapterId/edit',
    component: ChapterEditor,
    canActivate: [authGuard, teacherGuard]
  }
];
