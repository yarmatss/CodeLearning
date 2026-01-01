import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { CourseService } from '../../../core/services/course.service';
import { ChapterService } from '../../../core/services/chapter.service';
import { Course, Chapter, UpdateCourseRequest } from '../../../core/models/course.model';

@Component({
  selector: 'app-course-editor',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './course-editor.component.html',
  styleUrl: './course-editor.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CourseEditorComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  readonly courseService = inject(CourseService);
  private readonly chapterService = inject(ChapterService);

  readonly course = signal<Course | null>(null);
  readonly chapters = signal<Chapter[]>([]);
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');
  
  readonly courseForm: FormGroup;
  readonly chapterForm: FormGroup;
  readonly isEditingCourse = signal(false);
  readonly isAddingChapter = signal(false);

  constructor() {
    this.courseForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(3)]],
      description: ['', [Validators.required, Validators.minLength(10)]]
    });

    this.chapterForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(3)]]
    });
  }

  ngOnInit(): void {
    const courseId = this.route.snapshot.paramMap.get('id');
    if (courseId) {
      this.loadCourse(courseId);
      this.loadChapters(courseId);
    }
  }

  loadCourse(id: string): void {
    this.isLoading.set(true);
    this.courseService.getCourseById(id).subscribe({
      next: (course) => {
        this.course.set(course);
        this.courseForm.patchValue({
          title: course.title,
          description: course.description
        });
        this.isLoading.set(false);
      },
      error: (error: any) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to load course');
      }
    });
  }

  loadChapters(courseId: string): void {
    this.chapterService.getCourseChapters(courseId).subscribe({
      next: (chapters) => {
        this.chapters.set(chapters);
      },
      error: () => {
        // Silent error - chapters might be empty
        this.chapters.set([]);
      }
    });
  }

  toggleEditCourse(): void {
    this.isEditingCourse.update(v => !v);
    if (!this.isEditingCourse()) {
      // Reset form if canceling
      const course = this.course();
      if (course) {
        this.courseForm.patchValue({
          title: course.title,
          description: course.description
        });
      }
    }
  }

  saveCourse(): void {
    if (this.courseForm.invalid) {
      this.courseForm.markAllAsTouched();
      return;
    }

    const courseId = this.course()?.id;
    if (!courseId) return;

    this.errorMessage.set('');
    this.successMessage.set('');

    this.courseService.updateCourse(courseId, this.courseForm.value as UpdateCourseRequest).subscribe({
      next: (updatedCourse) => {
        this.course.set(updatedCourse);
        this.successMessage.set('Course updated successfully');
        this.isEditingCourse.set(false);
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to update course');
      }
    });
  }

  toggleAddChapter(): void {
    this.isAddingChapter.update(v => !v);
    if (!this.isAddingChapter()) {
      this.chapterForm.reset();
    }
  }

  addChapter(): void {
    if (this.chapterForm.invalid) {
      this.chapterForm.markAllAsTouched();
      return;
    }

    const courseId = this.course()?.id;
    if (!courseId) return;

    this.errorMessage.set('');

    this.chapterService.addChapter(courseId, this.chapterForm.value).subscribe({
      next: (chapter) => {
        this.chapters.update(chs => [...chs, chapter]);
        this.chapterForm.reset();
        this.isAddingChapter.set(false);
        this.successMessage.set('Chapter added successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to add chapter');
      }
    });
  }

  deleteChapter(chapterId: string): void {
    if (!confirm('Are you sure you want to delete this chapter? This will also delete all subchapters and blocks.')) {
      return;
    }

    this.chapterService.deleteChapter(chapterId).subscribe({
      next: () => {
        this.chapters.update(chs => chs.filter(ch => ch.id !== chapterId));
        this.successMessage.set('Chapter deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to delete chapter');
      }
    });
  }

  publishCourse(): void {
    const courseId = this.course()?.id;
    if (!courseId) return;

    if (confirm('Are you sure you want to publish this course? Published courses cannot be edited.')) {
      this.courseService.publishCourse(courseId).subscribe({
        next: () => {
          this.router.navigate(['/courses', courseId]);
        },
        error: (error: any) => {
          this.errorMessage.set(error.error?.message || 'Failed to publish course');
        }
      });
    }
  }
}
