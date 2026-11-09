import { ChangeDetectionStrategy, Component, computed, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ChapterService } from '../../../core/services/chapter.service';
import { CourseService } from '../../../core/services/course.service';
import { Chapter, Course, CourseStatus, Subchapter } from '../../../core/models/course.model';
import { DialogService } from '../../../core/services/dialog.service';
import { extractApiError } from '../../../core/utils/api-error';
import { CourseLockBannerComponent } from '../../../shared/components/course-lock-banner/course-lock-banner.component';

@Component({
  selector: 'app-chapter-editor',
  imports: [ReactiveFormsModule, RouterLink, CourseLockBannerComponent],
  templateUrl: './chapter-editor.html',
  styleUrl: './chapter-editor.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ChapterEditor implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly chapterService = inject(ChapterService);
  private readonly courseService = inject(CourseService);
  private readonly dialogService = inject(DialogService);

  readonly course = signal<Course | null>(null);
  readonly chapter = signal<Chapter | null>(null);
  readonly isRenaming = signal(false);
  readonly renameForm: FormGroup;
  // The API refuses every change to a published course
  readonly isLocked = computed(() => this.course()?.status === CourseStatus.Published);
  readonly subchapters = signal<Subchapter[]>([]);
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');

  readonly subchapterForm: FormGroup;
  readonly isAddingSubchapter = signal(false);

  courseId = '';
  chapterId = '';

  constructor() {
    this.subchapterForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(3)]]
    });

    this.renameForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(3)]]
    });
  }

  ngOnInit(): void {
    this.courseId = this.route.snapshot.paramMap.get('courseId') ?? '';
    this.chapterId = this.route.snapshot.paramMap.get('chapterId') ?? '';

    if (this.chapterId) {
      this.loadCourseAndChapter();
      this.loadSubchapters();
    }
  }

  /** The chapter has no endpoint of its own, so it is picked from the course's chapter list. */
  loadCourseAndChapter(): void {
    this.courseService.getCourseById(this.courseId).subscribe({
      next: (course) => this.course.set(course),
      error: (error: unknown) => this.errorMessage.set(extractApiError(error, 'Failed to load course'))
    });
    this.chapterService.getCourseChapters(this.courseId).subscribe({
      next: (chapters) => {
        const chapter = chapters.find(c => c.id === this.chapterId) ?? null;
        this.chapter.set(chapter);
        if (!chapter) {
          this.errorMessage.set('Chapter not found');
        }
      },
      error: (error: unknown) => this.errorMessage.set(extractApiError(error, 'Failed to load chapter'))
    });
  }

  onUnpublished(course: Course): void {
    this.course.set(course);
  }

  startRename(): void {
    this.renameForm.reset({ title: this.chapter()?.title ?? '' });
    this.isRenaming.set(true);
  }

  cancelRename(): void {
    this.isRenaming.set(false);
  }

  saveRename(): void {
    if (this.renameForm.invalid) {
      this.renameForm.markAllAsTouched();
      return;
    }

    this.errorMessage.set('');
    const title = (this.renameForm.value.title as string).trim();
    this.chapterService.renameChapter(this.courseId, this.chapterId, { title }).subscribe({
      next: (chapter) => {
        this.chapter.set(chapter);
        this.isRenaming.set(false);
        this.successMessage.set('Chapter renamed');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to rename chapter'));
      }
    });
  }

  loadSubchapters(): void {
    this.isLoading.set(true);
    this.chapterService.getChapterSubchapters(this.chapterId).subscribe({
      next: (subchapters) => {
        this.subchapters.set(subchapters);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.isLoading.set(false);
        this.errorMessage.set(extractApiError(error, 'Failed to load subchapters'));
      }
    });
  }

  toggleAddSubchapter(): void {
    this.isAddingSubchapter.update(v => !v);
    if (!this.isAddingSubchapter()) {
      this.subchapterForm.reset();
    }
  }

  addSubchapter(): void {
    if (this.subchapterForm.invalid) {
      this.subchapterForm.markAllAsTouched();
      return;
    }

    this.errorMessage.set('');

    this.chapterService.addSubchapter(this.chapterId, this.subchapterForm.value).subscribe({
      next: (subchapter) => {
        this.subchapters.update(subs => [...subs, subchapter]);
        this.subchapterForm.reset();
        this.isAddingSubchapter.set(false);
        this.successMessage.set('Subchapter added successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to add subchapter'));
      }
    });
  }

  moveSubchapterUp(subchapterId: string, currentIndex: number): void {
    if (currentIndex === 1) return;

    const newIndex = currentIndex - 1;
    this.chapterService.updateSubchapterOrder(this.chapterId, subchapterId, newIndex).subscribe({
      next: () => {
        this.loadSubchapters();
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to reorder subchapter'));
      }
    });
  }

  moveSubchapterDown(subchapterId: string, currentIndex: number): void {
    const maxIndex = this.subchapters().length;
    if (currentIndex === maxIndex) return;

    const newIndex = currentIndex + 1;
    this.chapterService.updateSubchapterOrder(this.chapterId, subchapterId, newIndex).subscribe({
      next: () => {
        this.loadSubchapters();
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to reorder subchapter'));
      }
    });
  }

  async deleteSubchapter(subchapterId: string): Promise<void> {
    const { confirmed } = await this.dialogService.confirm({
      title: 'Delete subchapter?',
      message: 'Are you sure you want to delete this subchapter? This will also delete all blocks.',
      confirmText: 'Delete',
      cancelText: 'Cancel',
      type: 'warning'
    });
    if (!confirmed) {
      return;
    }

    this.chapterService.deleteSubchapter(this.chapterId, subchapterId).subscribe({
      next: () => {
        this.loadSubchapters();
        this.successMessage.set('Subchapter deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to delete subchapter'));
      }
    });
  }
}
