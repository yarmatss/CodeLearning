import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ProblemResponse {
  id: string;
  title: string;
  description: string;
  difficulty: 'Easy' | 'Medium' | 'Hard';
  authorId: string;
  authorName?: string;
  createdAt: Date;
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
  difficulty: 'Easy' | 'Medium' | 'Hard';
  testCases?: CreateTestCaseRequest[];
  starterCodes?: CreateStarterCodeRequest[];
  tagIds?: string[];
}

export interface UpdateProblemRequest {
  title: string;
  description: string;
  difficulty: 'Easy' | 'Medium' | 'Hard';
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
  getProblems(difficulty?: string, tagId?: string, search?: string): Observable<ProblemResponse[]> {
    let params: any = {};
    if (difficulty) params.difficulty = difficulty;
    if (tagId) params.tagId = tagId;
    if (search) params.search = search;
    
    return this.http.get<ProblemResponse[]>('/api/problems', { params });
  }

  getMyProblems(): Observable<ProblemResponse[]> {
    return this.http.get<ProblemResponse[]>('/api/problems/my');
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
}
