import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Submission, SubmitCodeRequest } from '../models/submission.model';

@Injectable({
  providedIn: 'root'
})
export class SubmissionService {
  private readonly http = inject(HttpClient);
  private readonly API_URL = '/api/submissions';

  submit(request: SubmitCodeRequest): Observable<Submission> {
    return this.http.post<Submission>(this.API_URL, request);
  }

  getById(submissionId: string): Observable<Submission> {
    return this.http.get<Submission>(`${this.API_URL}/${submissionId}`);
  }

  getByProblem(problemId: string): Observable<Submission[]> {
    return this.http.get<Submission[]>(`${this.API_URL}/problem/${problemId}`);
  }
}
