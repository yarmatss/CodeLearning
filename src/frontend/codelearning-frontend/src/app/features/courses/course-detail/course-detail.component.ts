import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DatePipe, CommonModule } from '@angular/common';
import { SafeHtml } from '@angular/platform-browser';
import { CourseService } from '../../../core/services/course.service';
import { AuthService } from '../../../core/services/auth.service';
import { MarkdownService } from '../../../core/services/markdown.service';
import { ProgressService } from '../../../core/services/progress.service';
import { DialogService } from '../../../core/services/dialog.service';
import { Course, CourseStatus } from '../../../core/models/course.model';
import { CourseProgress, CourseStructure } from '../../../core/models/progress.model';

@Component({
  selector: 'app-course-detail',
  imports: [RouterLink, DatePipe, CommonModule],
  templateUrl: './course-detail.component.html',
  styleUrl: './course-detail.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CourseDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  readonly courseService = inject(CourseService);
  readonly authService = inject(AuthService);
  readonly markdownService = inject(MarkdownService);
  readonly progressService = inject(ProgressService);
  readonly dialogService = inject(DialogService);

  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly isEnrolled = signal(false);
  readonly isEnrolling = signal(false);

  readonly course = signal<Course | null>(null);
  readonly courseProgress = signal<CourseProgress | null>(null);
  readonly courseStructure = signal<CourseStructure | null>(null);

  readonly canEdit = signal(false);

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
        
        // Check if user can edit
        const user = this.authService.currentUser();
        if (user && course.instructorId === user.userId && course.status === CourseStatus.Draft) {
          this.canEdit.set(true);
        } else {
          this.canEdit.set(false);
        }
      },
      error: (error: any) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to load course');
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
    this.progressService.getCourseProgress(courseId).subscribe({
      next: (progress) => {
        this.courseProgress.set(progress);
      },
      error: (error) => {
        console.error('Failed to load course progress:', error);
      }
    });
  }

  loadCourseStructure(courseId: string): void {
    this.courseService.getCourseStructure(courseId).subscribe({
      next: (structure) => {
        this.courseStructure.set(structure);
      },
      error: (error) => {
        console.error('Failed to load course structure:', error);
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
      error: (error: any) => {
        this.isEnrolling.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to enroll in course');
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
        error: (error: any) => {
          this.isEnrolling.set(false);
          this.errorMessage.set(error.error?.message || 'Failed to unenroll from course');
        }
      });
    }
  }

  publishCourse(): void {
    const courseId = this.course()?.id;
    if (!courseId) return;

    if (confirm('Are you sure you want to publish this course? Published courses cannot be edited.')) {
      this.courseService.publishCourse(courseId).subscribe({
        next: (response) => {
          this.course.set(response.course);
          this.canEdit.set(false);
        },
        error: (error: any) => {
          const errorMessage = error.error?.detail || error.error?.message || error.message || 'Failed to publish course';
          this.errorMessage.set(errorMessage);
        }
      });
    }
  }

  deleteCourse(): void {
    const courseId = this.course()?.id;
    if (!courseId) return;

    if (confirm('Are you sure you want to delete this course? This action cannot be undone.')) {
      this.courseService.deleteCourse(courseId).subscribe({
        next: () => {
          window.location.href = '/courses';
        },
        error: (error: any) => {
          const errorMessage = error.error?.detail || error.error?.message || error.message || 'Failed to delete course';
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

  renderDescription(description: string): SafeHtml {
    return this.markdownService.renderMarkdownSync(description);
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

  // Expose enum to template
  readonly CourseStatus = CourseStatus;
}
