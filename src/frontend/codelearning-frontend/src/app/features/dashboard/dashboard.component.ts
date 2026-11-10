import { ChangeDetectionStrategy, Component, computed, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { Router, NavigationEnd, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { filter, Subscription } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { CourseService } from '../../core/services/course.service';
import { Course, CourseStatus, EnrolledCourse, TeacherStats } from '../../core/models/course.model';
import { MAX_PAGE_SIZE } from '../../core/models/paging.model';
import { extractApiError } from '../../core/utils/api-error';

// How many of the teacher's most recently created courses the dashboard lists
const RECENT_COURSES_COUNT = 5;
import { CourseSkeletonComponent } from '../../shared/components/course-skeleton/course-skeleton.component';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, CommonModule, CourseSkeletonComponent],
  templateUrl: './dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardComponent implements OnInit, OnDestroy {
  readonly authService = inject(AuthService);
  readonly courseService = inject(CourseService);
  private readonly router = inject(Router);
  
  readonly isLoading = signal(false);
  readonly errorMessage = signal('');
  readonly enrolledCourses = signal<EnrolledCourse[]>([]);
  // The enrollment list is paged; this is the student's real total even past the first page
  readonly enrolledTotal = signal(0);
  readonly teacherStats = signal<TeacherStats | null>(null);

  readonly CourseStatus = CourseStatus;

  readonly recentCourses = computed<Course[]>(() =>
    [...this.courseService.courses()]
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
      .slice(0, RECENT_COURSES_COUNT)
  );
  
  private routerSubscription?: Subscription;

  ngOnInit(): void {
    this.loadDashboardData();
    
    // Reload data when navigating back to dashboard
    this.routerSubscription = this.router.events
      .pipe(filter(event => event instanceof NavigationEnd))
      .subscribe((event: NavigationEnd) => {
        if (event.url === '/dashboard') {
          this.loadDashboardData();
        }
      });
  }

  ngOnDestroy(): void {
    this.routerSubscription?.unsubscribe();
  }

  loadDashboardData(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    if (this.authService.isTeacher()) {
      this.courseService.getMyStats().subscribe({
        next: (stats) => this.teacherStats.set(stats),
        error: (error: unknown) => this.errorMessage.set(extractApiError(error, 'Failed to load your statistics'))
      });
      this.courseService.getMyCourses().subscribe({
        next: () => this.isLoading.set(false),
        error: (error: unknown) => {
          this.isLoading.set(false);
          this.errorMessage.set(extractApiError(error, 'Failed to load your courses'));
        }
      });
    } else {
      // The largest page the API serves; the counters below are exact for up to that many enrollments
      this.courseService.getEnrolledCourses({ page: 1, pageSize: MAX_PAGE_SIZE }).subscribe({
        next: (page) => {
          this.enrolledCourses.set(page.items);
          this.enrolledTotal.set(page.totalCount);
          this.isLoading.set(false);
        },
        error: (error: unknown) => {
          this.isLoading.set(false);
          this.errorMessage.set(extractApiError(error, 'Failed to load your courses'));
        }
      });
    }
  }

  isCourseAvailable(course: EnrolledCourse): boolean {
    return course.courseStatus === CourseStatus.Published;
  }

  getProgressBarClass(percentage: number): string {
    if (percentage === 0) return 'bg-gray-300';
    if (percentage < 50) return 'bg-yellow-500';
    if (percentage < 100) return 'bg-blue-500';
    return 'bg-green-500';
  }

  getCompletedCoursesCount(): number {
    return this.enrolledCourses().filter(c => c.progressPercentage === 100).length;
  }

  getInProgressCoursesCount(): number {
    return this.enrolledCourses().filter(c => c.progressPercentage > 0 && c.progressPercentage < 100).length;
  }
}
