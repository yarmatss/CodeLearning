import { ChangeDetectionStrategy, Component, inject, signal, computed, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SafeHtml } from '@angular/platform-browser';
import { CourseService } from '../../../core/services/course.service';
import { AuthService } from '../../../core/services/auth.service';
import { MarkdownService } from '../../../core/services/markdown.service';
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
  readonly markdownService = inject(MarkdownService);

  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly searchQuery = signal('');
  readonly statusFilter = signal<'all' | 'draft' | 'published'>('all');

  readonly filteredCourses = computed(() => {
    let courses = this.courseService.courses();
    const query = this.searchQuery().toLowerCase();
    const status = this.statusFilter();

    // Filter by status (only for teachers)
    if (this.authService.isTeacher() && status !== 'all') {
      courses = courses.filter(course => {
        if (status === 'draft') {
          return course.status === CourseStatus.Draft;
        } else if (status === 'published') {
          return course.status === CourseStatus.Published;
        }
        return true;
      });
    }

    // Filter by search query
    if (query) {
      courses = courses.filter(course =>
        course.title.toLowerCase().includes(query) ||
        course.description.toLowerCase().includes(query) ||
        course.instructorName.toLowerCase().includes(query)
      );
    }

    return courses;
  });

  ngOnInit(): void {
    this.loadCourses();
  }

  loadCourses(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    // Teachers see all their courses (filtering happens in computed)
    // Students see only published courses
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

  onStatusFilterChange(): void {
    // Filtering happens automatically via computed signal
  }

  onStatusChange(status: 'all' | 'draft' | 'published'): void {
    this.statusFilter.set(status);
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

  renderDescription(description: string): SafeHtml {
    return this.markdownService.renderMarkdownSync(description);
  }
}
