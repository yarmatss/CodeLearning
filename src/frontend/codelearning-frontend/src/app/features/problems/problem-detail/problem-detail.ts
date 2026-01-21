import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { ProblemService, ProblemResponse } from '../../../core/services/problem.service';
import { AuthService } from '../../../core/services/auth.service';
import { LanguageService } from '../../../core/services/language.service';
import { MarkdownPipe } from '../../../shared/pipes/markdown.pipe';

@Component({
  selector: 'app-problem-detail',
  imports: [CommonModule, RouterLink, MarkdownPipe],
  templateUrl: './problem-detail.html',
  styleUrl: './problem-detail.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProblemDetail implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly problemService = inject(ProblemService);
  private readonly languageService = inject(LanguageService);
  readonly authService = inject(AuthService);

  readonly problem = signal<ProblemResponse | null>(null);
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');

  problemId: string | null = null;

  ngOnInit(): void {
    // Load languages first
    this.languageService.getLanguages().subscribe();
    
    this.problemId = this.route.snapshot.paramMap.get('id');
    if (this.problemId) {
      this.loadProblem();
    }
  }

  loadProblem(): void {
    if (!this.problemId) return;

    this.isLoading.set(true);
    this.problemService.getProblem(this.problemId).subscribe({
      next: (problem) => {
        this.problem.set(problem);
        this.isLoading.set(false);
      },
      error: (error: any) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.detail || 'Failed to load problem');
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

  getLanguageName(languageId: string): string {
    return this.languageService.getLanguageName(languageId);
  }

  getPublicTestCasesCount(): number {
    if (!this.problem()?.testCases) return 0;
    return this.problem()!.testCases.filter(tc => tc.isPublic).length;
  }
}
