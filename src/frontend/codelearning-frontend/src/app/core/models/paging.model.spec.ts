import { emptyPage, toQueryParams } from './paging.model';

describe('toQueryParams', () => {
  it('keeps set values and drops empty ones', () => {
    const params = toQueryParams({ page: 2, pageSize: 20, search: '', tagId: undefined, difficulty: null });
    expect(params.keys().sort()).toEqual(['page', 'pageSize']);
    expect(params.get('page')).toBe('2');
  });

  it('returns no params for an empty query', () => {
    expect(toQueryParams().keys()).toEqual([]);
  });
});

describe('emptyPage', () => {
  it('has no items and no pages', () => {
    expect(emptyPage(10)).toEqual({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 });
  });
});
