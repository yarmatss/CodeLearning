import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
  OnInit
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import {
  EMPTY,
  Subject,
  catchError,
  exhaustMap,
  finalize,
  switchMap,
  take,
  takeUntil,
  takeWhile,
  tap,
  timer
} from 'rxjs';
import { ProgressService } from '../../core/services/progress.service';
import { BlockService } from '../../core/services/block.service';
import { MarkdownService } from '../../core/services/markdown.service';
import { QuizService, QuizSubmission, QuizResult } from '../../core/services/quiz.service';
import { SubmissionService } from '../../core/services/submission.service';
import { ProblemService } from '../../core/services/problem.service';
import { DialogService } from '../../core/services/dialog.service';
import { CourseProgress, BlockProgress } from '../../core/models/progress.model';
import { Block, BlockType } from '../../core/models/course.model';
import {
  FINAL_SUBMISSION_STATUSES,
  Submission,
  SubmissionStatus,
  SubmitCodeRequest,
  TestResultStatus,
  isAcceptedSubmission
} from '../../core/models/submission.model';
import { CodeEditorComponent } from '../../shared/components/code-editor/code-editor.component';
import { EditorLanguage } from '../../shared/components/code-editor/monaco-loader';
import { extractApiError, isAccessDenied } from '../../core/utils/api-error';

// A submission is polled once a second for at most this many times before the page gives up
const POLL_INTERVAL_MS = 1000;
const MAX_POLL_ATTEMPTS = 90;

// YouTube video ids are 11 URL-safe base64 characters
const YOUTUBE_VIDEO_ID = /^[A-Za-z0-9_-]{11}$/;

type OrderedBlock = BlockProgress & { chapterIndex: number; subchapterIndex: number };

