import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Block, BlockType } from '../models/course.model';

export interface CreateTheoryBlockRequest {
  title: string;
  content: string;
}

export interface UpdateTheoryBlockRequest {
  title: string;
  content: string;
}

export interface CreateVideoBlockRequest {
  title: string;
  videoUrl: string;
}

export interface UpdateVideoBlockRequest {
  title: string;
  videoUrl: string;
}

export interface CreateQuizBlockRequest {
  title: string;
  questions: CreateQuizQuestionRequest[];
}

export interface UpdateQuizBlockRequest {
  title: string;
  questions: CreateQuizQuestionRequest[];
}

export interface CreateQuizQuestionRequest {
  questionText: string;
  type: string; // "SingleChoice" | "MultipleChoice" | "TrueFalse"
  points: number;
  answers: CreateQuizAnswerRequest[];
}

export interface CreateQuizAnswerRequest {
  answerText: string;
  isCorrect: boolean;
}

export interface CreateProblemBlockRequest {
  title: string;
  problemId: string;
}

export interface UpdateProblemBlockRequest {
  title: string;
  problemId: string;
}

export interface UpdateBlockOrderRequest {
  newOrderIndex: number;
}

@Injectable({
  providedIn: 'root'
})
export class BlockService {
  private readonly http = inject(HttpClient);

  getSubchapterBlocks(subchapterId: string): Observable<Block[]> {
    return this.http.get<Block[]>(`/api/subchapters/${subchapterId}/blocks`);
  }

  getBlock(blockId: string): Observable<Block> {
    return this.http.get<Block>(`/api/blocks/${blockId}`);
  }

  // Theory Block
  addTheoryBlock(subchapterId: string, data: CreateTheoryBlockRequest): Observable<Block> {
    return this.http.post<Block>(`/api/subchapters/${subchapterId}/blocks/theory`, data);
  }

  updateTheoryBlock(subchapterId: string, blockId: string, data: UpdateTheoryBlockRequest): Observable<Block> {
    return this.http.put<Block>(`/api/subchapters/${subchapterId}/blocks/${blockId}/theory`, data);
  }

  // Video Block
  addVideoBlock(subchapterId: string, data: CreateVideoBlockRequest): Observable<Block> {
    return this.http.post<Block>(`/api/subchapters/${subchapterId}/blocks/video`, data);
  }

  updateVideoBlock(subchapterId: string, blockId: string, data: UpdateVideoBlockRequest): Observable<Block> {
    return this.http.put<Block>(`/api/subchapters/${subchapterId}/blocks/${blockId}/video`, data);
  }

  // Quiz Block
  addQuizBlock(subchapterId: string, data: CreateQuizBlockRequest): Observable<Block> {
    return this.http.post<Block>(`/api/subchapters/${subchapterId}/blocks/quiz`, data);
  }

  updateQuizBlock(subchapterId: string, blockId: string, data: UpdateQuizBlockRequest): Observable<Block> {
    return this.http.put<Block>(`/api/subchapters/${subchapterId}/blocks/${blockId}/quiz`, data);
  }

  // Problem Block
  addProblemBlock(subchapterId: string, data: CreateProblemBlockRequest): Observable<Block> {
    return this.http.post<Block>(`/api/subchapters/${subchapterId}/blocks/problem`, data);
  }

  updateProblemBlock(subchapterId: string, blockId: string, data: UpdateProblemBlockRequest): Observable<Block> {
    return this.http.put<Block>(`/api/subchapters/${subchapterId}/blocks/${blockId}/problem`, data);
  }

  // Common operations
  updateBlockOrder(subchapterId: string, blockId: string, newOrderIndex: number): Observable<Block> {
    return this.http.patch<Block>(
      `/api/subchapters/${subchapterId}/blocks/${blockId}/order`,
      { newOrderIndex }
    );
  }

  deleteBlock(subchapterId: string, blockId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`/api/subchapters/${subchapterId}/blocks/${blockId}`);
  }
}
