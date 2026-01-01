import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import {
  Course,
  CreateCourseRequest,
  UpdateCourseRequest,
  Enrollment,
  EnrolledCourse,
  EnrollmentStatus
} from '../models/course.model';

@Injectable({
  providedIn: 'root'
})
export class CourseService {
  private readonly http = inject(HttpClient);
  private readonly API_URL = '/api/courses';
  private readonly ENROLLMENT_URL = '/api/enrollments';

  courses = signal<Course[]>([]);
  selectedCourse = signal<Course | null>(null);

  // Teacher: Get my courses
  getMyCourses(): Observable<Course[]> {
    return this.http.get<Course[]>(`${this.API_URL}/my-courses`).pipe(
      tap(courses => this.courses.set(courses))
    );
  }

  // Public: Get published courses
  getPublishedCourses(): Observable<Course[]> {
    return this.http.get<Course[]>(`${this.API_URL}/published`).pipe(
      tap(courses => this.courses.set(courses))
    );
  }

  // Get course by ID
  getCourseById(id: string): Observable<Course> {
    return this.http.get<Course>(`${this.API_URL}/${id}`).pipe(
      tap(course => this.selectedCourse.set(course))
    );
  }

  // Teacher: Create course
  createCourse(data: CreateCourseRequest): Observable<Course> {
    return this.http.post<Course>(this.API_URL, data).pipe(
      tap(course => {
        this.courses.update(courses => [...courses, course]);
      })
    );
  }

  // Teacher: Update course
  updateCourse(id: string, data: UpdateCourseRequest): Observable<Course> {
    return this.http.put<Course>(`${this.API_URL}/${id}`, data).pipe(
      tap(updatedCourse => {
        this.courses.update(courses =>
          courses.map(c => c.id === id ? updatedCourse : c)
        );
        if (this.selectedCourse()?.id === id) {
          this.selectedCourse.set(updatedCourse);
        }
      })
    );
  }

  // Teacher: Publish course
  publishCourse(id: string): Observable<{ message: string; course: Course }> {
    return this.http.post<{ message: string; course: Course }>(`${this.API_URL}/${id}/publish`, {}).pipe(
      tap(response => {
        this.courses.update(courses =>
          courses.map(c => c.id === id ? response.course : c)
        );
        if (this.selectedCourse()?.id === id) {
          this.selectedCourse.set(response.course);
        }
      })
    );
  }

  // Teacher: Delete course
  deleteCourse(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.API_URL}/${id}`).pipe(
      tap(() => {
        this.courses.update(courses => courses.filter(c => c.id !== id));
        if (this.selectedCourse()?.id === id) {
          this.selectedCourse.set(null);
        }
      })
    );
  }

  // Student: Get enrolled courses
  getEnrolledCourses(): Observable<EnrolledCourse[]> {
    return this.http.get<EnrolledCourse[]>(`${this.ENROLLMENT_URL}/my-courses`);
  }

  // Student: Enroll in course
  enrollInCourse(courseId: string): Observable<Enrollment> {
    return this.http.post<Enrollment>(`${this.ENROLLMENT_URL}/courses/${courseId}`, {});
  }

  // Student: Unenroll from course
  unenrollFromCourse(courseId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.ENROLLMENT_URL}/courses/${courseId}`);
  }

  // Student: Check enrollment status
  getEnrollmentStatus(courseId: string): Observable<EnrollmentStatus> {
    return this.http.get<EnrollmentStatus>(`${this.ENROLLMENT_URL}/courses/${courseId}/status`);
  }
}
