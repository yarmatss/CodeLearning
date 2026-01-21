import { ChangeDetectionStrategy, Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { Router, NavigationEnd, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { filter, Subscription } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { CourseService } from '../../core/services/course.service';
import { EnrolledCourse } from '../../core/models/course.model';
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
  readonly enrolledCourses = signal<EnrolledCourse[]>([]);
  
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
    
    if (this.authService.isTeacher()) {
      this.courseService.getMyCourses().subscribe({
        next: () => {
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
        }
      });
    } else {
      this.courseService.getEnrolledCourses().subscribe({
        next: (courses) => {
          this.enrolledCourses.set(courses);
          this.isLoading.set(false);
        },
        error: (error) => {
          console.error('Error loading enrolled courses:', error);
          this.isLoading.set(false);
        }
      });
    }
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
