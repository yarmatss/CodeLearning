import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { CourseService } from '../../../core/services/course.service';
import { AuthService } from '../../../core/services/auth.service';
import { Course, CourseStatus } from '../../../core/models/course.model';

@Component({
  selector: 'app-course-detail',
  imports: [RouterLink, DatePipe],
  templateUrl: './course-detail.component.html',
  styleUrl: './course-detail.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CourseDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  readonly courseService = inject(CourseService);
  readonly authService = inject(AuthService);

  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly isEnrolled = signal(false);
  readonly isEnrolling = signal(false);

  readonly course = signal<Course | null>(null);

  readonly canEdit = signal(false);

  ngOnInit(): void {
    const courseId = this.route.snapshot.paramMap.get('id');
    if (courseId) {
      this.loadCourse(courseId);
      if (this.authService.isStudent()) {
        this.checkEnrollmentStatus(courseId);
      }
    }
  }

  loadCourse(id: string): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.courseService.getCourseById(id).subscribe({
      next: (course) => {
        this.course.set(course);
        this.isLoading.set(false);
        
        // Check if user can edit
        const user = this.authService.currentUser();
        if (user && course.instructorId === user.userId && course.status === CourseStatus.Draft) {
          this.canEdit.set(true);
        } else {
          this.canEdit.set(false);
        }
      },
      error: (error: any) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to load course');
      }
    });
  }

  checkEnrollmentStatus(courseId: string): void {
    this.courseService.getEnrollmentStatus(courseId).subscribe({
      next: (status) => {
        this.isEnrolled.set(status.isEnrolled);
      },
      error: () => {
        this.isEnrolled.set(false);
      }
    });
  }

  enrollInCourse(): void {
    const courseId = this.course()?.id;
    if (!courseId) return;

    this.isEnrolling.set(true);
    this.errorMessage.set('');

    this.courseService.enrollInCourse(courseId).subscribe({
      next: () => {
        this.isEnrolling.set(false);
        this.isEnrolled.set(true);
      },
      error: (error: any) => {
        this.isEnrolling.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to enroll in course');
      }
    });
  }

  publishCourse(): void {
    const courseId = this.course()?.id;
    if (!courseId) return;

    if (confirm('Are you sure you want to publish this course? Published courses cannot be edited.')) {
      this.courseService.publishCourse(courseId).subscribe({
        next: (response) => {
          this.course.set(response.course);
          this.canEdit.set(false);
        },
        error: (error: any) => {
          this.errorMessage.set(error.error?.message || 'Failed to publish course');
        }
      });
    }
  }

  deleteCourse(): void {
    const courseId = this.course()?.id;
    if (!courseId) return;

    if (confirm('Are you sure you want to delete this course? This action cannot be undone.')) {
      this.courseService.deleteCourse(courseId).subscribe({
        next: () => {
          window.location.href = '/courses';
        },
        error: (error: any) => {
          this.errorMessage.set(error.error?.message || 'Failed to delete course');
        }
      });
    }
  }

  getStatusBadgeClass(status: CourseStatus): string {
    return status === CourseStatus.Published
      ? 'bg-green-100 text-green-800'
      : 'bg-yellow-100 text-yellow-800';
  }

  getStatusText(status: CourseStatus): string {
    return status === CourseStatus.Published ? 'Published' : 'Draft';
  }

  // Expose enum to template
  readonly CourseStatus = CourseStatus;
}