@Component({
  selector: 'app-learn',
  imports: [CommonModule, RouterLink, CodeEditorComponent],
  templateUrl: './learn.html',
  styleUrl: './learn.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Learn implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly progressService = inject(ProgressService);
  private readonly blockService = inject(BlockService);
  private readonly markdownService = inject(MarkdownService);
  private readonly quizService = inject(QuizService);
  private readonly submissionService = inject(SubmissionService);
  private readonly problemService = inject(ProblemService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly dialogService = inject(DialogService);

  // Every block change goes through this subject: loading switches to the newest id, and
  // everything started for the previous block (problem details, polling) stops when it emits
  private readonly blockId$ = new Subject<string>();

  courseId = '';

  readonly courseProgress = signal<CourseProgress | null>(null);
  readonly currentBlock = signal<Block | null>(null);
  readonly isLoading = signal(false);
  readonly isBlockLoading = signal(false);
  readonly isSidebarOpen = signal(true);
  // Shown instead of the block when the course or the block cannot be loaded
  readonly errorMessage = signal<string>('');
  // Set when the API refuses the content (401/403: not enrolled, or the course is unpublished);
  // the page then offers the course page, where the student can enroll, instead of an error
  readonly accessDeniedMessage = signal<string>('');
  // Shown above the block for a failed action (submit, quiz), keeping the editor and its code
  readonly actionError = signal<string>('');
  readonly infoMessage = signal<string>('');

  // Quiz state
  readonly quizAnswers = signal<Map<string, Set<string>>>(new Map());
  readonly quizResult = signal<QuizResult | null>(null);
  readonly isSubmittingQuiz = signal(false);
  readonly attemptedQuizSubmit = signal(false);

  // Problem state
  readonly selectedLanguage = signal<string | null>(null);
  readonly userCode = signal<string>('');
  readonly currentSubmission = signal<Submission | null>(null);
  readonly previousSubmissions = signal<Submission[]>([]);
  readonly isSubmitting = signal(false);
  readonly isPolling = signal(false);

  readonly BlockType = BlockType;
  readonly TestResultStatus = TestResultStatus;
  readonly SubmissionStatus = SubmissionStatus;

  // Rendered once per block instead of on every change detection pass
  readonly theoryHtml = computed(() => this.markdownService.render(this.currentBlock()?.theoryContent?.content));
  readonly problemDescriptionHtml = computed(() => this.markdownService.render(this.currentBlock()?.problem?.description));

  // Built from the id the backend extracted and validated, never from the raw URL
  readonly videoEmbedUrl = computed<SafeResourceUrl | null>(() => {
    const videoId = this.currentBlock()?.videoContent?.videoId;
    if (!videoId || !YOUTUBE_VIDEO_ID.test(videoId)) {
      return null;
    }
    return this.sanitizer.bypassSecurityTrustResourceUrl(`https://www.youtube-nocookie.com/embed/${videoId}`);
  });

  readonly availableLanguages = computed(() => this.currentBlock()?.problem?.starterCodes ?? []);

  readonly editorLanguage = computed<EditorLanguage>(() => {
    const languageId = this.selectedLanguage();
    const starter = this.availableLanguages().find(sc => sc.languageId === languageId);
    return toEditorLanguage(starter?.languageName);
  });

  // Unanswered quiz questions (only shown once a submit was attempted)
  readonly unansweredQuestions = computed(() => {
    const block = this.currentBlock();
    if (!block?.quiz || !this.attemptedQuizSubmit()) return new Set<string>();

    const answers = this.quizAnswers();
    return new Set(block.quiz.questions.filter(q => !answers.get(q.id)?.size).map(q => q.id));
  });

  readonly passedTestCases = computed(() => this.currentSubmission()?.passedTestCases || 0);
  readonly totalTestCases = computed(() => this.currentSubmission()?.totalTestCases || 0);

  readonly allBlocks = computed<OrderedBlock[]>(() => {
    const progress = this.courseProgress();
    if (!progress) return [];

    const blocks: OrderedBlock[] = [];
    for (const chapter of progress.chapters) {
      for (const subchapter of chapter.subchapters) {
        for (const block of subchapter.blocks) {
          blocks.push({ ...block, chapterIndex: chapter.orderIndex, subchapterIndex: subchapter.orderIndex });
        }
      }
    }
    // Course order: chapter, then subchapter, then block
    return blocks.sort((a, b) =>
      a.chapterIndex - b.chapterIndex || a.subchapterIndex - b.subchapterIndex || a.orderIndex - b.orderIndex
    );
  });

  readonly currentBlockIndex = computed(() => {
    const current = this.currentBlock();
    if (!current) return -1;
    return this.allBlocks().findIndex(b => b.blockId === current.id);
  });

  readonly hasNextBlock = computed(() => {
    const index = this.currentBlockIndex();
    return index >= 0 && index < this.allBlocks().length - 1;
  });

  readonly hasPreviousBlock = computed(() => this.currentBlockIndex() > 0);

  /** Whether the student has met the current block's requirement and may move on. */
  readonly isCurrentBlockSatisfied = computed(() => {
    const block = this.currentBlock();
    if (!block) return false;
    if (this.isBlockCompleted(block.id)) return true;

    switch (block.type) {
      case BlockType.Quiz:
        return !!this.quizResult();
      case BlockType.Problem:
        // A problem solved earlier counts even if its block was never marked complete
        return this.previousSubmissions().some(isAcceptedSubmission)
          || (!!this.currentSubmission() && isAcceptedSubmission(this.currentSubmission()!));
      default:
        return true;
    }
  });

  constructor() {
    this.blockId$
      .pipe(
        tap(() => this.resetBlockState()),
        switchMap(blockId =>
          this.blockService.getBlock(blockId).pipe(
            catchError((error: unknown) => {
              this.showLoadError(error, 'Failed to load block');
              return EMPTY;
            }),
            finalize(() => this.isBlockLoading.set(false))
          )
        ),
        takeUntilDestroyed()
      )
      .subscribe(block => this.showBlock(block));

    // Leaving the page by any route (exit link, navbar, back button) completes the theory or
    // video block being read
    this.destroyRef.onDestroy(() => this.completePassiveBlock(this.currentBlock()));
  }

  ngOnInit(): void {
    this.courseId = this.route.snapshot.paramMap.get('courseId') ?? '';
    if (this.courseId) {
      this.loadCourseProgress();
    }
  }

  loadCourseProgress(): void {
    this.isLoading.set(true);
    this.progressService.getCourseProgress(this.courseId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (progress) => {
          this.courseProgress.set(progress);
          this.isLoading.set(false);

          const startBlockId = progress.currentBlockId ?? this.allBlocks()[0]?.blockId;
          if (startBlockId) {
            this.loadBlock(startBlockId);
          }
        },
        error: (error: unknown) => {
          this.showLoadError(error, 'Failed to load course');
          this.isLoading.set(false);
        }
      });
  }

  loadBlock(blockId: string): void {
    const leaving = this.currentBlock();
    if (leaving?.id !== blockId) {
      this.completePassiveBlock(leaving);
    }
    this.blockId$.next(blockId);
  }

  async goToNextBlock(): Promise<void> {
    if (!this.hasNextBlock()) return;
    if (!(await this.ensureCurrentBlockSatisfied('moving to the next block'))) return;

    const nextBlock = this.allBlocks()[this.currentBlockIndex() + 1];
    this.loadBlock(nextBlock.blockId);
  }

  goToPreviousBlock(): void {
    if (this.hasPreviousBlock()) {
      this.loadBlock(this.allBlocks()[this.currentBlockIndex() - 1].blockId);
    }
  }

  selectBlock(blockId: string): void {
    this.loadBlock(blockId);
  }

  toggleSidebar(): void {
    this.isSidebarOpen.update(v => !v);
  }

  async completeCourse(): Promise<void> {
    const block = this.currentBlock();
    if (!block) {
      this.router.navigate(['/dashboard']);
      return;
    }

    if (!(await this.ensureCurrentBlockSatisfied('finishing the course'))) return;

    // Theory and video blocks are completed by the destroy hook as the page is left
    this.router.navigate(['/dashboard']);
  }

  isBlockCompleted(blockId: string): boolean {
    return this.allBlocks().find(b => b.blockId === blockId)?.isCompleted ?? false;
  }

  getBlockProgress(chapterId: string, subchapterId: string): { completed: number; total: number } {
    const subchapter = this.courseProgress()
      ?.chapters.find(c => c.chapterId === chapterId)
      ?.subchapters.find(s => s.subchapterId === subchapterId);
    if (!subchapter) return { completed: 0, total: 0 };

    return {
      completed: subchapter.blocks.filter(b => b.isCompleted).length,
      total: subchapter.blocks.length
    };
  }

  // Quiz methods
  selectQuizAnswer(questionId: string, answerId: string, isSingleChoice: boolean): void {
    const answers = new Map(this.quizAnswers());

    if (isSingleChoice) {
      answers.set(questionId, new Set([answerId]));
    } else {
      const questionAnswers = new Set(answers.get(questionId) ?? []);
      if (questionAnswers.has(answerId)) {
        questionAnswers.delete(answerId);
      } else {
        questionAnswers.add(answerId);
      }
      answers.set(questionId, questionAnswers);
    }

    this.quizAnswers.set(answers);
  }

  isAnswerSelected(questionId: string, answerId: string): boolean {
    return this.quizAnswers().get(questionId)?.has(answerId) || false;
  }

  async submitQuiz(): Promise<void> {
    const block = this.currentBlock();
    if (!block?.quiz || this.isSubmittingQuiz()) return;

    this.actionError.set('');
    this.attemptedQuizSubmit.set(true);

    const unanswered = this.unansweredQuestions().size;
    if (unanswered > 0) {
      await this.dialogService.alert(
        `Please answer all questions before submitting. ${unanswered} question(s) remaining.`,
        'Incomplete Quiz',
        'warning'
      );
      return;
    }

    const submission: QuizSubmission = {
      answers: block.quiz.questions.map(q => ({
        questionId: q.id,
        selectedAnswerIds: Array.from(this.quizAnswers().get(q.id) ?? [])
      }))
    };

    const blockId = block.id;
    this.isSubmittingQuiz.set(true);
    this.quizService.submitQuiz(block.quiz.id, submission)
      .pipe(
        takeUntil(this.blockId$),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.isSubmittingQuiz.set(false))
      )
      .subscribe({
        next: (result) => {
          if (this.currentBlock()?.id !== blockId) return;
          this.quizResult.set(result);
          // The backend completes the quiz block on submit; refresh the sidebar to show it
          this.reloadProgress();
        },
        error: (error: unknown) => {
          this.actionError.set(extractApiError(error, 'Failed to submit quiz'));
        }
      });
  }

  // Problem block methods
  selectLanguage(languageId: string, starterCode: string): void {
    if (!this.availableLanguages().some(lang => lang.languageId === languageId)) {
      return;
    }

    this.selectedLanguage.set(languageId);
    this.userCode.set(starterCode);
    this.currentSubmission.set(null);
  }

  updateCode(code: string): void {
    this.userCode.set(code);
  }

  submitCode(): void {
    const block = this.currentBlock();
    const languageId = this.selectedLanguage();
    if (!block?.problem || !languageId || this.isSubmitting()) return;

    this.actionError.set('');
    this.infoMessage.set('');

    if (!this.availableLanguages().some(lang => lang.languageId === languageId)) {
      this.actionError.set('Selected language is not supported for this problem.');
      return;
    }

    // Captured now: the student may switch blocks while the submission runs
    const blockId = block.id;
    const problemId = block.problem.id;
    const request: SubmitCodeRequest = { problemId, languageId, code: this.userCode() };

    this.isSubmitting.set(true);
    this.submissionService.submit(request)
      .pipe(takeUntil(this.blockId$), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (submission) => {
          this.currentSubmission.set(submission);
          this.pollSubmission(submission.id, blockId, problemId);
        },
        error: (error: unknown) => {
          this.isSubmitting.set(false);
          // Rate limiting replies with ProblemDetails, which carries the reason in `detail`
          this.actionError.set(extractApiError(error, 'Failed to submit code'));
        }
      });
  }

  viewSubmissionDetails(submissionId: string): void {
    const blockId = this.currentBlock()?.id;
    this.submissionService.getById(submissionId)
      .pipe(takeUntil(this.blockId$), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (submission) => {
          if (this.currentBlock()?.id !== blockId) return;
          this.currentSubmission.set(submission);
          setTimeout(() => {
            document.querySelector('.submission-results')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
          }, 100);
        },
        error: (error: unknown) => {
          this.actionError.set(extractApiError(error, 'Failed to load submission details'));
        }
      });
  }

  getStatusName(status: number): string {
    switch (status) {
      case SubmissionStatus.Pending: return 'Pending';
      case SubmissionStatus.Running: return 'Running';
      case SubmissionStatus.Completed: return 'Completed';
      case SubmissionStatus.CompilationError: return 'Compilation Error';
      case SubmissionStatus.RuntimeError: return 'Runtime Error';
      case SubmissionStatus.TimeLimitExceeded: return 'Time Limit Exceeded';
      case SubmissionStatus.MemoryLimitExceeded: return 'Memory Limit Exceeded';
      default: return 'Unknown';
    }
  }

  getStatusClass(status: number): string {
    const baseClass = 'rounded-lg p-4 ';
    switch (status) {
      case SubmissionStatus.Completed:
        return baseClass + 'bg-green-50 border border-green-200';
      case SubmissionStatus.Pending:
      case SubmissionStatus.Running:
        return baseClass + 'bg-blue-50 border border-blue-200';
      case SubmissionStatus.CompilationError:
      case SubmissionStatus.RuntimeError:
      case SubmissionStatus.TimeLimitExceeded:
      case SubmissionStatus.MemoryLimitExceeded:
        return baseClass + 'bg-red-50 border border-red-200';
      default:
        return baseClass + 'bg-gray-50 border border-gray-200';
    }
  }

  getStatusBadgeClass(status: number): string {
    switch (status) {
      case SubmissionStatus.Completed:
        return 'bg-green-100 text-green-800';
      case SubmissionStatus.Pending:
      case SubmissionStatus.Running:
        return 'bg-blue-100 text-blue-800';
      case SubmissionStatus.CompilationError:
      case SubmissionStatus.RuntimeError:
        return 'bg-red-100 text-red-800';
      case SubmissionStatus.TimeLimitExceeded:
        return 'bg-orange-100 text-orange-800';
      case SubmissionStatus.MemoryLimitExceeded:
        return 'bg-purple-100 text-purple-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  }

  private showLoadError(error: unknown, fallback: string): void {
    if (isAccessDenied(error)) {
      this.accessDeniedMessage.set(extractApiError(error, 'Enroll in this course to view its content'));
      return;
    }
    this.errorMessage.set(extractApiError(error, fallback));
  }

  private resetBlockState(): void {
    this.errorMessage.set('');
    this.accessDeniedMessage.set('');
    this.actionError.set('');
    this.infoMessage.set('');
    this.isBlockLoading.set(true);

    this.quizAnswers.set(new Map());
    this.quizResult.set(null);
    this.attemptedQuizSubmit.set(false);
    this.isSubmittingQuiz.set(false);

    this.selectedLanguage.set(null);
    this.userCode.set('');
    this.currentSubmission.set(null);
    this.previousSubmissions.set([]);
    this.isSubmitting.set(false);
    this.isPolling.set(false);
  }

  private showBlock(block: Block): void {
    this.currentBlock.set(block);
    const stillCurrent = () => this.currentBlock()?.id === block.id;

    if (block.quiz) {
      this.quizService.getQuizAttempt(block.quiz.id)
        .pipe(takeUntil(this.blockId$), takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (result) => {
            if (stillCurrent()) this.quizResult.set(result);
          },
          // No previous attempt: the quiz is simply shown unanswered
          error: () => undefined
        });
    }

    if (block.problem) {
      const problemId = block.problem.id;

      this.problemService.getProblem(problemId)
        .pipe(takeUntil(this.blockId$), takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (details) => {
            if (!stillCurrent()) return;
            const starterCodes = details.starterCodes.map(sc => ({
              id: sc.id,
              code: sc.code,
              languageId: sc.languageId,
              languageName: sc.languageName
            }));
            this.currentBlock.set({
              ...block,
              problem: { ...block.problem!, tags: details.tags, starterCodes }
            });

            const first = starterCodes[0];
            if (first) {
              this.selectedLanguage.set(first.languageId);
              this.userCode.set(first.code);
            }
          },
          error: (error: unknown) => {
            if (stillCurrent()) this.actionError.set(extractApiError(error, 'Failed to load problem details'));
          }
        });

      this.submissionService.getByProblem(problemId)
        .pipe(takeUntil(this.blockId$), takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (submissions) => {
            if (!stillCurrent()) return;
            this.previousSubmissions.set(submissions);
            // Solved before (e.g. on another device, or the page was left mid-poll) but never
            // marked complete: complete it now so "Next" and the progress bar agree
            if (!this.isBlockCompleted(block.id) && submissions.some(isAcceptedSubmission)) {
              this.markBlockComplete(block.id);
            }
          },
          error: () => this.previousSubmissions.set([])
        });
    }
  }

  private pollSubmission(submissionId: string, blockId: string, problemId: string): void {
    this.isPolling.set(true);
    let finished = false;

    timer(0, POLL_INTERVAL_MS)
      .pipe(
        take(MAX_POLL_ATTEMPTS),
        // exhaustMap: a slow status request is not cancelled by the next tick
        exhaustMap(() => this.submissionService.getById(submissionId)),
        takeWhile(submission => !FINAL_SUBMISSION_STATUSES.includes(submission.status), true),
        takeUntil(this.blockId$),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.isPolling.set(false);
          this.isSubmitting.set(false);
        })
      )
      .subscribe({
        next: (submission) => {
          if (this.currentBlock()?.id !== blockId || submission.problemId !== problemId) return;
          this.currentSubmission.set(submission);

          if (FINAL_SUBMISSION_STATUSES.includes(submission.status)) {
            finished = true;
            this.previousSubmissions.update(prev => [submission, ...prev.filter(s => s.id !== submission.id)]);
            if (isAcceptedSubmission(submission)) {
              // The runner already completed the block on the server; the explicit call is a
              // harmless duplicate, and either way the sidebar is refreshed to show the tick
              this.markBlockComplete(blockId);
            }
          }
        },
        error: (error: unknown) => {
          if (this.currentBlock()?.id === blockId) {
            this.actionError.set(extractApiError(error, 'Lost track of your submission. Check Previous Submissions for its result.'));
          }
        },
        complete: () => {
          if (!finished && this.currentBlock()?.id === blockId) {
            this.infoMessage.set('Your submission is taking longer than usual. Its result will appear under Previous Submissions; reopen this block later to see it.');
          }
        }
      });
  }

  private async ensureCurrentBlockSatisfied(action: string): Promise<boolean> {
    const block = this.currentBlock();
    if (!block || this.isCurrentBlockSatisfied()) {
      // A problem solved earlier whose block is not yet marked complete gets completed now
      if (block?.type === BlockType.Problem && !this.isBlockCompleted(block.id)) {
        this.markBlockComplete(block.id);
      }
      return true;
    }

    if (block.type === BlockType.Quiz) {
      await this.dialogService.alert(`Please complete the quiz before ${action}.`, 'Quiz Not Completed', 'warning');
    } else if (block.type === BlockType.Problem) {
      await this.dialogService.alert(
        `Please solve the problem with a 100% score before ${action}.`,
        'Problem Not Solved',
        'warning'
      );
    }
    return false;
  }

  /** Theory and video blocks have no task: viewing them, then leaving, completes them. */
  private completePassiveBlock(block: Block | null): void {
    if (!block || this.isBlockCompleted(block.id)) return;
    if (block.type === BlockType.Theory || block.type === BlockType.Video) {
      this.markBlockComplete(block.id);
    }
  }

  private markBlockComplete(blockId: string): void {
    // Not tied to this component's lifetime: completing on the way out must still reach the server
    // The progress is reloaded even when the call fails: for a solved problem the server may have
    // completed the block already, and the sidebar should show what the server says
    this.blockService.completeBlock(blockId).subscribe({
      next: () => this.reloadProgress(),
      error: () => this.reloadProgress()
    });
  }

  private reloadProgress(): void {
    if (!this.courseId) return;
    this.progressService.getCourseProgress(this.courseId).subscribe({
      next: (progress) => this.courseProgress.set(progress),
      error: () => undefined
    });
  }
}

function toEditorLanguage(languageName: string | undefined): EditorLanguage {
  const name = (languageName ?? '').toLowerCase().replace(/\s+/g, '');
  if (name.includes('c#') || name.includes('csharp')) return 'csharp';
  if (name.includes('javascript') || name === 'js' || name.startsWith('node')) return 'javascript';
  if (name.includes('java')) return 'java';
  return 'python';
}
