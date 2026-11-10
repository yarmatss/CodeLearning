import { ChangeDetectionStrategy, Component, computed, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, FormGroup, FormArray, ReactiveFormsModule, Validators } from '@angular/forms';
import { CommonModule } from '@angular/common';
import {
  BlockService,
  CreateTheoryBlockRequest,
  CreateVideoBlockRequest,
  CreateProblemBlockRequest,
  CreateQuizBlockRequest
} from '../../../core/services/block.service';
import { ProblemService, ProblemListItem } from '../../../core/services/problem.service';
import { ChapterService } from '../../../core/services/chapter.service';
import { CourseService } from '../../../core/services/course.service';
import { Block, BlockType, Course, CourseStatus, Subchapter } from '../../../core/models/course.model';
import { MAX_PAGE_SIZE } from '../../../core/models/paging.model';
import { DialogService } from '../../../core/services/dialog.service';
import { extractApiError } from '../../../core/utils/api-error';
import { CourseLockBannerComponent } from '../../../shared/components/course-lock-banner/course-lock-banner.component';

@Component({
  selector: 'app-subchapter-editor',
  imports: [ReactiveFormsModule, RouterLink, CommonModule, CourseLockBannerComponent],
  templateUrl: './subchapter-editor.html',
  styleUrl: './subchapter-editor.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SubchapterEditor implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly blockService = inject(BlockService);
  private readonly problemService = inject(ProblemService);
  private readonly chapterService = inject(ChapterService);
  private readonly courseService = inject(CourseService);
  private readonly dialogService = inject(DialogService);

  readonly course = signal<Course | null>(null);
  readonly subchapter = signal<Subchapter | null>(null);
  readonly isRenaming = signal(false);
  readonly renameForm: FormGroup;
  // The API refuses every change to a published course
  readonly isLocked = computed(() => this.course()?.status === CourseStatus.Published);

  readonly blocks = signal<Block[]>([]);
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');
  readonly isAddingBlock = signal(false);
  readonly isEditingBlock = signal(false);
  readonly editingBlockId = signal<string | null>(null);
  readonly selectedBlockType = signal<BlockType | null>(null);

  // Problem-specific signals
  readonly myProblems = signal<ProblemListItem[]>([]);
  readonly isLoadingProblems = signal(false);
  readonly selectedProblemId = signal<string | null>(null);

  readonly blockForm: FormGroup;
  readonly BlockType = BlockType;

  courseId = '';
  chapterId = '';
  subchapterId = '';

  get questions(): FormArray {
    return this.blockForm.get('questions') as FormArray;
  }

  constructor() {
    this.blockForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(3)]],
      type: [null, Validators.required],
      // Theory fields
      theoryContent: [''],
      // Video fields
      videoUrl: [''],
      // Problem fields
      problemId: [''],
      // Quiz fields
      questions: this.fb.array([])
    });

    this.renameForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(3)]]
    });
  }

  ngOnInit(): void {
    this.courseId = this.route.snapshot.paramMap.get('courseId') ?? '';
    this.chapterId = this.route.snapshot.paramMap.get('chapterId') ?? '';
    this.subchapterId = this.route.snapshot.paramMap.get('subchapterId') ?? '';

    if (this.subchapterId) {
      this.loadCourseAndSubchapter();
      this.loadBlocks();
    }
  }

  /** The subchapter has no endpoint of its own, so it is picked from its chapter's list. */
  loadCourseAndSubchapter(): void {
    this.courseService.getCourseById(this.courseId).subscribe({
      next: (course) => this.course.set(course),
      error: (error: unknown) => this.errorMessage.set(extractApiError(error, 'Failed to load course'))
    });
    this.chapterService.getChapterSubchapters(this.chapterId).subscribe({
      next: (subchapters) => {
        const subchapter = subchapters.find(s => s.id === this.subchapterId) ?? null;
        this.subchapter.set(subchapter);
        if (!subchapter) {
          this.errorMessage.set('Subchapter not found');
        }
      },
      error: (error: unknown) => this.errorMessage.set(extractApiError(error, 'Failed to load subchapter'))
    });
  }

  onUnpublished(course: Course): void {
    this.course.set(course);
  }

  startRename(): void {
    this.renameForm.reset({ title: this.subchapter()?.title ?? '' });
    this.isRenaming.set(true);
  }

  cancelRename(): void {
    this.isRenaming.set(false);
  }

  saveRename(): void {
    if (this.renameForm.invalid) {
      this.renameForm.markAllAsTouched();
      return;
    }

    this.errorMessage.set('');
    const title = (this.renameForm.value.title as string).trim();
    this.chapterService.renameSubchapter(this.chapterId, this.subchapterId, { title }).subscribe({
      next: (subchapter) => {
        this.subchapter.set(subchapter);
        this.isRenaming.set(false);
        this.successMessage.set('Subchapter renamed');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to rename subchapter'));
      }
    });
  }

  /** Opens the problem's own editor (test cases, starter code), coming back here afterwards. */
  editProblem(problemId: string): void {
    this.router.navigate(['/problems', problemId, 'edit'], {
      queryParams: { returnUrl: this.getCurrentUrl() }
    });
  }

  getCurrentUrl(): string {
    return this.router.url;
  }

  startEditBlock(block: Block): void {
    this.isEditingBlock.set(true);
    this.editingBlockId.set(block.id);
    this.isAddingBlock.set(true); // Reuse the same form UI
    this.blockForm.reset();
    this.questions.clear();

    // Populate form based on block type
    this.blockForm.patchValue({
      title: block.title,
      type: block.type
    });

    if (block.type === BlockType.Problem && block.problem) {
      // The block's title and which problem it shows; the problem itself is edited on its own page
      this.blockForm.patchValue({ problemId: block.problem.id });
      this.selectedProblemId.set(block.problem.id);
    } else if (block.type === BlockType.Theory && block.theoryContent) {
      this.blockForm.patchValue({ theoryContent: block.theoryContent.content });
    } else if (block.type === BlockType.Video && block.videoContent) {
      this.blockForm.patchValue({ videoUrl: block.videoContent.videoUrl });
    } else if (block.type === BlockType.Quiz && block.quiz) {
      // Populate quiz questions
      this.questions.clear();
      block.quiz.questions.forEach(q => {
        const questionGroup = this.fb.group({
          questionText: [q.content, Validators.required],
          type: [q.type, Validators.required],
          points: [q.points, [Validators.required, Validators.min(1)]],
          explanation: [q.explanation || ''],
          answers: this.fb.array(
            q.answers.map(a => this.fb.group({
              answerText: [a.text, Validators.required],
              isCorrect: [a.isCorrect ?? false]
            }))
          )
        });
        this.questions.push(questionGroup);
      });
    }

    // The form is shared by every block type, so the validators of the type edited before
    // must be replaced by this block's (called after the questions exist, so no blank one is added)
    this.onBlockTypeChange(block.type);
  }

  cancelEdit(): void {
    this.isEditingBlock.set(false);
    this.editingBlockId.set(null);
    this.toggleAddBlock();
  }

  loadBlocks(): void {
    this.isLoading.set(true);
    this.blockService.getSubchapterBlocks(this.subchapterId).subscribe({
      next: (blocks) => {
        this.blocks.set(blocks);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.isLoading.set(false);
        this.errorMessage.set(extractApiError(error, 'Failed to load blocks'));
      }
    });
  }

  toggleAddBlock(): void {
    this.isAddingBlock.update(v => !v);
    // Start from a clean form either way: closing discards the draft, opening must not inherit
    // the values or validators of the block edited before
    this.blockForm.reset();
    this.questions.clear();
    this.selectedProblemId.set(null);
    this.onBlockTypeChange(null);
  }

  onBlockTypeChange(type: BlockType | null): void {
    this.selectedBlockType.set(type);
    this.blockForm.patchValue({ type });

    // Clear validators for all type-specific fields first
    this.blockForm.get('theoryContent')?.clearValidators();
    this.blockForm.get('videoUrl')?.clearValidators();
    this.blockForm.get('problemId')?.clearValidators();

    // Set validators based on selected type
    if (type === BlockType.Theory) {
      this.blockForm.get('theoryContent')?.setValidators([Validators.required]);
    } else if (type === BlockType.Video) {
      this.blockForm.get('videoUrl')?.setValidators([Validators.required]);
    }

    // Update validity
    this.blockForm.get('theoryContent')?.updateValueAndValidity();
    this.blockForm.get('videoUrl')?.updateValueAndValidity();
    this.blockForm.get('problemId')?.updateValueAndValidity();

    // Load teacher's problems when Problem type is selected
    if (type === BlockType.Problem && this.myProblems().length === 0) {
      this.loadMyProblems();
    }

    // Add initial question when Quiz type is selected
    if (type === BlockType.Quiz && this.questions.length === 0) {
      this.addQuestion();
    }
  }

  loadMyProblems(): void {
    this.isLoadingProblems.set(true);
    // The largest page the API serves; a teacher with more problems can search on the Problems page
    this.problemService.getMyProblems({ page: 1, pageSize: MAX_PAGE_SIZE }).subscribe({
      next: (page) => {
        this.myProblems.set(page.items);
        this.isLoadingProblems.set(false);
      },
      error: (error: unknown) => {
        this.isLoadingProblems.set(false);
        this.errorMessage.set(extractApiError(error, 'Failed to load problems'));
      }
    });
  }

  // Quiz management methods
  addQuestion(): void {
    const questionGroup = this.fb.group({
      questionText: ['', Validators.required],
      type: ['SingleChoice', Validators.required],
      points: [1, [Validators.required, Validators.min(1)]],
      explanation: [''],
      answers: this.fb.array([])
    });
    this.questions.push(questionGroup);
    // Add two default answers
    this.addAnswer(this.questions.length - 1);
    this.addAnswer(this.questions.length - 1);
  }

  removeQuestion(index: number): void {
    this.questions.removeAt(index);
  }

  getAnswers(questionIndex: number): FormArray {
    return this.questions.at(questionIndex).get('answers') as FormArray;
  }

  addAnswer(questionIndex: number): void {
    const answersArray = this.getAnswers(questionIndex);
    const answerGroup = this.fb.group({
      answerText: ['', Validators.required],
      isCorrect: [false]
    });
    answersArray.push(answerGroup);
  }

  removeAnswer(questionIndex: number, answerIndex: number): void {
    const answersArray = this.getAnswers(questionIndex);
    answersArray.removeAt(answerIndex);
  }

  onQuestionTypeChange(questionIndex: number, type: string): void {
    const questionGroup = this.questions.at(questionIndex);
    questionGroup.patchValue({ type });

    // For TrueFalse, ensure exactly 2 answers (True/False)
    if (type === 'TrueFalse') {
      const answersArray = this.getAnswers(questionIndex);
      answersArray.clear();

      const trueAnswer = this.fb.group({
        answerText: ['True'],
        isCorrect: [false]
      });
      const falseAnswer = this.fb.group({
        answerText: ['False'],
        isCorrect: [false]
      });
      answersArray.push(trueAnswer);
      answersArray.push(falseAnswer);
    }

    // For SingleChoice, ensure only one answer is marked correct
    if (type === 'SingleChoice') {
      this.ensureSingleCorrectAnswer(questionIndex);
    }
  }

  onSingleChoiceChange(questionIndex: number, answerIndex: number): void {
    const answersArray = this.getAnswers(questionIndex);

    // Uncheck all answers first
    for (let i = 0; i < answersArray.length; i++) {
      answersArray.at(i).patchValue({ isCorrect: false });
    }

    // Check only the selected answer
    answersArray.at(answerIndex).patchValue({ isCorrect: true });
  }

  private ensureSingleCorrectAnswer(questionIndex: number): void {
    const answersArray = this.getAnswers(questionIndex);
    let foundCorrect = false;

    for (let i = 0; i < answersArray.length; i++) {
      const isCorrect = answersArray.at(i).get('isCorrect')?.value;
      if (isCorrect && !foundCorrect) {
        foundCorrect = true;
      } else if (isCorrect && foundCorrect) {
        // Uncheck additional correct answers
        answersArray.at(i).patchValue({ isCorrect: false });
      }
    }
  }

  onProblemSelect(problemId: string): void {
    this.selectedProblemId.set(problemId);
  }

  addBlock(): void {
    const isEditing = this.isEditingBlock();
    const blockId = this.editingBlockId();

    // For Quiz type, validate questions separately
    const formValue = this.blockForm.value;

    if (formValue.type === BlockType.Quiz) {
      // Only validate title and type for Quiz
      if (!formValue.title || formValue.title.length < 3) {
        this.errorMessage.set('Title is required (minimum 3 characters)');
        this.blockForm.markAllAsTouched();
        return;
      }
    } else {
      // For other types, use standard validation
      if (this.blockForm.invalid) {
        this.blockForm.markAllAsTouched();
        return;
      }
    }

    this.errorMessage.set('');
    let request$;

    // Call type-specific endpoint
    switch (formValue.type) {
      case BlockType.Theory:
        const theoryRequest: CreateTheoryBlockRequest = {
          title: formValue.title,
          content: formValue.theoryContent || ''
        };
        request$ = isEditing && blockId
          ? this.blockService.updateTheoryBlock(this.subchapterId, blockId, theoryRequest)
          : this.blockService.addTheoryBlock(this.subchapterId, theoryRequest);
        break;

      case BlockType.Video:
        const videoRequest: CreateVideoBlockRequest = {
          title: formValue.title,
          videoUrl: formValue.videoUrl || ''
        };
        request$ = isEditing && blockId
          ? this.blockService.updateVideoBlock(this.subchapterId, blockId, videoRequest)
          : this.blockService.addVideoBlock(this.subchapterId, videoRequest);
        break;

      case BlockType.Quiz:
        // Validate questions
        if (this.questions.length === 0) {
          this.errorMessage.set('At least one question is required');
          return;
        }

        // Validate each question has at least one correct answer
        for (let i = 0; i < this.questions.length; i++) {
          const answers = this.getAnswers(i);
          if (answers.length < 2) {
            this.errorMessage.set(`Question ${i + 1} must have at least 2 answers`);
            return;
          }

          const hasCorrectAnswer = answers.controls.some(a => a.get('isCorrect')?.value === true);
          if (!hasCorrectAnswer) {
            this.errorMessage.set(`Question ${i + 1} must have at least one correct answer`);
            return;
          }
        }

        const quizRequest: CreateQuizBlockRequest = {
          title: formValue.title,
          questions: this.questions.value.map((q: any) => ({
            questionText: q.questionText,
            type: q.type,
            points: q.points,
            explanation: q.explanation || null,
            answers: q.answers.map((a: any) => ({
              answerText: a.answerText,
              isCorrect: a.isCorrect
            }))
          }))
        };
        request$ = isEditing && blockId
          ? this.blockService.updateQuizBlock(this.subchapterId, blockId, quizRequest)
          : this.blockService.addQuizBlock(this.subchapterId, quizRequest);
        break;

      case BlockType.Problem:
        const problemId = this.selectedProblemId() || formValue.problemId;
        if (!problemId) {
          this.errorMessage.set('Please select a problem');
          return;
        }

        const problemRequest: CreateProblemBlockRequest = {
          title: formValue.title,
          problemId: problemId
        };
        request$ = isEditing && blockId
          ? this.blockService.updateProblemBlock(this.subchapterId, blockId, problemRequest)
          : this.blockService.addProblemBlock(this.subchapterId, problemRequest);
        break;

      default:
        this.errorMessage.set('Invalid block type');
        return;
    }

    request$.subscribe({
      next: () => {
        this.blockForm.reset();
        this.questions.clear();
        this.isAddingBlock.set(false);
        this.isEditingBlock.set(false);
        this.editingBlockId.set(null);
        this.selectedBlockType.set(null);
        this.successMessage.set(isEditing ? 'Block updated successfully' : 'Block added successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
        // Reload blocks to get complete data from server
        this.loadBlocks();
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to save block'));
      }
    });
  }

  moveBlockUp(blockId: string, currentIndex: number): void {
    if (currentIndex === 1) return;

    const newIndex = currentIndex - 1;
    this.blockService.updateBlockOrder(this.subchapterId, blockId, newIndex).subscribe({
      next: () => {
        this.loadBlocks();
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to reorder block'));
      }
    });
  }

  moveBlockDown(blockId: string, currentIndex: number): void {
    const maxIndex = this.blocks().length;
    if (currentIndex === maxIndex) return;

    const newIndex = currentIndex + 1;
    this.blockService.updateBlockOrder(this.subchapterId, blockId, newIndex).subscribe({
      next: () => {
        this.loadBlocks();
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to reorder block'));
      }
    });
  }

  async deleteBlock(blockId: string): Promise<void> {
    const { confirmed } = await this.dialogService.confirm({
      title: 'Delete block?',
      message: 'Are you sure you want to delete this block? This cannot be undone.',
      confirmText: 'Delete',
      cancelText: 'Cancel',
      type: 'warning'
    });
    if (!confirmed) {
      return;
    }

    this.blockService.deleteBlock(this.subchapterId, blockId).subscribe({
      next: () => {
        this.loadBlocks();
        this.successMessage.set('Block deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to delete block'));
      }
    });
  }

  getBlockTypeLabel(type: BlockType): string {
    switch (type) {
      case BlockType.Theory: return 'Theory';
      case BlockType.Video: return 'Video';
      case BlockType.Quiz: return 'Quiz';
      case BlockType.Problem: return 'Problem';
      default: return 'Unknown';
    }
  }

  getBlockTypeBadgeClass(type: BlockType): string {
    switch (type) {
      case BlockType.Theory: return 'bg-blue-100 text-blue-800';
      case BlockType.Video: return 'bg-purple-100 text-purple-800';
      case BlockType.Quiz: return 'bg-green-100 text-green-800';
      case BlockType.Problem: return 'bg-orange-100 text-orange-800';
      default: return 'bg-gray-100 text-gray-800';
    }
  }

  getTotalQuizPoints(quiz: any): number {
    if (!quiz || !quiz.questions) return 0;
    return quiz.questions.reduce((sum: number, q: any) => sum + (q.points || 0), 0);
  }
}
