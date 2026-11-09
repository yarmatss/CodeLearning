import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Block } from '../models/course.model';

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

/** Body of 201 Created from every block create endpoint; the block itself is not returned. */
export interface BlockCreatedResponse {
  id: string;
  message: string;
}

/** Body of 200 OK from block update, reorder and delete endpoints. */
export interface MessageResponse {
  message: string;
}

export interface CompleteBlockResponse {
  blockId: string;
  completedAt: string;
  nextBlockId?: string | null;
  message: string;
  courseCompleted: boolean;
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
  addTheoryBlock(subchapterId: string, data: CreateTheoryBlockRequest): Observable<BlockCreatedResponse> {
    return this.http.post<BlockCreatedResponse>(`/api/subchapters/${subchapterId}/blocks/theory`, data);
  }

  updateTheoryBlock(subchapterId: string, blockId: string, data: UpdateTheoryBlockRequest): Observable<MessageResponse> {
    return this.http.put<MessageResponse>(`/api/subchapters/${subchapterId}/blocks/${blockId}/theory`, data);
  }

  // Video Block
  addVideoBlock(subchapterId: string, data: CreateVideoBlockRequest): Observable<BlockCreatedResponse> {
    return this.http.post<BlockCreatedResponse>(`/api/subchapters/${subchapterId}/blocks/video`, data);
  }

  updateVideoBlock(subchapterId: string, blockId: string, data: UpdateVideoBlockRequest): Observable<MessageResponse> {
    return this.http.put<MessageResponse>(`/api/subchapters/${subchapterId}/blocks/${blockId}/video`, data);
  }

  // Quiz Block
  addQuizBlock(subchapterId: string, data: CreateQuizBlockRequest): Observable<BlockCreatedResponse> {
    return this.http.post<BlockCreatedResponse>(`/api/subchapters/${subchapterId}/blocks/quiz`, data);
  }

  updateQuizBlock(subchapterId: string, blockId: string, data: UpdateQuizBlockRequest): Observable<MessageResponse> {
    return this.http.put<MessageResponse>(`/api/subchapters/${subchapterId}/blocks/${blockId}/quiz`, data);
  }

  // Problem Block
  addProblemBlock(subchapterId: string, data: CreateProblemBlockRequest): Observable<BlockCreatedResponse> {
    return this.http.post<BlockCreatedResponse>(`/api/subchapters/${subchapterId}/blocks/problem`, data);
  }

  updateProblemBlock(subchapterId: string, blockId: string, data: UpdateProblemBlockRequest): Observable<MessageResponse> {
    return this.http.put<MessageResponse>(`/api/subchapters/${subchapterId}/blocks/${blockId}/problem`, data);
  }

  // Common operations
  updateBlockOrder(subchapterId: string, blockId: string, newOrderIndex: number): Observable<MessageResponse> {
    return this.http.patch<MessageResponse>(
      `/api/subchapters/${subchapterId}/blocks/${blockId}/order`,
      { newOrderIndex }
    );
  }

  deleteBlock(subchapterId: string, blockId: string): Observable<MessageResponse> {
    return this.http.delete<MessageResponse>(`/api/subchapters/${subchapterId}/blocks/${blockId}`);
  }

  // Progress tracking
  completeBlock(blockId: string): Observable<CompleteBlockResponse> {
    return this.http.post<CompleteBlockResponse>(`/api/progress/blocks/${blockId}/complete`, {});
  }
}
