import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, switchMap, throwError } from 'rxjs';
import { PagedResult, PageRequest, toQueryParams } from '../models/paging.model';

export type ProblemDifficulty = 'Easy' | 'Medium' | 'Hard';

/** Row of GET /api/problems and /api/problems/my: counts instead of test cases and starter codes. */
export interface ProblemListItem {
  id: string;
  title: string;
  // At most 200 characters, ending with "..." when the full description is longer
  description: string;
  difficulty: ProblemDifficulty;
  authorId: string;
  authorName: string;
  testCasesCount: number;
  languagesCount: number;
  isUsedByPublishedCourse: boolean;
  tags: TagResponse[];
  createdAt: Date;
}

export interface ProblemListQuery extends PageRequest {
  difficulty?: ProblemDifficulty | '';
  tagId?: string;
  search?: string;
}

export interface ProblemResponse {
  id: string;
  title: string;
  description: string;
  difficulty: ProblemDifficulty;
  authorId: string;
  authorName?: string;
  createdAt: Date;
  // A published course uses the problem: every edit answers 409 until that course is unpublished
  isUsedByPublishedCourse: boolean;
  testCases: TestCaseResponse[];
  starterCodes: StarterCodeResponse[];
  tags: TagResponse[];
}

export interface TestCaseResponse {
  id: string;
  input: string;
  expectedOutput: string;
  isPublic: boolean;
  orderIndex: number;
}

export interface StarterCodeResponse {
  id: string;
  code: string;
  languageId: string;
  languageName: string;
}

export interface TagResponse {
  id: string;
  name: string;
}

export interface CreateProblemRequest {
  title: string;
  description: string;
  difficulty: ProblemDifficulty;
  testCases?: CreateTestCaseRequest[];
  starterCodes?: CreateStarterCodeRequest[];
  tagIds?: string[];
}

export interface UpdateProblemRequest {
  title: string;
  description: string;
  difficulty: ProblemDifficulty;
  tagIds?: string[];
}

export interface CreateTestCaseRequest {
  input: string;
  expectedOutput: string;
  isPublic: boolean;
}

export interface UpdateTestCaseRequest {
  input: string;
  expectedOutput: string;
  isPublic: boolean;
}

export interface BulkAddTestCasesRequest {
  testCases: CreateTestCaseRequest[];
}

export interface ReorderTestCasesRequest {
  testCaseIds: string[];
}

export interface CreateStarterCodeRequest {
  code: string;
  languageId: string;
}

@Injectable({
  providedIn: 'root'
})
export class ProblemService {
  private readonly http = inject(HttpClient);

  // Problem CRUD
  /** Problems the caller authored or that a published course they are enrolled in uses. */
  getProblems(query: ProblemListQuery = {}): Observable<PagedResult<ProblemListItem>> {
    return this.http.get<PagedResult<ProblemListItem>>('/api/problems', { params: toQueryParams(query) });
  }

  /** The teacher's own problems. */
  getMyProblems(query: ProblemListQuery = {}): Observable<PagedResult<ProblemListItem>> {
    return this.http.get<PagedResult<ProblemListItem>>('/api/problems/my', { params: toQueryParams(query) });
  }

  getProblem(id: string): Observable<ProblemResponse> {
    return this.http.get<ProblemResponse>(`/api/problems/${id}`);
  }

  createProblem(data: CreateProblemRequest): Observable<ProblemResponse> {
    return this.http.post<ProblemResponse>('/api/problems', data);
  }

  updateProblem(id: string, data: UpdateProblemRequest): Observable<ProblemResponse> {
    return this.http.put<ProblemResponse>(`/api/problems/${id}`, data);
  }

  deleteProblem(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`/api/problems/${id}`);
  }

  getTags(): Observable<TagResponse[]> {
    return this.http.get<TagResponse[]>('/api/problems/tags');
  }

  // Test Cases
  addTestCase(problemId: string, data: CreateTestCaseRequest): Observable<TestCaseResponse> {
    return this.http.post<TestCaseResponse>(`/api/problems/${problemId}/testcases`, data);
  }

  bulkAddTestCases(problemId: string, data: BulkAddTestCasesRequest): Observable<TestCaseResponse[]> {
    return this.http.post<TestCaseResponse[]>(`/api/problems/${problemId}/testcases/bulk`, data);
  }

  updateTestCase(testCaseId: string, data: UpdateTestCaseRequest): Observable<TestCaseResponse> {
    return this.http.put<TestCaseResponse>(`/api/problems/testcases/${testCaseId}`, data);
  }

  deleteTestCase(testCaseId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`/api/problems/testcases/${testCaseId}`);
  }

  reorderTestCases(problemId: string, data: ReorderTestCasesRequest): Observable<TestCaseResponse[]> {
    return this.http.put<TestCaseResponse[]>(`/api/problems/${problemId}/testcases/reorder`, data);
  }

  // Starter Codes
  addStarterCode(problemId: string, data: CreateStarterCodeRequest): Observable<StarterCodeResponse> {
    return this.http.post<StarterCodeResponse>(`/api/problems/${problemId}/startercodes`, data);
  }

  deleteStarterCode(starterCodeId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`/api/problems/startercodes/${starterCodeId}`);
  }

  /**
   * Replaces a starter code's code. The API has no update endpoint, so this deletes the starter
   * code and adds it again; if the add fails, the original code is put back and the add's error is
   * rethrown. Deleting the last starter code is refused by the API, so a problem's only language
   * cannot be edited this way.
   */
  replaceStarterCode(problemId: string, existing: StarterCodeResponse, code: string): Observable<StarterCodeResponse> {
    const add = (body: string) => this.addStarterCode(problemId, { languageId: existing.languageId, code: body });
    return this.deleteStarterCode(existing.id).pipe(
      switchMap(() =>
        add(code).pipe(
          catchError((error: unknown) =>
            add(existing.code).pipe(
              catchError(() => throwError(() => error)),
              switchMap(() => throwError(() => error))
            )
          )
        )
      )
    );
  }
}
