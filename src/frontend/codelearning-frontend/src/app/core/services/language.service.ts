import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

export interface Language {
  id: string;
  name: string;
  version: string;
  fileExtension: string;
  isEnabled: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class LanguageService {
  private readonly http = inject(HttpClient);
  private readonly API_URL = '/api/languages';

  readonly languages = signal<Language[]>([]);

  getLanguages(): Observable<Language[]> {
    return this.http.get<Language[]>(this.API_URL).pipe(
      tap(languages => this.languages.set(languages))
    );
  }

  getLanguageName(id: string): string {
    const language = this.languages().find(l => l.id === id);
    return language?.name || 'Unknown';
  }
}
