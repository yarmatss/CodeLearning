import { HttpErrorResponse } from '@angular/common/http';

/**
 * Turns an HTTP error into one readable message.
 *
 * The API answers with ProblemDetails (`title`, `detail` and, for validation failures, `errors`
 * keyed by field path) or with a plain `{ message }` body. Validation errors are flattened into
 * "Field: message" lines so forms can show every reason at once.
 */
export function extractApiError(error: unknown, fallback = 'An error occurred'): string {
  if (!(error instanceof HttpErrorResponse)) {
    return error instanceof Error && error.message ? error.message : fallback;
  }

  if (error.status === 0) {
    return 'Cannot reach the server. Check your connection and try again.';
  }

  const body = error.error;
  if (body && typeof body === 'object') {
    const errors = (body as { errors?: unknown }).errors;
    if (errors && typeof errors === 'object') {
      const lines: string[] = [];
      for (const [key, value] of Object.entries(errors as Record<string, unknown>)) {
        const messages = Array.isArray(value) ? value : [value];
        const field = formatFieldPath(key);
        for (const message of messages) {
          if (typeof message === 'string' && message) {
            lines.push(field ? `${field}: ${message}` : message);
          }
        }
      }
      if (lines.length > 0) {
        return lines.join('\n');
      }
    }

    const { detail, message, title } = body as { detail?: unknown; message?: unknown; title?: unknown };
    for (const candidate of [detail, message, title]) {
      if (typeof candidate === 'string' && candidate) {
        return candidate;
      }
    }
  }

  if (typeof body === 'string' && body) {
    return body;
  }

  return fallback;
}

/** The HTTP status of a failed request, or undefined when the error is not an HTTP response. */
export function httpStatus(error: unknown): number | undefined {
  return error instanceof HttpErrorResponse ? error.status : undefined;
}

/**
 * Whether the server refused to show content to this user: 401 (not signed in) or 403 (e.g. not
 * enrolled, or the course is unpublished). Pages show an "enroll to view" prompt for these
 * instead of a generic error.
 */
export function isAccessDenied(error: unknown): boolean {
  const status = httpStatus(error);
  return status === 401 || status === 403;
}

/** Whether an edit was refused because a published course uses the problem (409). */
export function isConflict(error: unknown): boolean {
  return httpStatus(error) === 409;
}

/**
 * Makes a validation key such as `Questions[0].Answers[1].AnswerText` readable
 * ("Question 1, Answer 2, AnswerText"). Keys without indices are returned unchanged.
 */
export function formatFieldPath(path: string): string {
  if (!path || path === '$') {
    return '';
  }

  const parts: string[] = [];
  const indexPattern = /([A-Za-z]+)\[(\d+)\]/g;
  let match: RegExpExecArray | null;
  while ((match = indexPattern.exec(path)) !== null) {
    const name = match[1].replace(/s$/i, '');
    parts.push(`${name.charAt(0).toUpperCase()}${name.slice(1)} ${Number(match[2]) + 1}`);
  }

  const last = path.split('.').pop() ?? path;
  if (!/\[\d+\]$/.test(last)) {
    parts.push(last);
  }

  return parts.join(', ');
}
