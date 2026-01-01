import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ChapterService } from '../../../core/services/chapter.service';
import { Chapter, Subchapter } from '../../../core/models/course.model';

@Component({
  selector: 'app-chapter-editor',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './chapter-editor.html',
  styleUrl: './chapter-editor.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ChapterEditor implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly chapterService = inject(ChapterService);

  readonly chapter = signal<Chapter | null>(null);
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
  }

  ngOnInit(): void {
    this.courseId = this.route.snapshot.paramMap.get('courseId') ?? '';
    this.chapterId = this.route.snapshot.paramMap.get('chapterId') ?? '';
    
    if (this.chapterId) {
      this.loadSubchapters();
    }
  }

  loadSubchapters(): void {
    this.isLoading.set(true);
    this.chapterService.getChapterSubchapters(this.chapterId).subscribe({
      next: (subchapters) => {
        this.subchapters.set(subchapters);
        // Set chapter info from first subchapter if available
        if (subchapters.length > 0) {
          // We'll need to get chapter details separately or extract from route
        }
        this.isLoading.set(false);
      },
      error: (error: any) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to load subchapters');
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
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to add subchapter');
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
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to reorder subchapter');
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
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to reorder subchapter');
      }
    });
  }

  deleteSubchapter(subchapterId: string): void {
    if (!confirm('Are you sure you want to delete this subchapter? This will also delete all blocks.')) {
      return;
    }

    this.chapterService.deleteSubchapter(this.chapterId, subchapterId).subscribe({
      next: () => {
        this.loadSubchapters();
        this.successMessage.set('Subchapter deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to delete subchapter');
      }
    });
  }
}
