import { ChangeDetectionStrategy, Component, inject, signal, computed, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SafeHtml } from '@angular/platform-browser';
import { CourseService } from '../../../core/services/course.service';
import { AuthService } from '../../../core/services/auth.service';
import { MarkdownService } from '../../../core/services/markdown.service';
import { CourseStatus, EnrolledCourse } from '../../../core/models/course.model';
import { MAX_PAGE_SIZE } from '../../../core/models/paging.model';
import { extractApiError } from '../../../core/utils/api-error';
import { PaginationComponent } from '../../../shared/components/pagination/pagination.component';

// Published courses shown per page in the student catalog
const CATALOG_PAGE_SIZE = 12;

@Component({
  selector: 'app-course-list',
  imports: [RouterLink, PaginationComponent],
  templateUrl: './course-list.component.html',
  styleUrl: './course-list.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CourseListComponent implements OnInit {
  // Expose enum to template
  readonly CourseStatus = CourseStatus;
  readonly courseService = inject(CourseService);
  readonly authService = inject(AuthService);
  private readonly markdownService = inject(MarkdownService);

  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly searchQuery = signal('');
  readonly statusFilter = signal<'all' | 'draft' | 'published'>('all');
  readonly enrolledCourses = signal<EnrolledCourse[]>([]);

  // Student catalog paging (teachers get all their courses at once)
  readonly page = signal(1);
  readonly totalPages = signal(0);
  readonly totalCount = signal(0);

  // Each description is rendered once when the course list loads, not on every change detection pass
  readonly descriptionHtml = computed(() => new Map<string, SafeHtml>(
    this.courseService.courses().map(course => [course.id, this.markdownService.render(course.description)])
  ));

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
    this.errorMessage.set('');

    if (this.authService.isTeacher()) {
      // Teachers see all their courses (filtering happens in computed)
      this.isLoading.set(true);
      this.courseService.getMyCourses().subscribe({
        next: () => this.isLoading.set(false),
        error: (error: unknown) => {
          this.isLoading.set(false);
          this.errorMessage.set(extractApiError(error, 'Failed to load courses'));
        }
      });
      return;
    }

    this.loadCatalogPage(1);

    // Enrollment badges are optional decoration; the course list still works without them.
    // The largest page covers every enrollment of all but the most prolific students.
    this.courseService.getEnrolledCourses({ page: 1, pageSize: MAX_PAGE_SIZE }).subscribe({
      next: (enrolled) => this.enrolledCourses.set(enrolled.items),
      error: () => this.enrolledCourses.set([])
    });
  }

  loadCatalogPage(page: number): void {
    this.isLoading.set(true);
    this.errorMessage.set('');
    this.courseService.getPublishedCourses({ page, pageSize: CATALOG_PAGE_SIZE }).subscribe({
      next: (result) => {
        this.page.set(result.page);
        this.totalPages.set(result.totalPages);
        this.totalCount.set(result.totalCount);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.isLoading.set(false);
        this.errorMessage.set(extractApiError(error, 'Failed to load courses'));
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


  isEnrolledIn(courseId: string): boolean {
    return this.enrolledCourses().some(ec => ec.courseId === courseId);
  }

  getCourseProgress(courseId: string): number {
    const enrolled = this.enrolledCourses().find(ec => ec.courseId === courseId);
    return enrolled?.progressPercentage ?? 0;
  }
}
