import { HttpParams } from '@angular/common/http';

/** One page of a list endpoint (`PagedResult<T>` on the API). */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface PageRequest {
  page?: number;
  pageSize?: number;
}

/** The API's defaults and limit; out-of-range values are clamped by the server. */
export const DEFAULT_PAGE_SIZE = 20;
export const MAX_PAGE_SIZE = 100;

/**
 * Builds query params from a page request plus optional filters, leaving out empty values so the
 * server's defaults apply.
 */
export function toQueryParams(query: object = {}): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') {
      continue;
    }
    params = params.set(key, String(value));
  }
  return params;
}

/** An empty page, for initial state and failed loads. */
export function emptyPage<T>(pageSize = DEFAULT_PAGE_SIZE): PagedResult<T> {
  return { items: [], page: 1, pageSize, totalCount: 0, totalPages: 0 };
}
