import { ChangeDetectionStrategy, Component, inject, signal, OnInit, computed } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { SafeHtml, DomSanitizer } from '@angular/platform-browser';
import { ProgressService } from '../../core/services/progress.service';
import { BlockService } from '../../core/services/block.service';
import { MarkdownService } from '../../core/services/markdown.service';
import { QuizService, QuizSubmission, QuizResult } from '../../core/services/quiz.service';
import { SubmissionService } from '../../core/services/submission.service';
import { ProblemService } from '../../core/services/problem.service';
import { DialogService } from '../../core/services/dialog.service';
import { CourseProgress, BlockProgress } from '../../core/models/progress.model';
import { Block, BlockType } from '../../core/models/course.model';
import { Submission, SubmitCodeRequest } from '../../core/models/submission.model';
import { CodeEditorComponent } from '../../shared/components/code-editor/code-editor.component';
import { firstValueFrom } from 'rxjs';

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
  private readonly progressService = inject(ProgressService);
  private readonly blockService = inject(BlockService);
  private readonly markdownService = inject(MarkdownService);
  private readonly quizService = inject(QuizService);
  private readonly submissionService = inject(SubmissionService);
  private readonly problemService = inject(ProblemService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly dialogService = inject(DialogService);

  readonly courseProgress = signal<CourseProgress | null>(null);
  readonly currentBlock = signal<Block | null>(null);
  readonly isLoading = signal(false);
  readonly isSidebarOpen = signal(true);
  readonly errorMessage = signal<string>('');
  
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
  readonly isPolling = signal(false);

  // Computed helper for available languages (only from starter codes)
  readonly availableLanguages = computed(() => {
    const block = this.currentBlock();
    if (!block?.problem?.starterCodes) return [];
    return block.problem.starterCodes;
  });

  // Computed helper for unanswered quiz questions (only show if submit was attempted)
  readonly unansweredQuestions = computed(() => {
    const block = this.currentBlock();
    const attempted = this.attemptedQuizSubmit();
    
    if (!block?.quiz || !attempted) return new Set<string>();
    
    const unanswered = new Set<string>();
    block.quiz.questions.forEach(q => {
      const answers = this.quizAnswers().get(q.id);
      if (!answers || answers.size === 0) {
        unanswered.add(q.id);
      }
    });
    return unanswered;
  });

  // Computed helper for submission
  readonly passedTestCases = computed(() => {
    const submission = this.currentSubmission();
    return submission?.passedTestCases || 0;
  });

  readonly totalTestCases = computed(() => {
    const submission = this.currentSubmission();
    return submission?.totalTestCases || 0;
  });

  readonly getStatusName = (status: number): string => {
    switch (status) {
      case 0: return 'Pending';
      case 1: return 'Running';
      case 2: return 'Completed';
      case 3: return 'Compilation Error';
      case 4: return 'Runtime Error';
      case 5: return 'Time Limit Exceeded';
      case 6: return 'Memory Limit Exceeded';
      default: return 'Unknown';
    }
  };

  // Computed signals for navigation
  readonly allBlocks = computed(() => {
    const progress = this.courseProgress();
    if (!progress) return [];
    
    const blocks: Array<BlockProgress & { chapterIndex: number; subchapterIndex: number }> = [];
    progress.chapters.forEach((chapter) => {
      chapter.subchapters.forEach((subchapter) => {
        subchapter.blocks.forEach((block) => {
          blocks.push({
            ...block,
            chapterIndex: chapter.orderIndex,
            subchapterIndex: subchapter.orderIndex
          });
        });
      });
    });
    // Sortuj po hierarchii: chapter → subchapter → block
    return blocks.sort((a, b) => {
      if (a.chapterIndex !== b.chapterIndex) {
        return a.chapterIndex - b.chapterIndex;
      }
      if (a.subchapterIndex !== b.subchapterIndex) {
        return a.subchapterIndex - b.subchapterIndex;
      }
      return a.orderIndex - b.orderIndex;
    });
  });

  readonly currentBlockIndex = computed(() => {
    const current = this.currentBlock();
    const blocks = this.allBlocks();
    if (!current || blocks.length === 0) return -1;
    return blocks.findIndex(b => b.blockId === current.id);
  });

  readonly hasNextBlock = computed(() => {
    const index = this.currentBlockIndex();
    const blocks = this.allBlocks();
    return index >= 0 && index < blocks.length - 1;
  });

  readonly hasPreviousBlock = computed(() => {
    const index = this.currentBlockIndex();
    return index > 0;
  });

  readonly BlockType = BlockType;

  ngOnInit(): void {
    const courseId = this.route.snapshot.paramMap.get('courseId');
    if (courseId) {
      this.loadCourseProgress(courseId);
    }
  }

  loadCourseProgress(courseId: string): void {
    this.isLoading.set(true);
    this.progressService.getCourseProgress(courseId).subscribe({
      next: (progress) => {
        this.courseProgress.set(progress);
        
        // Load current block or first block
        if (progress.currentBlockId) {
          this.loadBlock(progress.currentBlockId);
        } else {
          const firstBlock = this.allBlocks()[0];
          if (firstBlock) {
            this.loadBlock(firstBlock.blockId);
          }
        }
        this.isLoading.set(false);
      },
      error: (error) => {
        this.errorMessage.set(error.error?.message || 'Failed to load course');
        this.isLoading.set(false);
      }
    });
  }

  loadBlock(blockId: string): void {
    // Clear error message when changing blocks
    this.errorMessage.set('');
    
    this.blockService.getBlock(blockId).subscribe({
      next: (block: Block) => {
        this.currentBlock.set(block);
        
        // Reset quiz state when loading new block
        this.quizAnswers.set(new Map());
        this.quizResult.set(null);
        this.attemptedQuizSubmit.set(false);
        
        // Reset problem state when loading new block
        this.selectedLanguage.set(null);
        this.userCode.set('');
        this.currentSubmission.set(null);
        this.previousSubmissions.set([]);
        
        // If it's a quiz block, try to load previous attempt
        if (block.quiz) {
          this.quizService.getQuizAttempt(block.quiz.id).subscribe({
            next: (result) => {
              // Show previous result
              this.quizResult.set(result);
            },
            error: () => {
              // No previous attempt, that's fine
            }
          });
        }
        
        // If it's a problem block, fetch full problem details with starterCodes
        if (block.problem) {
          this.problemService.getProblem(block.problem.id).subscribe({
            next: (problemDetails) => {
              // Update problem with full details including starterCodes and tags
              block.problem!.tags = problemDetails.tags;
              block.problem!.starterCodes = problemDetails.starterCodes.map(sc => ({
                id: sc.id,
                code: sc.code,
                languageId: sc.languageId,
                languageName: sc.languageName
              }));
              
              // Auto-select first language if available
              if (problemDetails.starterCodes && problemDetails.starterCodes.length > 0) {
                const firstStarter = problemDetails.starterCodes[0];
                this.selectedLanguage.set(firstStarter.languageId);
                this.userCode.set(firstStarter.code);
              }
              
              // Trigger change detection by updating the signal
              this.currentBlock.set({ ...block });
            },
            error: (err) => {
              console.error('Failed to load problem details:', err);
            }
          });

          // Load previous submissions for this problem
          this.submissionService.getByProblem(block.problem.id).subscribe({
            next: (submissions) => {
              this.previousSubmissions.set(submissions);
            },
            error: (err) => {
              console.error('Failed to load previous submissions:', err);
              this.previousSubmissions.set([]);
            }
          });
        }
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to load block');
      }
    });
  }

  async goToNextBlock(): Promise<void> {
    const blocks = this.allBlocks();
    const currentIndex = this.currentBlockIndex();
    const currentBlockData = this.currentBlock();
    
    if (currentIndex >= 0 && currentIndex < blocks.length - 1) {
      // Validate current block before moving to next
      if (currentBlockData) {
        // Check if quiz is completed
        if (currentBlockData.type === BlockType.Quiz && !this.quizResult()) {
          await this.dialogService.alert(
            'Please complete the quiz before moving to the next block.',
            'Quiz Not Completed',
            'warning'
          );
          return;
        }
        
        // Check if problem is solved
        if (currentBlockData.type === BlockType.Problem) {
          const submission = this.currentSubmission();
          if (!submission || submission.score !== 100) {
            await this.dialogService.alert(
              'Please solve the problem with 100% score before moving to the next block.',
              'Problem Not Solved',
              'warning'
            );
            return;
          }
        }
      }
      
      const nextBlock = blocks[currentIndex + 1];
      this.loadBlock(nextBlock.blockId);
      
      // Mark current block as complete only if requirements are met
      const currentBlockId = this.currentBlock()?.id;
      
      if (currentBlockId && currentBlockData) {
        let shouldMarkComplete = true;
        
        // Don't mark quiz blocks as complete if not solved
        if (currentBlockData.type === BlockType.Quiz && !this.quizResult()) {
          shouldMarkComplete = false;
        }
        
        // Don't mark problem blocks as complete if not solved with 100% score
        if (currentBlockData.type === BlockType.Problem) {
          const submission = this.currentSubmission();
          if (!submission || submission.score !== 100) {
            shouldMarkComplete = false;
          }
        }
        
        if (shouldMarkComplete) {
          this.markBlockComplete(currentBlockId);
        }
      }
    }
  }

  goToPreviousBlock(): void {
    const blocks = this.allBlocks();
    const currentIndex = this.currentBlockIndex();
    
    if (currentIndex > 0) {
      const previousBlock = blocks[currentIndex - 1];
      this.loadBlock(previousBlock.blockId);
    }
  }

  markBlockComplete(blockId: string): void {
    this.blockService.completeBlock(blockId).subscribe({
      next: () => {
        // Reload progress to update UI
        const courseId = this.route.snapshot.paramMap.get('courseId');
        if (courseId) {
          this.progressService.getCourseProgress(courseId).subscribe({
            next: (progress) => {
              this.courseProgress.set(progress);
            }
          });
        }
      },
      error: (error) => {
        console.error('Failed to mark block as complete:', error);
      }
    });
  }

  selectBlock(blockId: string): void {
    this.loadBlock(blockId);
  }

  toggleSidebar(): void {
    this.isSidebarOpen.update(v => !v);
  }

  async completeCourse(): Promise<void> {
    const currentBlock = this.currentBlock();
    
    if (!currentBlock) {
      this.router.navigate(['/dashboard']);
      return;
    }
    
    // Check if current block meets completion requirements
    let canComplete = true;
    
    // Check if block is already completed
    if (!this.isBlockCompleted(currentBlock.id)) {
      // Don't complete if quiz is not solved
      if (currentBlock.type === BlockType.Quiz && !this.quizResult()) {
        canComplete = false;
      }
      
      // Don't complete if problem doesn't have 100% score
      if (currentBlock.type === BlockType.Problem) {
        const submission = this.currentSubmission();
        if (!submission || submission.score !== 100) {
          canComplete = false;
        }
      }
      
      // If requirements are met, mark as complete
      if (canComplete) {
        this.markBlockComplete(currentBlock.id);
      }
    }
    
    // Only navigate to dashboard if all requirements are met OR block is already completed
    if (canComplete || this.isBlockCompleted(currentBlock.id)) {
      this.router.navigate(['/dashboard']);
    } else {
      // Show error message - block requirements not met
      if (currentBlock.type === BlockType.Quiz) {
        await this.dialogService.alert(
          'Please complete the quiz before finishing the course.',
          'Quiz Not Completed',
          'warning'
        );
      } else if (currentBlock.type === BlockType.Problem) {
        await this.dialogService.alert(
          'Please solve the problem with 100% score before finishing the course.',
          'Problem Not Solved',
          'warning'
        );
      }
    }
  }

  renderMarkdown(content: string): SafeHtml {
    return this.markdownService.renderMarkdownSync(content);
  }

  getSafeYouTubeUrl(url: string) {
    const videoId = this.extractYouTubeVideoId(url);
    const embedUrl = `https://www.youtube.com/embed/${videoId}`;
    return this.sanitizer.bypassSecurityTrustResourceUrl(embedUrl);
  }

  getYouTubeEmbedUrl(url: string): string {
    const videoId = this.extractYouTubeVideoId(url);
    return `https://www.youtube.com/embed/${videoId}`;
  }

  private extractYouTubeVideoId(url: string): string {
    const regExp = /^.*(youtu.be\/|v\/|u\/\w\/|embed\/|watch\?v=|&v=)([^#&?]*).*/;
    const match = url.match(regExp);
    return (match && match[2].length === 11) ? match[2] : '';
  }

  isBlockCompleted(blockId: string): boolean {
    const blocks = this.allBlocks();
    const block = blocks.find(b => b.blockId === blockId);
    return block?.isCompleted || false;
  }

  getBlockProgress(chapterId: string, subchapterId: string): { completed: number; total: number } {
    const progress = this.courseProgress();
    if (!progress) return { completed: 0, total: 0 };
    
    const chapter = progress.chapters.find(c => c.chapterId === chapterId);
    if (!chapter) return { completed: 0, total: 0 };
    
    const subchapter = chapter.subchapters.find(s => s.subchapterId === subchapterId);
    if (!subchapter) return { completed: 0, total: 0 };
    
    const completed = subchapter.blocks.filter(b => b.isCompleted).length;
    const total = subchapter.blocks.length;
    
    return { completed, total };
  }

  // Quiz methods
  selectQuizAnswer(questionId: string, answerId: string, isSingleChoice: boolean): void {
    const answers = this.quizAnswers();
    
    if (isSingleChoice) {
      // For single choice, replace any existing answer
      answers.set(questionId, new Set([answerId]));
    } else {
      // For multiple choice, toggle the answer
      const questionAnswers = answers.get(questionId) || new Set();
      if (questionAnswers.has(answerId)) {
        questionAnswers.delete(answerId);
      } else {
        questionAnswers.add(answerId);
      }
      answers.set(questionId, questionAnswers);
    }
    
    this.quizAnswers.set(new Map(answers));
  }

  isAnswerSelected(questionId: string, answerId: string): boolean {
    const answers = this.quizAnswers();
    return answers.get(questionId)?.has(answerId) || false;
  }

  async submitQuiz(): Promise<void> {
    const block = this.currentBlock();
    if (!block || !block.quiz) return;

    // Clear previous error
    this.errorMessage.set('');
    
    // Mark that submit was attempted
    this.attemptedQuizSubmit.set(true);

    // Validate that all questions have at least one answer
    const unansweredQuestions = block.quiz.questions.filter(q => {
      const answers = this.quizAnswers().get(q.id);
      return !answers || answers.size === 0;
    });

    if (unansweredQuestions.length > 0) {
      await this.dialogService.alert(
        `Please answer all questions before submitting. ${unansweredQuestions.length} question(s) remaining.`,
        'Incomplete Quiz',
        'warning'
      );
      return;
    }

    const submission: QuizSubmission = {
      answers: block.quiz.questions.map(q => ({
        questionId: q.id,
        selectedAnswerIds: Array.from(this.quizAnswers().get(q.id) || [])
      }))
    };

    this.isSubmittingQuiz.set(true);
    this.quizService.submitQuiz(block.quiz.id, submission).subscribe({
      next: (result) => {
        this.quizResult.set(result);
        this.isSubmittingQuiz.set(false);
        
        // Mark block as complete (backend already did this, but reload progress)
        const courseId = this.route.snapshot.paramMap.get('courseId');
        if (courseId) {
          this.progressService.getCourseProgress(courseId).subscribe({
            next: (progress) => {
              this.courseProgress.set(progress);
            }
          });
        }
      },
      error: (error) => {
        this.errorMessage.set(error.error?.message || 'Failed to submit quiz');
        this.isSubmittingQuiz.set(false);
      }
    });
  }

  // Problem block methods
  selectLanguage(languageId: string, starterCode: string): void {
    // Validate that this language is available for the problem
    const availableLangs = this.availableLanguages();
    const isAvailable = availableLangs.some(lang => lang.languageId === languageId);
    
    if (!isAvailable) {
      console.warn('Selected language is not available for this problem');
      return;
    }
    
    this.selectedLanguage.set(languageId);
    this.userCode.set(starterCode);
    this.currentSubmission.set(null);
  }

  updateCode(code: string): void {
    this.userCode.set(code);
  }

  async submitCode(): Promise<void> {
    const block = this.currentBlock();
    const languageId = this.selectedLanguage();
    
    if (!block || !block.problem || !languageId) return;

    // Clear previous error
    this.errorMessage.set('');

    // Final validation - ensure language is in starter codes
    const availableLangs = this.availableLanguages();
    const isLanguageSupported = availableLangs.some(lang => lang.languageId === languageId);
    
    if (!isLanguageSupported) {
      await this.dialogService.alert(
        'Selected language is not supported for this problem.',
        'Invalid Language',
        'error'
      );
      return;
    }

    const request: SubmitCodeRequest = {
      problemId: block.problem.id,
      languageId: languageId,
      code: this.userCode()
    };

    try {
      const submission = await firstValueFrom(
        this.submissionService.submit(request)
      );
      
      this.currentSubmission.set(submission);
      // Start polling
      await this.pollSubmission(submission.id);
    } catch (error: any) {
      this.errorMessage.set(error.error?.message || 'Failed to submit code');
    }
  }

  private async pollSubmission(submissionId: string): Promise<void> {
    this.isPolling.set(true);
    const maxAttempts = 60; // 1 minute (1s interval)

    for (let i = 0; i < maxAttempts; i++) {
      try {
        const sub = await firstValueFrom(
          this.submissionService.getById(submissionId)
        );

        this.currentSubmission.set(sub);

        // Check if submission is in final state (not Pending/Running)
        const finalStatuses = [2, 3, 4, 5, 6]; // Completed, CompilationError, RuntimeError, TimeLimitExceeded, MemoryLimitExceeded

        if (finalStatuses.includes(sub.status)) {
          this.isPolling.set(false);
          // Dodaj submission do previousSubmissions na początek listy
          const prev = this.previousSubmissions();
          this.previousSubmissions.set([sub, ...prev]);
          // If score is 100%, mark block as complete
          if (sub.status === 2 && sub.score === 100) {
            const blockId = this.currentBlock()?.id;
            if (blockId) {
              this.markBlockComplete(blockId);
            }
          }
          break;
        }

        await new Promise(resolve => setTimeout(resolve, 1000));
      } catch (error) {
        console.error('Polling error:', error);
        this.isPolling.set(false);
        break;
      }
    }

    this.isPolling.set(false);
  }

  getStatusClass(status: number): string {
    const baseClass = 'rounded-lg p-4 ';
    switch (status) {
      case 2: // Completed
        return baseClass + 'bg-green-50 border border-green-200';
      case 0: // Pending
      case 1: // Running
        return baseClass + 'bg-blue-50 border border-blue-200';
      case 3: // CompilationError
      case 4: // RuntimeError
      case 5: // TimeLimitExceeded
      case 6: // MemoryLimitExceeded
        return baseClass + 'bg-red-50 border border-red-200';
      default:
        return baseClass + 'bg-gray-50 border border-gray-200';
    }
  }

  getStatusBadgeClass(status: number): string {
    switch (status) {
      case 2: // Completed
        return 'bg-green-100 text-green-800';
      case 0: // Pending
      case 1: // Running
        return 'bg-blue-100 text-blue-800';
      case 3: // CompilationError
      case 4: // RuntimeError
        return 'bg-red-100 text-red-800';
      case 5: // TimeLimitExceeded
        return 'bg-orange-100 text-orange-800';
      case 6: // MemoryLimitExceeded
        return 'bg-purple-100 text-purple-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  }

  getLanguageName(languageId: string): 'python' | 'javascript' | 'csharp' | 'java' {
    // Map GUID to language name for Monaco Editor
    if (languageId === '11111111-1111-1111-1111-111111111111') return 'python';
    if (languageId === '22222222-2222-2222-2222-222222222222') return 'javascript';
    if (languageId === '33333333-3333-3333-3333-333333333333') return 'csharp';
    if (languageId === '44444444-4444-4444-4444-444444444444') return 'java';
    return 'python'; // default
  }

  viewSubmissionDetails(submissionId: string): void {
    this.submissionService.getById(submissionId).subscribe({
      next: (submission) => {
        // Set as current submission to display in the UI
        this.currentSubmission.set(submission);
        // Scroll to submission results
        setTimeout(() => {
          const element = document.querySelector('.submission-results');
          if (element) {
            element.scrollIntoView({ behavior: 'smooth', block: 'start' });
          }
        }, 100);
      },
      error: (err) => {
        console.error('Failed to load submission details:', err);
      }
    });
  }
}
