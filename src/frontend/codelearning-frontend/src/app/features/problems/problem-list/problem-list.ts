import { ChangeDetectionStrategy, Component, inject, signal, OnInit, computed, OnDestroy } from '@angular/core';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { filter, Subscription } from 'rxjs';
import { ProblemService, ProblemResponse } from '../../../core/services/problem.service';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-problem-list',
  imports: [CommonModule, RouterLink, ReactiveFormsModule],
  templateUrl: './problem-list.html',
  styleUrl: './problem-list.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProblemList implements OnInit, OnDestroy {
  private readonly problemService = inject(ProblemService);
  readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly problems = signal<ProblemResponse[]>([]);
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');

  readonly searchControl = new FormControl('');
  readonly difficultyControl = new FormControl('');

  readonly isTeacher = computed(() => this.authService.currentUser()?.role === 'Teacher');
  
  private routerSubscription?: Subscription;

  ngOnInit(): void {
    this.loadProblems();
    
    // Reload list when navigating back to /problems (e.g., from problem editor)
    this.routerSubscription = this.router.events.pipe(
      filter(event => event instanceof NavigationEnd),
      filter((event: any) => event.url === '/problems')
    ).subscribe(() => {
      this.loadProblems();
    });
  }
  
  ngOnDestroy(): void {
    this.routerSubscription?.unsubscribe();
  }

  loadProblems(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    const search = this.searchControl.value || undefined;
    const difficulty = this.difficultyControl.value || undefined;

    this.problemService.getProblems(difficulty, undefined, search).subscribe({
      next: (problems) => {
        this.problems.set(problems);
        this.isLoading.set(false);
      },
      error: (error: any) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.detail || 'Failed to load problems');
      }
    });
  }

  onSearch(): void {
    this.loadProblems();
  }

  onDifficultyChange(): void {
    this.loadProblems();
  }

  clearFilters(): void {
    this.searchControl.setValue('');
    this.difficultyControl.setValue('');
    this.loadProblems();
  }

  editProblem(id: string): void {
    this.router.navigate(['/problems', id, 'edit']);
  }

  deleteProblem(id: string, title: string): void {
    if (!confirm(`Are you sure you want to delete "${title}"? This will also remove it from all courses.`)) {
      return;
    }

    this.problemService.deleteProblem(id).subscribe({
      next: () => {
        this.successMessage.set('Problem deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
        this.loadProblems();
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to delete problem');
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
