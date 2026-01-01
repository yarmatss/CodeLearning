import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Chapter, CreateChapterRequest, Subchapter, CreateSubchapterRequest } from '../models/course.model';

@Injectable({
  providedIn: 'root'
})
export class ChapterService {
  private readonly http = inject(HttpClient);

  // Chapters
  getCourseChapters(courseId: string): Observable<Chapter[]> {
    return this.http.get<Chapter[]>(`/api/courses/${courseId}/chapters`);
  }

  addChapter(courseId: string, data: CreateChapterRequest): Observable<Chapter> {
    return this.http.post<Chapter>(`/api/courses/${courseId}/chapters`, data);
  }

  updateChapterOrder(chapterId: string, newOrderIndex: number): Observable<Chapter> {
    return this.http.patch<Chapter>(`/api/courses/chapters/${chapterId}/order`, newOrderIndex);
  }

  deleteChapter(chapterId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`/api/courses/chapters/${chapterId}`);
  }

  // Subchapters
  getChapterSubchapters(chapterId: string): Observable<Subchapter[]> {
    return this.http.get<Subchapter[]>(`/api/chapters/${chapterId}/subchapters`);
  }

  addSubchapter(chapterId: string, data: CreateSubchapterRequest): Observable<Subchapter> {
    return this.http.post<Subchapter>(`/api/chapters/${chapterId}/subchapters`, data);
  }

  updateSubchapterOrder(subchapterId: string, newOrderIndex: number): Observable<Subchapter> {
    return this.http.patch<Subchapter>(`/api/chapters/subchapters/${subchapterId}/order`, newOrderIndex);
  }

  deleteSubchapter(subchapterId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`/api/chapters/subchapters/${subchapterId}`);
  }
}
