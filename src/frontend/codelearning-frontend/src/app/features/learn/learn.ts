import { ChangeDetectionStrategy, Component, inject, signal, OnInit, computed } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { SafeHtml, DomSanitizer } from '@angular/platform-browser';
import { ProgressService } from '../../core/services/progress.service';
import { BlockService } from '../../core/services/block.service';
import { MarkdownService } from '../../core/services/markdown.service';
import { QuizService, QuizSubmission, QuizResult } from '../../core/services/quiz.service';
import { CourseProgress, BlockProgress } from '../../core/models/progress.model';
import { Block, BlockType } from '../../core/models/course.model';

@Component({
  selector: 'app-learn',
  imports: [CommonModule, RouterLink],
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
  private readonly sanitizer = inject(DomSanitizer);

  readonly courseProgress = signal<CourseProgress | null>(null);
  readonly currentBlock = signal<Block | null>(null);
  readonly isLoading = signal(false);
  readonly isSidebarOpen = signal(true);
  readonly errorMessage = signal<string>('');
  
  // Quiz state
  readonly quizAnswers = signal<Map<string, Set<string>>>(new Map());
  readonly quizResult = signal<QuizResult | null>(null);
  readonly isSubmittingQuiz = signal(false);

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
    this.blockService.getBlock(blockId).subscribe({
      next: (block: Block) => {
        this.currentBlock.set(block);
        // Reset quiz state when loading new block
        this.quizAnswers.set(new Map());
        this.quizResult.set(null);
        
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
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to load block');
      }
    });
  }

  goToNextBlock(): void {
    const blocks = this.allBlocks();
    const currentIndex = this.currentBlockIndex();
    
    if (currentIndex >= 0 && currentIndex < blocks.length - 1) {
      const nextBlock = blocks[currentIndex + 1];
      this.loadBlock(nextBlock.blockId);
      
      // Mark current block as complete (except for unsolved quizzes)
      const currentBlockId = this.currentBlock()?.id;
      const currentBlockData = this.currentBlock();
      
      if (currentBlockId && currentBlockData) {
        // Don't mark quiz blocks as complete if not solved
        const isUnsolvedQuiz = currentBlockData.type === BlockType.Quiz && !this.quizResult();
        
        if (!isUnsolvedQuiz) {
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

  completeCourse(): void {
    // Mark the last block as complete if not already
    const currentBlock = this.currentBlock();
    if (currentBlock && !this.isBlockCompleted(currentBlock.id)) {
      this.markBlockComplete(currentBlock.id);
    }
    
    // Navigate back to dashboard with success message
    this.router.navigate(['/dashboard']);
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

  submitQuiz(): void {
    const block = this.currentBlock();
    if (!block || !block.quiz) return;

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
}
