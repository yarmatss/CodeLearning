import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, FormGroup, FormArray, ReactiveFormsModule, Validators } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { 
  ProblemService, 
  ProblemResponse, 
  CreateProblemRequest,
  UpdateProblemRequest,
  TestCaseResponse,
  StarterCodeResponse,
  CreateTestCaseRequest,
  CreateStarterCodeRequest
} from '../../../core/services/problem.service';

interface Language {
  id: string;
  name: string;
}

@Component({
  selector: 'app-problem-editor',
  imports: [CommonModule, RouterLink, ReactiveFormsModule],
  templateUrl: './problem-editor.html',
  styleUrl: './problem-editor.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProblemEditor implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly problemService = inject(ProblemService);

  readonly problem = signal<ProblemResponse | null>(null);
  readonly testCases = signal<TestCaseResponse[]>([]);
  readonly starterCodes = signal<StarterCodeResponse[]>([]);
  
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');
  readonly isEditMode = signal(false);
  
  readonly problemForm: FormGroup;
  readonly testCaseForm: FormGroup;
  readonly starterCodeForm: FormGroup;
  
  readonly availableLanguages: Language[] = [
    { id: '11111111-1111-1111-1111-111111111111', name: 'Python' },
    { id: '22222222-2222-2222-2222-222222222222', name: 'JavaScript' },
    { id: '33333333-3333-3333-3333-333333333333', name: 'C#' },
    { id: '44444444-4444-4444-4444-444444444444', name: 'Java' }
  ];

  problemId: string | null = null;

  constructor() {
    this.problemForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(3)]],
      description: ['', Validators.required],
      difficulty: ['Easy', Validators.required],
      testCases: this.fb.array([]),
      starterCodes: this.fb.array([])
    });

    this.testCaseForm = this.fb.group({
      input: ['', Validators.required],
      expectedOutput: ['', Validators.required],
      isPublic: [true]
    });

    this.starterCodeForm = this.fb.group({
      languageId: ['', Validators.required],
      code: ['', Validators.required]
    });
  }

  get testCasesFormArray(): FormArray {
    return this.problemForm.get('testCases') as FormArray;
  }

  get starterCodesFormArray(): FormArray {
    return this.problemForm.get('starterCodes') as FormArray;
  }

  ngOnInit(): void {
    this.problemId = this.route.snapshot.paramMap.get('id');
    if (this.problemId) {
      this.isEditMode.set(true);
      this.loadProblem();
    }
  }

  loadProblem(): void {
    if (!this.problemId) return;

    this.isLoading.set(true);
    this.problemService.getProblem(this.problemId).subscribe({
      next: (problem) => {
        this.problem.set(problem);
        this.testCases.set(problem.testCases);
        this.starterCodes.set(problem.starterCodes);
        
        this.problemForm.patchValue({
          title: problem.title,
          description: problem.description,
          difficulty: problem.difficulty
        });
        
        this.isLoading.set(false);
      },
      error: (error: any) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.detail || 'Failed to load problem');
      }
    });
  }

  saveProblem(): void {
    if (this.problemForm.invalid) {
      this.problemForm.markAllAsTouched();
      return;
    }

    this.errorMessage.set('');
    const formValue = this.problemForm.value;

    if (this.isEditMode() && this.problemId) {
      // Update existing problem
      const updateData: UpdateProblemRequest = {
        title: formValue.title,
        description: formValue.description,
        difficulty: formValue.difficulty
      };

      this.problemService.updateProblem(this.problemId, updateData).subscribe({
        next: () => {
          this.successMessage.set('Problem updated successfully');
          setTimeout(() => this.successMessage.set(''), 3000);
          this.loadProblem();
        },
        error: (error: any) => {
          this.errorMessage.set(error.error?.detail || 'Failed to update problem');
        }
      });
    } else {
      // Create new problem - validate test cases first
      if (this.testCasesFormArray.length === 0) {
        this.errorMessage.set('At least one test case is required');
        return;
      }

      const hasPublicTestCase = this.testCasesFormArray.controls.some(
        tc => tc.get('isPublic')?.value === true
      );
      if (!hasPublicTestCase) {
        this.errorMessage.set('At least one test case must be public');
        return;
      }

      const createData: CreateProblemRequest = {
        title: formValue.title,
        description: formValue.description,
        difficulty: formValue.difficulty,
        testCases: formValue.testCases,
        starterCodes: formValue.starterCodes
      };

      this.problemService.createProblem(createData).subscribe({
        next: (problem) => {
          this.successMessage.set('Problem created successfully');
          this.router.navigate(['/problems', problem.id, 'edit']);
        },
        error: (error: any) => {
          // Extract validation errors if available
          const errors = error.error?.errors;
          if (errors) {
            const errorMessages: string[] = [];
            Object.keys(errors).forEach(key => {
              if (Array.isArray(errors[key])) {
                errorMessages.push(...errors[key]);
              }
            });
            this.errorMessage.set(errorMessages.join('. ') || 'Failed to create problem');
          } else {
            this.errorMessage.set(error.error?.detail || 'Failed to create problem');
          }
        }
      });
    }
  }

  // Test Case Management (Create Mode)
  addTestCaseToForm(): void {
    if (this.testCaseForm.invalid) {
      this.testCaseForm.markAllAsTouched();
      return;
    }

    const testCaseGroup = this.fb.group({
      input: [this.testCaseForm.value.input, Validators.required],
      expectedOutput: [this.testCaseForm.value.expectedOutput, Validators.required],
      isPublic: [this.testCaseForm.value.isPublic]
    });

    this.testCasesFormArray.push(testCaseGroup);
    this.testCaseForm.reset({ isPublic: true });
    this.successMessage.set('Test case added to form');
    setTimeout(() => this.successMessage.set(''), 2000);
  }

  removeTestCaseFromForm(index: number): void {
    this.testCasesFormArray.removeAt(index);
  }

  // Test Case Management (Edit Mode)
  addTestCase(): void {
    if (this.testCaseForm.invalid || !this.problemId) {
      this.testCaseForm.markAllAsTouched();
      return;
    }

    const data: CreateTestCaseRequest = this.testCaseForm.value;
    
    this.problemService.addTestCase(this.problemId, data).subscribe({
      next: () => {
        this.testCaseForm.reset({ isPublic: true });
        this.loadProblem();
        this.successMessage.set('Test case added successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to add test case');
      }
    });
  }

  deleteTestCase(testCaseId: string): void {
    if (!confirm('Are you sure you want to delete this test case?')) {
      return;
    }

    this.problemService.deleteTestCase(testCaseId).subscribe({
      next: () => {
        this.loadProblem();
        this.successMessage.set('Test case deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to delete test case');
      }
    });
  }

  moveTestCaseUp(index: number): void {
    if (index === 0 || !this.problemId) return;
    
    const cases = this.testCases();
    const newOrder = [...cases];
    [newOrder[index - 1], newOrder[index]] = [newOrder[index], newOrder[index - 1]];
    
    const reorderData = { testCaseIds: newOrder.map(tc => tc.id) };
    
    this.problemService.reorderTestCases(this.problemId, reorderData).subscribe({
      next: () => {
        this.loadProblem();
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to reorder test cases');
      }
    });
  }

  moveTestCaseDown(index: number): void {
    const cases = this.testCases();
    if (index === cases.length - 1 || !this.problemId) return;
    
    const newOrder = [...cases];
    [newOrder[index], newOrder[index + 1]] = [newOrder[index + 1], newOrder[index]];
    
    const reorderData = { testCaseIds: newOrder.map(tc => tc.id) };
    
    this.problemService.reorderTestCases(this.problemId, reorderData).subscribe({
      next: () => {
        this.loadProblem();
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to reorder test cases');
      }
    });
  }

  // Starter Code Management (Create Mode)
  addStarterCodeToForm(): void {
    if (this.starterCodeForm.invalid) {
      this.starterCodeForm.markAllAsTouched();
      return;
    }

    const starterCodeGroup = this.fb.group({
      languageId: [this.starterCodeForm.value.languageId, Validators.required],
      code: [this.starterCodeForm.value.code, Validators.required]
    });

    this.starterCodesFormArray.push(starterCodeGroup);
    this.starterCodeForm.reset();
    this.successMessage.set('Starter code added to form');
    setTimeout(() => this.successMessage.set(''), 2000);
  }

  removeStarterCodeFromForm(index: number): void {
    this.starterCodesFormArray.removeAt(index);
  }

  getLanguageNameById(languageId: string): string {
    return this.availableLanguages.find(lang => lang.id === languageId)?.name || 'Unknown';
  }

  // Starter Code Management (Edit Mode)
  addStarterCode(): void {
    if (this.starterCodeForm.invalid || !this.problemId) {
      this.starterCodeForm.markAllAsTouched();
      return;
    }

    const data: CreateStarterCodeRequest = this.starterCodeForm.value;
    
    this.problemService.addStarterCode(this.problemId, data).subscribe({
      next: () => {
        this.starterCodeForm.reset();
        this.loadProblem();
        this.successMessage.set('Starter code added successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to add starter code');
      }
    });
  }

  deleteStarterCode(starterCodeId: string): void {
    if (!confirm('Are you sure you want to delete this starter code?')) {
      return;
    }

    this.problemService.deleteStarterCode(starterCodeId).subscribe({
      next: () => {
        this.loadProblem();
        this.successMessage.set('Starter code deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to delete starter code');
      }
    });
  }

  getAvailableLanguagesForStarterCode() {
    if (this.isEditMode()) {
      const usedLanguageIds = this.starterCodes().map(sc => sc.languageId);
      return this.availableLanguages.filter(lang => !usedLanguageIds.includes(lang.id));
    } else {
      // Create mode - check FormArray
      const usedLanguageIds = this.starterCodesFormArray.controls.map(
        control => control.get('languageId')?.value
      );
      return this.availableLanguages.filter(lang => !usedLanguageIds.includes(lang.id));
    }
  }
}
