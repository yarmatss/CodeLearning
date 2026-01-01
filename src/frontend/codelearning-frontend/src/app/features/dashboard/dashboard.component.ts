import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { CourseService } from '../../core/services/course.service';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  templateUrl: './dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardComponent implements OnInit {
  readonly authService = inject(AuthService);
  readonly courseService = inject(CourseService);
  
  readonly isLoading = signal(false);

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
        next: () => {
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
        }
      });
    }
  }
}
