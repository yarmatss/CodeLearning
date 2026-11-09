import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { Course, CourseStatus } from '../../../core/models/course.model';
import { CourseService } from '../../../core/services/course.service';
import { AuthService } from '../../../core/services/auth.service';
import { DialogService } from '../../../core/services/dialog.service';
import { extractApiError } from '../../../core/utils/api-error';

/**
 * Shown on the course editors while the course is published: the API refuses every change to a
 * published course, so the editors disable their controls and the owner can unpublish from here.
 */
@Component({
  selector: 'app-course-lock-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isPublished()) {
      <div class="mb-6 rounded-md border border-yellow-200 bg-yellow-50 p-4" role="status">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <p class="text-sm text-yellow-800">
            🔒 This course is published, so its content can't be edited.
            @if (isOwner()) {
              Unpublish it to make changes; enrolled students keep their progress but can't open
              the lessons until you publish again.
            }
          </p>
          @if (isOwner()) {
            <button
              type="button"
              (click)="unpublish()"
              [disabled]="isWorking()"
              class="rounded-md bg-yellow-600 px-4 py-2 text-sm text-white hover:bg-yellow-700 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {{ isWorking() ? 'Unpublishing...' : 'Unpublish Course' }}
            </button>
          }
        </div>
        @if (error()) {
          <p class="mt-2 whitespace-pre-line text-sm text-red-700">{{ error() }}</p>
        }
      </div>
    }
  `
})
export class CourseLockBannerComponent {
  private readonly courseService = inject(CourseService);
  private readonly authService = inject(AuthService);
  private readonly dialogService = inject(DialogService);

  readonly course = input<Course | null>(null);
  /** Emits the course returned by the API once it is unpublished. */
  readonly unpublished = output<Course>();

  readonly isWorking = signal(false);
  readonly error = signal('');

  readonly isPublished = computed(() => this.course()?.status === CourseStatus.Published);
  readonly isOwner = computed(() => {
    const course = this.course();
    return !!course && course.instructorId === this.authService.currentUser()?.userId;
  });

  async unpublish(): Promise<void> {
    const course = this.course();
    if (!course || this.isWorking()) return;

    const { confirmed } = await this.dialogService.confirm({
      title: 'Unpublish course?',
      message: 'Students will not be able to open the lessons until you publish the course again. Their enrollments and progress are kept.',
      confirmText: 'Unpublish',
      cancelText: 'Cancel',
      type: 'warning'
    });
    if (!confirmed) return;

    this.isWorking.set(true);
    this.error.set('');
    this.courseService.unpublishCourse(course.id).subscribe({
      next: response => {
        this.isWorking.set(false);
        this.unpublished.emit(response.course);
      },
      error: (error: unknown) => {
        this.isWorking.set(false);
        this.error.set(extractApiError(error, 'Failed to unpublish course'));
      }
    });
  }
}
