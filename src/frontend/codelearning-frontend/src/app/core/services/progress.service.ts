import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CourseProgress } from '../models/progress.model';

@Injectable({
  providedIn: 'root'
})
export class ProgressService {
  private readonly http = inject(HttpClient);
  private readonly API_URL = '/api/progress';

  getCourseProgress(courseId: string): Observable<CourseProgress> {
    return this.http.get<CourseProgress>(`${this.API_URL}/courses/${courseId}`);
  }
}
