import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CourseService } from './course.service';
import { ChapterService } from './chapter.service';
import { Course, CourseStatus } from '../models/course.model';
import { PagedResult } from '../models/paging.model';

const course = (id: string, status = CourseStatus.Draft): Course => ({
  id,
  title: `Course ${id}`,
  description: 'Description',
  status,
  instructorId: 't1',
  instructorName: 'Teacher',
  createdAt: new Date(),
  chaptersCount: 0,
  totalBlocks: 0
});

describe('CourseService', () => {
  let service: CourseService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CourseService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('requests a page of published courses and caches its items', () => {
    let result: PagedResult<Course> | undefined;
    service.getPublishedCourses({ page: 2, pageSize: 12 }).subscribe(page => (result = page));

    const req = backend.expectOne(r => r.url === '/api/courses/published');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('12');
    req.flush({ items: [course('a')], page: 2, pageSize: 12, totalCount: 13, totalPages: 2 });

    expect(result?.totalPages).toBe(2);
    expect(service.courses().map(c => c.id)).toEqual(['a']);
  });

  it('sends no paging params when none are given, so server defaults apply', () => {
    service.getEnrolledCourses().subscribe();
    const req = backend.expectOne(r => r.url === '/api/enrollments/my-courses');
    expect(req.request.params.keys()).toEqual([]);
    req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
  });

  it('unpublishes a course and updates the cached copy', () => {
    service.courses.set([course('a', CourseStatus.Published)]);
    service.unpublishCourse('a').subscribe();

    const req = backend.expectOne('/api/courses/a/unpublish');
    expect(req.request.method).toBe('POST');
    req.flush({ message: 'ok', course: course('a', CourseStatus.Draft) });

    expect(service.courses()[0].status).toBe(CourseStatus.Draft);
  });

  it('loads teacher stats', () => {
    let published = -1;
    service.getMyStats().subscribe(stats => (published = stats.publishedCourses));
    backend.expectOne('/api/courses/my/stats').flush({ publishedCourses: 2, draftCourses: 1, totalStudents: 5 });
    expect(published).toBe(2);
  });
});

describe('ChapterService renames', () => {
  let service: ChapterService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ChapterService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('renames a chapter with PUT', () => {
    service.renameChapter('c1', 'ch1', { title: 'New' }).subscribe();
    const req = backend.expectOne('/api/courses/c1/chapters/ch1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ title: 'New' });
    req.flush({ id: 'ch1', title: 'New', orderIndex: 1, courseId: 'c1', subchaptersCount: 0 });
  });

  it('renames a subchapter with PUT', () => {
    service.renameSubchapter('ch1', 's1', { title: 'New' }).subscribe();
    const req = backend.expectOne('/api/chapters/ch1/subchapters/s1');
    expect(req.request.method).toBe('PUT');
    req.flush({ id: 's1', title: 'New', orderIndex: 1, chapterId: 'ch1', blocksCount: 0 });
  });
});
