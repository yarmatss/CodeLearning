import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface QuizSubmission {
  answers: QuizAnswerSubmission[];
}

export interface QuizAnswerSubmission {
  questionId: string;
  selectedAnswerIds: string[];
}

export interface QuizResult {
  attemptId: string;
  quizId: string;
  score: number;
  maxScore: number;
  percentage: number;
  attemptedAt: string;
  questionResults: QuizQuestionResult[];
}

export interface QuizQuestionResult {
  questionId: string;
  questionContent: string;
  questionType: string;
  isCorrect: boolean;
  points: number;
  selectedAnswerIds: string[];
  answers: QuizAnswerFeedback[];
  explanation?: string;
}

export interface QuizAnswerFeedback {
  answerId: string;
  text: string;
  isCorrect: boolean;
  wasSelected: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class QuizService {
  private readonly http = inject(HttpClient);
  private readonly API_URL = '/api/quizzes';

  submitQuiz(quizId: string, submission: QuizSubmission): Observable<QuizResult> {
    return this.http.post<QuizResult>(`${this.API_URL}/${quizId}/submit`, submission);
  }

  getQuizAttempt(quizId: string): Observable<QuizResult> {
    return this.http.get<QuizResult>(`${this.API_URL}/${quizId}/attempt`);
  }
}
