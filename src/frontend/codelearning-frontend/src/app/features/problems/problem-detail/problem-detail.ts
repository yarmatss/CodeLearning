import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { ProblemService, ProblemResponse } from '../../../core/services/problem.service';
import { AuthService } from '../../../core/services/auth.service';
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
  readonly authService = inject(AuthService);

  readonly problem = signal<ProblemResponse | null>(null);
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');

  problemId: string | null = null;

  ngOnInit(): void {
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
    const languages: { [key: string]: string } = {
      '11111111-1111-1111-1111-111111111111': 'Python',
      '22222222-2222-2222-2222-222222222222': 'JavaScript',
      '33333333-3333-3333-3333-333333333333': 'C#',
      '44444444-4444-4444-4444-444444444444': 'Java'
    };
    return languages[languageId] || 'Unknown';
  }

  getPublicTestCasesCount(): number {
    if (!this.problem()?.testCases) return 0;
    return this.problem()!.testCases.filter(tc => tc.isPublic).length;
  }
}
