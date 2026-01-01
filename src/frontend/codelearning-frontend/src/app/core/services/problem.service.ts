import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ProblemResponse {
  id: string;
  title: string;
  description: string;
  difficulty: 'Easy' | 'Medium' | 'Hard';
  authorId: string;
  authorName: string;
  testCasesCount: number;
  starterCodesCount: number;
  tags: ProblemTag[];
}

export interface ProblemTag {
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

export interface CreateTestCaseRequest {
  input: string;
  expectedOutput: string;
  isPublic: boolean;
}

export interface CreateStarterCodeRequest {
  languageId: string;
  code: string;
}

@Injectable({
  providedIn: 'root'
})
export class ProblemService {
  private readonly http = inject(HttpClient);

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

  updateProblem(id: string, data: CreateProblemRequest): Observable<ProblemResponse> {
    return this.http.put<ProblemResponse>(`/api/problems/${id}`, data);
  }

  deleteProblem(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`/api/problems/${id}`);
  }
}
