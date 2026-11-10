import { ChangeDetectionStrategy, Component, computed, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, FormControl, FormGroup, FormArray, ReactiveFormsModule, Validators } from '@angular/forms';
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
import { DialogService } from '../../../core/services/dialog.service';
import { extractApiError, isConflict } from '../../../core/utils/api-error';
import { BULK_TEST_CASES_EXAMPLE, parseBulkTestCases } from '../../../core/utils/test-case-import';

// The API accepts 1-10 starter codes when a problem is created
const MAX_STARTER_CODES = 10;

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
  private readonly dialogService = inject(DialogService);

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

  // A published course uses the problem: the API answers 409 to every edit until it is unpublished
  readonly isLocked = computed(() => this.problem()?.isUsedByPublishedCourse ?? false);

  // Starter code editing (edit mode): the API has no update, so a save is a delete plus an add
  readonly editingStarterCodeId = signal<string | null>(null);
  readonly editStarterCodeControl = new FormControl('', { nonNullable: true, validators: Validators.required });
  readonly isSavingStarterCode = signal(false);

  // Bulk test-case import
  readonly isBulkImportOpen = signal(false);
  readonly bulkImportControl = new FormControl('', { nonNullable: true });
  readonly bulkImportError = signal('');
  readonly isImporting = signal(false);
  readonly bulkExample = BULK_TEST_CASES_EXAMPLE;
  readonly maxStarterCodes = MAX_STARTER_CODES;

  problemId: string | null = null;
  returnUrl: string | null = null;

  constructor() {
    this.problemForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(3)]],
      // The API requires at least 20 characters
      description: ['', [Validators.required, Validators.minLength(20)]],
      difficulty: ['Easy', Validators.required],
      tagIds: [[]],
      testCases: this.fb.array([]),
      starterCodes: this.fb.array([])
    });

    // Input and expected output may be empty: a program can read nothing or print nothing
    this.testCaseForm = this.fb.group({
      input: [''],
      expectedOutput: [''],
      isPublic: [true]
    });

    this.starterCodeForm = this.fb.group({
      languageId: ['', Validators.required],
      code: ['', Validators.required]
    });

    this.editTestCaseForm = this.fb.group({
      input: [''],
      expectedOutput: [''],
      isPublic: [true]
    });
  }

  /**
   * Shows a failed edit. A 409 means a published course started using the problem meanwhile, so
   * the problem is reloaded to show the lock.
   */
  private showEditError(error: unknown, fallback: string): void {
    this.errorMessage.set(extractApiError(error, fallback));
    if (isConflict(error)) {
      this.refreshProblem();
    }
  }

  private refreshProblem(): void {
    if (!this.problemId) return;
    this.problemService.getProblem(this.problemId).subscribe({
      next: (problem) => {
        this.problem.set(problem);
        this.testCases.set(problem.testCases);
        this.starterCodes.set(problem.starterCodes);
      },
      error: () => undefined
    });
  }

  private flash(message: string, ms = 3000): void {
    this.successMessage.set(message);
    setTimeout(() => this.successMessage.set(''), ms);
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
      error: (error: unknown) => {
        this.isLoading.set(false);
        this.errorMessage.set(extractApiError(error, 'Failed to load problem'));
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
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to refresh test cases'));
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
      error: (error: unknown) => {
        this.errorMessage.set(extractApiError(error, 'Failed to refresh starter codes'));
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
        error: (error: unknown) => {
          this.showEditError(error, 'Failed to update problem');
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

      if (this.starterCodesFormArray.length === 0) {
        this.errorMessage.set('Add starter code for at least one language: it decides which languages students can use');
        return;
      }
      if (this.starterCodesFormArray.length > MAX_STARTER_CODES) {
        this.errorMessage.set(`A problem can have at most ${MAX_STARTER_CODES} starter codes`);
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
        error: (error: unknown) => {
          this.errorMessage.set(extractApiError(error, 'Failed to create problem'));
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

    this.pushTestCaseToForm(this.testCaseFormValue());
    this.testCaseForm.reset({ input: '', expectedOutput: '', isPublic: true });
    this.successMessage.set('Test case added to form');
    setTimeout(() => this.successMessage.set(''), 2000);
  }

  removeTestCaseFromForm(index: number): void {
    this.testCasesFormArray.removeAt(index);
  }

  private pushTestCaseToForm(testCase: CreateTestCaseRequest): void {
    this.testCasesFormArray.push(this.fb.group({
      input: [testCase.input],
      expectedOutput: [testCase.expectedOutput],
      isPublic: [testCase.isPublic]
    }));
  }

  /** The add form's value with empty fields as "" (a reset form holds null). */
  private testCaseFormValue(form: FormGroup = this.testCaseForm): CreateTestCaseRequest {
    const value = form.value;
    return {
      input: value.input ?? '',
      expectedOutput: value.expectedOutput ?? '',
      isPublic: !!value.isPublic
    };
  }

  // Bulk import: in create mode the cases join the form; in edit mode they are saved at once
  toggleBulkImport(): void {
    this.isBulkImportOpen.update(open => !open);
    this.bulkImportError.set('');
  }

  importTestCases(): void {
    this.bulkImportError.set('');
    let testCases: CreateTestCaseRequest[];
    try {
      testCases = parseBulkTestCases(this.bulkImportControl.value);
    } catch (error) {
      this.bulkImportError.set(error instanceof Error ? error.message : 'Invalid test cases');
      return;
    }

    if (!this.isEditMode() || !this.problemId) {
      testCases.forEach(tc => this.pushTestCaseToForm(tc));
      this.finishBulkImport(testCases.length);
      return;
    }

    this.isImporting.set(true);
    this.problemService.bulkAddTestCases(this.problemId, { testCases }).subscribe({
      next: (added) => {
        this.isImporting.set(false);
        this.refreshTestCases();
        this.finishBulkImport(added.length);
      },
      error: (error: unknown) => {
        this.isImporting.set(false);
        this.bulkImportError.set(extractApiError(error, 'Failed to import test cases'));
        if (isConflict(error)) {
          this.refreshProblem();
        }
      }
    });
  }

  private finishBulkImport(count: number): void {
    this.bulkImportControl.setValue('');
    this.isBulkImportOpen.set(false);
    this.flash(`${count} test case${count === 1 ? '' : 's'} imported`);
  }

  // Test Case Management (Edit Mode)
  addTestCase(): void {
    if (this.testCaseForm.invalid || !this.problemId) {
      this.testCaseForm.markAllAsTouched();
      return;
    }

    const data: CreateTestCaseRequest = this.testCaseFormValue();

    this.problemService.addTestCase(this.problemId, data).subscribe({
      next: () => {
        this.testCaseForm.reset({ input: '', expectedOutput: '', isPublic: true });
        this.refreshTestCases();
        this.successMessage.set('Test case added successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: unknown) => {
        this.showEditError(error, 'Failed to add test case');
      }
    });
  }

  async deleteTestCase(testCaseId: string): Promise<void> {
    const { confirmed } = await this.dialogService.confirm({
      title: 'Delete test case?',
      message: 'Are you sure you want to delete this test case?',
      confirmText: 'Delete',
      cancelText: 'Cancel',
      type: 'warning'
    });
    if (!confirmed) {
      return;
    }

    this.problemService.deleteTestCase(testCaseId).subscribe({
      next: () => {
        this.refreshTestCases();
        this.successMessage.set('Test case deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: unknown) => {
        this.showEditError(error, 'Failed to delete test case');
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

    const data: UpdateTestCaseRequest = this.testCaseFormValue(this.editTestCaseForm);
    const testCaseId = this.editingTestCaseId()!;

    this.problemService.updateTestCase(testCaseId, data).subscribe({
      next: () => {
        this.editingTestCaseId.set(null);
        this.editTestCaseForm.reset({ isPublic: true });
        this.refreshTestCases();
        this.successMessage.set('Test case updated successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: unknown) => {
        this.showEditError(error, 'Failed to update test case');
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
      error: (error: unknown) => {
        this.showEditError(error, 'Failed to reorder test cases');
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
      error: (error: unknown) => {
        this.showEditError(error, 'Failed to reorder test cases');
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
      error: (error: unknown) => {
        this.showEditError(error, 'Failed to add starter code');
      }
    });
  }

  async deleteStarterCode(starterCodeId: string): Promise<void> {
    const { confirmed } = await this.dialogService.confirm({
      title: 'Delete starter code?',
      message: 'Are you sure you want to delete this starter code?',
      confirmText: 'Delete',
      cancelText: 'Cancel',
      type: 'warning'
    });
    if (!confirmed) {
      return;
    }

    this.problemService.deleteStarterCode(starterCodeId).subscribe({
      next: () => {
        this.refreshStarterCodes();
        this.successMessage.set('Starter code deleted successfully');
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (error: unknown) => {
        this.showEditError(error, 'Failed to delete starter code');
      }
    });
  }

  /**
   * Editing replaces the starter code (delete, then add). The API refuses to delete a problem's
   * last starter code, so the only language can't be edited until another one is added.
   */
  canEditStarterCode(): boolean {
    return !this.isLocked() && this.starterCodes().length > 1;
  }

  startEditingStarterCode(starterCode: StarterCodeResponse): void {
    if (!this.canEditStarterCode()) return;
    this.editingStarterCodeId.set(starterCode.id);
    this.editStarterCodeControl.setValue(starterCode.code);
  }

  cancelEditingStarterCode(): void {
    this.editingStarterCodeId.set(null);
  }

  saveStarterCodeEdit(): void {
    const existing = this.starterCodes().find(sc => sc.id === this.editingStarterCodeId());
    if (!existing || !this.problemId || this.isSavingStarterCode()) return;
    if (this.editStarterCodeControl.invalid) {
      this.editStarterCodeControl.markAsTouched();
      return;
    }

    this.errorMessage.set('');
    this.isSavingStarterCode.set(true);
    this.problemService.replaceStarterCode(this.problemId, existing, this.editStarterCodeControl.value).subscribe({
      next: () => {
        this.isSavingStarterCode.set(false);
        this.editingStarterCodeId.set(null);
        this.refreshStarterCodes();
        this.flash('Starter code updated');
      },
      error: (error: unknown) => {
        this.isSavingStarterCode.set(false);
        // The original was restored if the add failed; reload to show what the server has
        this.refreshStarterCodes();
        this.showEditError(error, 'Failed to update starter code');
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
