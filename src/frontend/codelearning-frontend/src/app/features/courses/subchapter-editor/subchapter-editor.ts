import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, FormGroup, FormArray, ReactiveFormsModule, Validators } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { 
  BlockService, 
  CreateTheoryBlockRequest, 
  CreateVideoBlockRequest, 
  CreateProblemBlockRequest 
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
      testCases: this.fb.array([])
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
      this.selectedBlockType.set(null);
      this.selectedProblemId.set(null);
      this.isCreatingNewProblem.set(false);
    }
  }

  onBlockTypeChange(type: BlockType): void {
    this.selectedBlockType.set(type);
    this.blockForm.patchValue({ type });
    
    // Load teacher's problems when Problem type is selected
    if (type === BlockType.Problem && this.myProblems().length === 0) {
      this.loadMyProblems();
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

  onProblemSelect(problemId: string): void {
    this.selectedProblemId.set(problemId);
    this.isCreatingNewProblem.set(false);
  }

  addBlock(): void {
    if (this.blockForm.invalid) {
      this.blockForm.markAllAsTouched();
      return;
    }

    this.errorMessage.set('');

    const formValue = this.blockForm.value;
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
                  this.errorMessage.set(error.error?.detail || 'Failed to create block');
                }
              });
            },
            error: (error: any) => {
              this.errorMessage.set(error.error?.detail || 'Failed to create problem');
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
        this.isAddingBlock.set(false);
        this.selectedBlockType.set(null);
        this.successMessage.set('Block added successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
        // Reload blocks to get complete data from server
        this.loadBlocks();
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || error.error?.message || 'Failed to add block');
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
}
