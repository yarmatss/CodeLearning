import { ChangeDetectionStrategy, Component, inject, signal, OnInit, computed, OnDestroy } from '@angular/core';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { filter, Subscription } from 'rxjs';
import {
  ProblemService,
  ProblemDifficulty,
  ProblemListItem,
  TagResponse
} from '../../../core/services/problem.service';
import { AuthService } from '../../../core/services/auth.service';
import { DialogService } from '../../../core/services/dialog.service';
import { extractApiError } from '../../../core/utils/api-error';
import { PaginationComponent } from '../../../shared/components/pagination/pagination.component';

const PROBLEMS_PAGE_SIZE = 20;

@Component({
  selector: 'app-problem-list',
  imports: [CommonModule, RouterLink, ReactiveFormsModule, PaginationComponent],
  templateUrl: './problem-list.html',
  styleUrl: './problem-list.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProblemList implements OnInit, OnDestroy {
  private readonly problemService = inject(ProblemService);
  readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly dialogService = inject(DialogService);

  readonly problems = signal<ProblemListItem[]>([]);
  readonly availableTags = signal<TagResponse[]>([]);
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');

  readonly page = signal(1);
  readonly totalPages = signal(0);
  readonly totalCount = signal(0);

  readonly searchControl = new FormControl('');
  readonly difficultyControl = new FormControl<ProblemDifficulty | ''>('');
  readonly tagControl = new FormControl('');

  readonly isTeacher = computed(() => this.authService.currentUser()?.role === 'Teacher');

  private routerSubscription?: Subscription;

  ngOnInit(): void {
    this.loadTags();
    this.loadProblems();

    // Reload list when navigating back to /problems (e.g., from problem editor)
    this.routerSubscription = this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      filter(event => event.url === '/problems')
    ).subscribe(() => {
      this.loadProblems(this.page());
    });
  }

  ngOnDestroy(): void {
    this.routerSubscription?.unsubscribe();
  }

  loadTags(): void {
    this.problemService.getTags().subscribe({
      next: (tags) => this.availableTags.set(tags),
      error: () => this.availableTags.set([])
    });
  }

  /** Only the author may change a problem. */
  canEditProblem(problem: ProblemListItem): boolean {
    return problem.authorId === this.authService.currentUser()?.userId;
  }

  loadProblems(page = 1): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.problemService.getProblems({
      page,
      pageSize: PROBLEMS_PAGE_SIZE,
      search: this.searchControl.value?.trim() || undefined,
      difficulty: this.difficultyControl.value || undefined,
      tagId: this.tagControl.value || undefined
    }).subscribe({
      next: (result) => {
        this.problems.set(result.items);
        this.page.set(result.page);
        this.totalPages.set(result.totalPages);
        this.totalCount.set(result.totalCount);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.isLoading.set(false);
        this.errorMessage.set(extractApiError(error, 'Failed to load problems'));
      }
    });
  }

  // Changing a filter starts again from the first page
  onSearch(): void {
    this.loadProblems();
  }

  onDifficultyChange(): void {
    this.loadProblems();
  }

  onTagChange(): void {
    this.loadProblems();
  }

  onPageChange(page: number): void {
    this.loadProblems(page);
  }

  clearFilters(): void {
    this.searchControl.setValue('');
    this.difficultyControl.setValue('');
    this.tagControl.setValue('');
    this.loadProblems();
  }

  editProblem(id: string): void {
    this.router.navigate(['/problems', id, 'edit']);
  }

  async deleteProblem(id: string, title: string): Promise<void> {
    const { confirmed } = await this.dialogService.confirm({
      title: 'Delete problem?',
      message: `Are you sure you want to delete "${title}"? This cannot be undone.`,
      confirmText: 'Delete',
      cancelText: 'Cancel',
      type: 'warning'
    });
    if (!confirmed) {
      return;
    }

    this.problemService.deleteProblem(id).subscribe({
      next: () => {
        this.successMessage.set('Problem deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
        // Stay on this page unless its last row was just removed
        const page = this.problems().length === 1 && this.page() > 1 ? this.page() - 1 : this.page();
        this.loadProblems(page);
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to delete problem'));
      }
    });
  }

  getDifficultyClass(difficulty: string): string {
    switch (difficulty) {
      case 'Easy':
        return 'bg-green-100 text-green-800';
      case 'Medium':
        return 'bg-yellow-100 text-yellow-800';
      case 'Hard':
        return 'bg-red-100 text-red-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  }
}
