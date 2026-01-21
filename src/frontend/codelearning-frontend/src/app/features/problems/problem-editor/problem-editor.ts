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
  UpdateTestCaseRequest,
  CreateStarterCodeRequest,
  TagResponse
} from '../../../core/services/problem.service';
import { LanguageService } from '../../../core/services/language.service';

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
  private readonly languageService = inject(LanguageService);

  readonly problem = signal<ProblemResponse | null>(null);
  readonly testCases = signal<TestCaseResponse[]>([]);
  readonly starterCodes = signal<StarterCodeResponse[]>([]);
  readonly availableTags = signal<TagResponse[]>([]);
  
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');
  readonly isEditMode = signal(false);
  readonly editingTestCaseId = signal<string | null>(null);
  readonly editTestCaseForm: FormGroup;
  
  readonly problemForm: FormGroup;
  readonly testCaseForm: FormGroup;
  readonly starterCodeForm: FormGroup;
  
  readonly availableLanguages = this.languageService.languages;

  problemId: string | null = null;
  returnUrl: string | null = null;

  constructor() {
    this.problemForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(3)]],
      description: ['', Validators.required],
      difficulty: ['Easy', Validators.required],
      tagIds: [[]],
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

    this.editTestCaseForm = this.fb.group({
      input: ['', Validators.required],
      expectedOutput: ['', Validators.required],
      isPublic: [true]
    });
  }

  get testCasesFormArray(): FormArray {
    return this.problemForm.get('testCases') as FormArray;
  }

  get starterCodesFormArray(): FormArray {
    return this.problemForm.get('starterCodes') as FormArray;
  }

  ngOnInit(): void {
    // Load languages first
    this.languageService.getLanguages().subscribe();
    
    this.loadTags();
    this.problemId = this.route.snapshot.paramMap.get('id');
    this.returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    if (this.problemId) {
      this.isEditMode.set(true);
      this.loadProblem();
    }
  }

  loadTags(): void {
    this.problemService.getTags().subscribe({
      next: (tags) => this.availableTags.set(tags),
      error: () => this.availableTags.set([])
    });
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
          difficulty: problem.difficulty,
          tagIds: problem.tags.map(t => t.id)
        });
        
        this.isLoading.set(false);
      },
      error: (error: any) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.detail || 'Failed to load problem');
      }
    });
  }

  refreshTestCases(): void {
    if (!this.problemId) return;

    this.problemService.getProblem(this.problemId).subscribe({
      next: (problem) => {
        this.problem.set(problem);
        this.testCases.set(problem.testCases);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to refresh test cases');
      }
    });
  }

  refreshStarterCodes(): void {
    if (!this.problemId) return;

    this.problemService.getProblem(this.problemId).subscribe({
      next: (problem) => {
        this.problem.set(problem);
        this.starterCodes.set(problem.starterCodes);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to refresh starter codes');
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
        difficulty: formValue.difficulty,
        tagIds: formValue.tagIds || []
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
        tagIds: formValue.tagIds || [],
        testCases: formValue.testCases,
        starterCodes: formValue.starterCodes
      };

      this.problemService.createProblem(createData).subscribe({
        next: (problem) => {
          this.successMessage.set('Problem created successfully');
          if (this.returnUrl) {
            this.router.navigateByUrl(this.returnUrl);
          } else {
            this.router.navigate(['/problems', problem.id, 'edit']);
          }
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
        this.refreshTestCases();
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
        this.refreshTestCases();
        this.successMessage.set('Test case deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to delete test case');
      }
    });
  }

  startEditingTestCase(testCase: TestCaseResponse): void {
    this.editingTestCaseId.set(testCase.id);
    this.editTestCaseForm.patchValue({
      input: testCase.input,
      expectedOutput: testCase.expectedOutput,
      isPublic: testCase.isPublic
    });
  }

  cancelEditingTestCase(): void {
    this.editingTestCaseId.set(null);
    this.editTestCaseForm.reset({ isPublic: true });
  }

  saveTestCaseEdit(): void {
    if (this.editTestCaseForm.invalid || !this.editingTestCaseId()) {
      this.editTestCaseForm.markAllAsTouched();
      return;
    }

    const data: UpdateTestCaseRequest = this.editTestCaseForm.value;
    const testCaseId = this.editingTestCaseId()!;

    this.problemService.updateTestCase(testCaseId, data).subscribe({
      next: () => {
        this.editingTestCaseId.set(null);
        this.editTestCaseForm.reset({ isPublic: true });
        this.refreshTestCases();
        this.successMessage.set('Test case updated successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to update test case');
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
        this.refreshTestCases();
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
        this.refreshTestCases();
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
    return this.languageService.getLanguageName(languageId);
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
        this.refreshStarterCodes();
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
        this.refreshStarterCodes();
        this.successMessage.set('Starter code deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: any) => {
        this.errorMessage.set(error.error?.detail || 'Failed to delete starter code');
      }
    });
  }

  getAvailableLanguagesForStarterCode() {
    const languages = this.availableLanguages();
    if (this.isEditMode()) {
      const usedLanguageIds = this.starterCodes().map(sc => sc.languageId);
      return languages.filter(lang => !usedLanguageIds.includes(lang.id));
    } else {
      // Create mode - check FormArray
      const usedLanguageIds = this.starterCodesFormArray.controls.map(
        control => control.get('languageId')?.value
      );
      return languages.filter(lang => !usedLanguageIds.includes(lang.id));
    }
  }

  isTagSelected(tagId: string): boolean {
    const selectedTags = this.problemForm.get('tagIds')?.value || [];
    return selectedTags.includes(tagId);
  }

  toggleTag(tagId: string): void {
    const currentTags = this.problemForm.get('tagIds')?.value || [];
    const index = currentTags.indexOf(tagId);
    
    if (index > -1) {
      currentTags.splice(index, 1);
    } else {
      currentTags.push(tagId);
    }
    
    this.problemForm.patchValue({ tagIds: [...currentTags] });
  }
}
