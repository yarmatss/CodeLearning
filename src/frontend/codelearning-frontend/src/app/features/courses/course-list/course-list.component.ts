import { ChangeDetectionStrategy, Component, inject, signal, computed, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CourseService } from '../../../core/services/course.service';
import { AuthService } from '../../../core/services/auth.service';
import { Course, CourseStatus } from '../../../core/models/course.model';

@Component({
  selector: 'app-course-list',
  imports: [RouterLink],
  templateUrl: './course-list.component.html',
  styleUrl: './course-list.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CourseListComponent implements OnInit {
  // Expose enum to template
  readonly CourseStatus = CourseStatus;
  readonly courseService = inject(CourseService);
  readonly authService = inject(AuthService);

  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly searchQuery = signal('');

  readonly filteredCourses = computed(() => {
    const courses = this.courseService.courses();
    const query = this.searchQuery().toLowerCase();

    if (!query) {
      return courses;
    }

    return courses.filter(course =>
      course.title.toLowerCase().includes(query) ||
      course.description.toLowerCase().includes(query) ||
      course.instructorName.toLowerCase().includes(query)
    );
  });

  ngOnInit(): void {
    this.loadCourses();
  }

  loadCourses(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    const observable = this.authService.isTeacher()
      ? this.courseService.getMyCourses()
      : this.courseService.getPublishedCourses();

    observable.subscribe({
      next: () => {
        this.isLoading.set(false);
      },
      error: (error: any) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to load courses');
      }
    });
  }

  onSearch(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.searchQuery.set(input.value);
  }

  getStatusBadgeClass(status: CourseStatus): string {
    return status === CourseStatus.Published
      ? 'bg-green-100 text-green-800'
      : 'bg-yellow-100 text-yellow-800';
  }

  getStatusText(status: CourseStatus): string {
    return status === CourseStatus.Published ? 'Published' : 'Draft';
  }
}
