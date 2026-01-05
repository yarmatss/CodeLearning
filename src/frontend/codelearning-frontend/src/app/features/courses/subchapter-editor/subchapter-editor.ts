import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, FormGroup, FormArray, ReactiveFormsModule, Validators } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { 
  BlockService, 
  CreateTheoryBlockRequest, 
  CreateVideoBlockRequest, 
  CreateProblemBlockRequest,
  CreateQuizBlockRequest,
  CreateQuizQuestionRequest,
  CreateQuizAnswerRequest
} from '../../../core/services/block.service';
import { ProblemService, ProblemResponse, CreateProblemRequest } from '../../../core/services/problem.service';
import { Block, BlockType } from '../../../core/models/course.model';

@Component({
  selector: 'app-subchapter-editor',
  imports: [ReactiveFormsModule, RouterLink, CommonModule],
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

  readonly blocks = signal<Block[]>([]);
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');
  readonly isAddingBlock = signal(false);
  readonly selectedBlockType = signal<BlockType | null>(null);
  
  // Problem-specific signals
  readonly myProblems = signal<ProblemResponse[]>([]);
  readonly isLoadingProblems = signal(false);
  readonly selectedProblemId = signal<string | null>(null);
  readonly isCreatingNewProblem = signal(false);

  readonly blockForm: FormGroup;
  readonly BlockType = BlockType;

  courseId = '';
  chapterId = '';
  subchapterId = '';

  get testCases(): FormArray {
    return this.blockForm.get('testCases') as FormArray;
  }

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
      newProblemTitle: [''],
      newProblemDescription: [''],
      newProblemDifficulty: ['Easy'],
      // Test cases for new problems
      testCases: this.fb.array([]),
      // Quiz fields
      questions: this.fb.array([])
    });
  }

  ngOnInit(): void {
    this.courseId = this.route.snapshot.paramMap.get('courseId') ?? '';
    this.chapterId = this.route.snapshot.paramMap.get('chapterId') ?? '';
    this.subchapterId = this.route.snapshot.paramMap.get('subchapterId') ?? '';
    
    if (this.subchapterId) {
      this.loadBlocks();
    }
  }

  loadBlocks(): void {
    this.isLoading.set(true);
    this.blockService.getSubchapterBlocks(this.subchapterId).subscribe({
      next: (blocks) => {
        this.blocks.set(blocks);
        this.isLoading.set(false);
      },
      error: (error: any) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to load blocks');
      }
    });
  }

  toggleAddBlock(): void {
    this.isAddingBlock.update(v => !v);
    if (!this.isAddingBlock()) {
      this.blockForm.reset();
      this.testCases.clear();
      this.questions.clear();
      this.selectedBlockType.set(null);
      this.selectedProblemId.set(null);
      this.isCreatingNewProblem.set(false);
    }
  }

  onBlockTypeChange(type: BlockType): void {
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
    this.problemService.getMyProblems().subscribe({
      next: (problems) => {
        this.myProblems.set(problems);
        this.isLoadingProblems.set(false);
      },
      error: (error: any) => {
        this.isLoadingProblems.set(false);
        this.errorMessage.set(error.error?.detail || 'Failed to load problems');
      }
    });
  }

  toggleCreateNewProblem(): void {
    this.isCreatingNewProblem.update(v => !v);
    this.selectedProblemId.set(null);
    
    // Add one test case when switching to create new problem
    if (this.isCreatingNewProblem() && this.testCases.length === 0) {
      this.addTestCase();
    }
  }

  addTestCase(): void {
    const testCaseGroup = this.fb.group({
      input: ['', Validators.required],
      expectedOutput: ['', Validators.required],
      isPublic: [true]
    });
    this.testCases.push(testCaseGroup);
  }

  removeTestCase(index: number): void {
    this.testCases.removeAt(index);
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
    
    console.log(`Question ${questionIndex} type changed to:`, type);
    console.log('Question form value:', questionGroup.value);
    
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
    this.isCreatingNewProblem.set(false);
  }

  addBlock(): void {
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
        request$ = this.blockService.addTheoryBlock(this.subchapterId, theoryRequest);
        break;
      
      case BlockType.Video:
        const videoRequest: CreateVideoBlockRequest = {
          title: formValue.title,
          videoUrl: formValue.videoUrl || ''
        };
        request$ = this.blockService.addVideoBlock(this.subchapterId, videoRequest);
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
        request$ = this.blockService.addQuizBlock(this.subchapterId, quizRequest);
        break;
      
      case BlockType.Problem:
        if (this.isCreatingNewProblem()) {
          // Validate test cases
          if (this.testCases.length === 0) {
            this.errorMessage.set('At least one test case is required');
            return;
          }
          
          const hasPublicTestCase = this.testCases.controls.some(tc => tc.get('isPublic')?.value === true);
          if (!hasPublicTestCase) {
            this.errorMessage.set('At least one test case must be public');
            return;
          }
          
          // Create new problem first, then create block
          const newProblemData: CreateProblemRequest = {
            title: formValue.newProblemTitle || formValue.title,
            description: formValue.newProblemDescription || '',
            difficulty: formValue.newProblemDifficulty as 'Easy' | 'Medium' | 'Hard',
            testCases: this.testCases.value
          };
          
          this.problemService.createProblem(newProblemData).subscribe({
            next: (createdProblem) => {
              const problemBlockRequest: CreateProblemBlockRequest = {
                title: formValue.title,
                problemId: createdProblem.id
              };
              
              this.blockService.addProblemBlock(this.subchapterId, problemBlockRequest).subscribe({
                next: () => {
                  this.blockForm.reset();
                  this.testCases.clear();
                  this.isAddingBlock.set(false);
                  this.selectedBlockType.set(null);
                  this.isCreatingNewProblem.set(false);
                  this.successMessage.set('Problem block added successfully');
                  setTimeout(() => this.successMessage.set(''), 3000);
                  this.loadBlocks();
                  this.loadMyProblems(); // Refresh problems list
                },
                error: (error: any) => {
                  this.errorMessage.set(this.extractErrorMessage(error));
                }
              });
            },
            error: (error: any) => {
              this.errorMessage.set(this.extractErrorMessage(error));
            }
          });
          return; // Exit early since we handle response in nested subscribe
        } else {
          // Use existing problem
          const problemId = this.selectedProblemId() || formValue.problemId;
          if (!problemId) {
            this.errorMessage.set('Please select a problem or create a new one');
            return;
          }
          
          const problemRequest: CreateProblemBlockRequest = {
            title: formValue.title,
            problemId: problemId
          };
          request$ = this.blockService.addProblemBlock(this.subchapterId, problemRequest);
        }
        break;
      
      default:
        this.errorMessage.set('Invalid block type');
        return;
    }

    request$.subscribe({
      next: (block) => {
        this.blockForm.reset();
        this.testCases.clear();
        this.questions.clear();
        this.isAddingBlock.set(false);
        this.selectedBlockType.set(null);
        this.successMessage.set('Block added successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
        // Reload blocks to get complete data from server
        this.loadBlocks();
      },
      error: (error: any) => {
        this.errorMessage.set(this.extractErrorMessage(error));
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
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to reorder block');
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
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to reorder block');
      }
    });
  }

  deleteBlock(blockId: string): void {
    if (!confirm('Are you sure you want to delete this block?')) {
      return;
    }

    this.blockService.deleteBlock(this.subchapterId, blockId).subscribe({
      next: () => {
        this.loadBlocks();
        this.successMessage.set('Block deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.message || 'Failed to delete block');
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

  private extractErrorMessage(error: any): string {
    // Check for detailed validation errors
    if (error.error?.errors) {
      const errorMessages: string[] = [];
      Object.keys(error.error.errors).forEach(key => {
        const messages = error.error.errors[key];
        if (Array.isArray(messages)) {
          // Parse field path to make it more readable
          const fieldPath = this.formatFieldPath(key);
          messages.forEach(msg => {
            errorMessages.push(`${fieldPath}: ${msg}`);
          });
        }
      });
      if (errorMessages.length > 0) {
        return errorMessages.join('\n');
      }
    }
    // Fallback to generic message
    return error.error?.detail || error.error?.message || 'An error occurred';
  }

  private formatFieldPath(path: string): string {
    // Convert "questions[0].Answers[1].AnswerText" to "Question 1, Answer 2"
    const questionMatch = path.match(/questions\[(\d+)\]/);
    const answerMatch = path.match(/Answers\[(\d+)\]/);
    const fieldMatch = path.match(/\.([^.\[]+)$/);

    let result = '';
    if (questionMatch) {
      const questionNum = parseInt(questionMatch[1]) + 1;
      result += `Question ${questionNum}`;
    }
    if (answerMatch) {
      const answerNum = parseInt(answerMatch[1]) + 1;
      result += result ? `, Answer ${answerNum}` : `Answer ${answerNum}`;
    }
    if (fieldMatch && !answerMatch && !questionMatch) {
      result = fieldMatch[1];
    }

    return result || path;
  }
}
