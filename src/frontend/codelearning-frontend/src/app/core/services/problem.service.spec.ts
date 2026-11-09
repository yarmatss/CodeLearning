import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProblemService, StarterCodeResponse } from './problem.service';

describe('ProblemService', () => {
  let service: ProblemService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ProblemService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('sends paging and only the filters that are set', () => {
    service.getProblems({ page: 3, pageSize: 20, difficulty: 'Easy', search: '', tagId: undefined }).subscribe();
    const req = backend.expectOne(r => r.url === '/api/problems');
    expect(req.request.params.keys().sort()).toEqual(['difficulty', 'page', 'pageSize']);
    expect(req.request.params.get('difficulty')).toBe('Easy');
    req.flush({ items: [], page: 3, pageSize: 20, totalCount: 0, totalPages: 0 });
  });

  it('pages the teacher\'s own problems', () => {
    service.getMyProblems({ page: 1, pageSize: 100 }).subscribe();
    const req = backend.expectOne(r => r.url === '/api/problems/my');
    expect(req.request.params.get('pageSize')).toBe('100');
    req.flush({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });
  });

  describe('replaceStarterCode', () => {
    const existing: StarterCodeResponse = { id: 'sc1', code: 'old', languageId: 'py', languageName: 'Python' };

    it('deletes the starter code and adds the new code for the same language', () => {
      let result: StarterCodeResponse | undefined;
      service.replaceStarterCode('p1', existing, 'new').subscribe(r => (result = r));

      backend.expectOne(r => r.method === 'DELETE' && r.url === '/api/problems/startercodes/sc1').flush({ message: 'ok' });
      const add = backend.expectOne(r => r.method === 'POST' && r.url === '/api/problems/p1/startercodes');
      expect(add.request.body).toEqual({ languageId: 'py', code: 'new' });
      add.flush({ ...existing, id: 'sc2', code: 'new' });

      expect(result?.id).toBe('sc2');
    });

    it('restores the original code and rethrows when the add fails', () => {
      let status = 0;
      service.replaceStarterCode('p1', existing, 'new').subscribe({ error: e => (status = e.status) });

      backend.expectOne(r => r.method === 'DELETE').flush({ message: 'ok' });
      backend.expectOne(r => r.method === 'POST').flush({ detail: 'Too big' }, { status: 400, statusText: 'Bad Request' });
      const restore = backend.expectOne(r => r.method === 'POST');
      expect(restore.request.body).toEqual({ languageId: 'py', code: 'old' });
      restore.flush(existing);

      expect(status).toBe(400);
    });

    it('stops when the delete is refused (e.g. 409 while locked)', () => {
      let status = 0;
      service.replaceStarterCode('p1', existing, 'new').subscribe({ error: e => (status = e.status) });
      backend.expectOne(r => r.method === 'DELETE').flush({ detail: 'Locked' }, { status: 409, statusText: 'Conflict' });
      expect(status).toBe(409);
    });
  });
});
