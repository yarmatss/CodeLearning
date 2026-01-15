import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';
import { CourseService } from '../../core/services/course.service';
import { EnrolledCourse } from '../../core/models/course.model';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, CommonModule],
  templateUrl: './dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardComponent implements OnInit {
  readonly authService = inject(AuthService);
  readonly courseService = inject(CourseService);
  
  readonly isLoading = signal(false);
  readonly enrolledCourses = signal<EnrolledCourse[]>([]);

  ngOnInit(): void {
    this.loadDashboardData();
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
          console.log('Enrolled courses:', courses);
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
