import { ChangeDetectionStrategy, Component, computed, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe, CommonModule } from '@angular/common';
import { CourseService } from '../../../core/services/course.service';
import { AuthService } from '../../../core/services/auth.service';
import { MarkdownService } from '../../../core/services/markdown.service';
import { ProgressService } from '../../../core/services/progress.service';
import { DialogService } from '../../../core/services/dialog.service';
import { BlockType, Course, CourseStatus } from '../../../core/models/course.model';
import { CourseProgress, CourseStructure } from '../../../core/models/progress.model';
import { extractApiError, isAccessDenied } from '../../../core/utils/api-error';

@Component({
  selector: 'app-course-detail',
  imports: [RouterLink, DatePipe, CommonModule],
  templateUrl: './course-detail.component.html',
  styleUrl: './course-detail.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CourseDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly courseService = inject(CourseService);
  readonly authService = inject(AuthService);
  private readonly markdownService = inject(MarkdownService);
  readonly progressService = inject(ProgressService);
  readonly dialogService = inject(DialogService);

  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly isEnrolled = signal(false);
  readonly isEnrolling = signal(false);

  readonly course = signal<Course | null>(null);
  readonly courseProgress = signal<CourseProgress | null>(null);
  readonly courseStructure = signal<CourseStructure | null>(null);

  // Set when the API refuses the enrolled student's progress (e.g. the course was unpublished)
  readonly progressUnavailable = signal('');

  readonly isOwner = computed(() => {
    const course = this.course();
    return !!course && course.instructorId === this.authService.currentUser()?.userId;
  });
  // Only drafts can be edited; a published course must be unpublished first
  readonly canEdit = computed(() => this.isOwner() && this.course()?.status === CourseStatus.Draft);
  readonly isUnpublishing = signal(false);

  // Rendered once per course change instead of on every change detection pass
  readonly descriptionHtml = computed(() => this.markdownService.render(this.course()?.description));

  ngOnInit(): void {
    const courseId = this.route.snapshot.paramMap.get('id');
    if (courseId) {
      this.loadCourse(courseId);
      if (this.authService.isStudent()) {
        this.checkEnrollmentStatus(courseId);
      } else {
        // For non-students (teachers, unauthenticated), load structure
        this.loadCourseStructure(courseId);
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
      },
      error: (error: unknown) => {
        this.isLoading.set(false);
        this.errorMessage.set(extractApiError(error, 'Failed to load course'));
      }
    });
  }

  checkEnrollmentStatus(courseId: string): void {
    this.courseService.getEnrollmentStatus(courseId).subscribe({
      next: (status) => {
        this.isEnrolled.set(status.isEnrolled);

        // If enrolled, load progress with structure
        if (status.isEnrolled) {
          this.loadCourseProgress(courseId);
        } else {
          // If not enrolled, load structure without progress
          this.loadCourseStructure(courseId);
        }
      },
      error: () => {
        this.isEnrolled.set(false);
        // On error, load structure anyway
        this.loadCourseStructure(courseId);
      }
    });
  }

  loadCourseProgress(courseId: string): void {
    this.progressUnavailable.set('');
    this.progressService.getCourseProgress(courseId).subscribe({
      next: (progress) => {
        this.courseProgress.set(progress);
      },
      error: (error: unknown) => {
        if (isAccessDenied(error)) {
          // Still show the public outline, with the reason the lessons can't be opened
          this.progressUnavailable.set(extractApiError(error, 'This course is not available at the moment'));
          this.loadCourseStructure(courseId);
          return;
        }
        this.errorMessage.set(extractApiError(error, 'Failed to load your progress'));
      }
    });
  }

  loadCourseStructure(courseId: string): void {
    this.courseService.getCourseStructure(courseId).subscribe({
      next: (structure) => {
        this.courseStructure.set(structure);
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to load the course outline'));
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
        // After enrollment, load progress instead of structure
        this.loadCourseProgress(courseId);
      },
      error: (error: unknown) => {
        this.isEnrolling.set(false);
        this.errorMessage.set(extractApiError(error, 'Failed to enroll in course'));
      }
    });
  }

  async unenrollFromCourse(): Promise<void> {
    const courseId = this.course()?.id;
    const courseTitle = this.course()?.title;
    if (!courseId) return;

    const result = await this.dialogService.confirm({
      title: `Unenroll from "${courseTitle}"?`,
      message: 'Your progress will be permanently lost. This action cannot be undone.',
      confirmText: 'Unenroll',
      cancelText: 'Cancel',
      type: 'warning'
    });

    if (result.confirmed) {
      this.isEnrolling.set(true);
      this.errorMessage.set('');

      this.courseService.unenrollFromCourse(courseId).subscribe({
        next: () => {
          this.isEnrolling.set(false);
          this.isEnrolled.set(false);
          // After unenrolling, clear progress and load structure
          this.courseProgress.set(null);
          this.loadCourseStructure(courseId);
        },
        error: (error: unknown) => {
          this.isEnrolling.set(false);
          this.errorMessage.set(extractApiError(error, 'Failed to unenroll from course'));
        }
      });
    }
  }

  async publishCourse(): Promise<void> {
    const courseId = this.course()?.id;
    if (!courseId) return;

    const { confirmed } = await this.dialogService.confirm({
      title: 'Publish course?',
      message: 'Students will be able to enroll and learn. While it is published the course and the problems it uses cannot be edited; you can unpublish it later to make changes.',
      confirmText: 'Publish',
      cancelText: 'Cancel',
      type: 'warning'
    });
    if (confirmed) {
      this.errorMessage.set('');
      this.courseService.publishCourse(courseId).subscribe({
        next: (response) => {
          this.course.set(response.course);
        },
        error: (error: unknown) => {
          const errorMessage = extractApiError(error, 'Failed to publish course');
          this.errorMessage.set(errorMessage);
        }
      });
    }
  }

  async unpublishCourse(): Promise<void> {
    const courseId = this.course()?.id;
    if (!courseId || this.isUnpublishing()) return;

    const { confirmed } = await this.dialogService.confirm({
      title: 'Unpublish course?',
      message: 'Students will not be able to open the lessons until you publish the course again. Their enrollments and progress are kept.',
      confirmText: 'Unpublish',
      cancelText: 'Cancel',
      type: 'warning'
    });
    if (!confirmed) return;

    this.isUnpublishing.set(true);
    this.errorMessage.set('');
    this.courseService.unpublishCourse(courseId).subscribe({
      next: (response) => {
        this.isUnpublishing.set(false);
        this.course.set(response.course);
      },
      error: (error: unknown) => {
        this.isUnpublishing.set(false);
        this.errorMessage.set(extractApiError(error, 'Failed to unpublish course'));
      }
    });
  }

  async deleteCourse(): Promise<void> {
    const courseId = this.course()?.id;
    if (!courseId) return;

    const { confirmed } = await this.dialogService.confirm({
      title: 'Delete course?',
      message: 'Are you sure you want to delete this course? This action cannot be undone.',
      confirmText: 'Delete',
      cancelText: 'Cancel',
      type: 'warning'
    });
    if (confirmed) {
      this.courseService.deleteCourse(courseId).subscribe({
        next: () => {
          this.router.navigate(['/courses']);
        },
        error: (error: unknown) => {
          const errorMessage = extractApiError(error, 'Failed to delete course');
          this.errorMessage.set(errorMessage);
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


  getCompletedBlocksInSubchapter(subchapterId: string): number {
    const progress = this.courseProgress();
    if (!progress) return 0;

    for (const chapter of progress.chapters) {
      const subchapter = chapter.subchapters.find(s => s.subchapterId === subchapterId);
      if (subchapter) {
        return subchapter.blocks.filter(b => b.isCompleted).length;
      }
    }
    return 0;
  }

  getCompletedBlocksInChapter(chapterId: string): number {
    const progress = this.courseProgress();
    if (!progress) return 0;

    const chapter = progress.chapters.find(c => c.chapterId === chapterId);
    if (!chapter) return 0;

    return chapter.subchapters.reduce((total, subchapter) => {
      return total + subchapter.blocks.filter(b => b.isCompleted).length;
    }, 0);
  }

  getTotalBlocksInChapter(chapterId: string): number {
    const progress = this.courseProgress();
    if (!progress) return 0;

    const chapter = progress.chapters.find(c => c.chapterId === chapterId);
    if (!chapter) return 0;

    return chapter.subchapters.reduce((total, subchapter) => {
      return total + subchapter.blocks.length;
    }, 0);
  }

  getTotalBlocksInChapterFromStructure(chapterId: string): number {
    const structure = this.courseStructure();
    if (!structure) return 0;

    const chapter = structure.chapters.find(c => c.chapterId === chapterId);
    if (!chapter) return 0;

    return chapter.subchapters.reduce((total, subchapter) => {
      return total + subchapter.blocksCount;
    }, 0);
  }

  blockTypeIcon(type: BlockType): string {
    switch (type) {
      case BlockType.Theory: return '📄';
      case BlockType.Video: return '🎥';
      case BlockType.Quiz: return '❓';
      case BlockType.Problem: return '💻';
      default: return '•';
    }
  }

  // Expose enum to template
  readonly CourseStatus = CourseStatus;
}
