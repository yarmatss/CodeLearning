import { HttpErrorResponse } from '@angular/common/http';
import { extractApiError, httpStatus, isAccessDenied, isConflict } from './api-error';

describe('extractApiError', () => {
  it('flattens ProblemDetails validation errors', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: {
        title: 'Validation failed',
        errors: {
          'Questions[0].Answers[1].AnswerText': ['Answer text is required'],
          Title: ['Title is required']
        }
      }
    });

    expect(extractApiError(error)).toBe(
      'Question 1, Answer 2, AnswerText: Answer text is required\nTitle: Title is required'
    );
  });

  it('prefers detail, then message', () => {
    expect(extractApiError(new HttpErrorResponse({ status: 409, error: { detail: 'Locked' } }))).toBe('Locked');
    expect(extractApiError(new HttpErrorResponse({ status: 400, error: { message: 'Bad' } }))).toBe('Bad');
  });

  it('falls back when the body says nothing', () => {
    expect(extractApiError(new HttpErrorResponse({ status: 500 }), 'Failed')).toBe('Failed');
  });
});

describe('HTTP status helpers', () => {
  it('treats 401 and 403 as access denied', () => {
    expect(isAccessDenied(new HttpErrorResponse({ status: 401 }))).toBe(true);
    expect(isAccessDenied(new HttpErrorResponse({ status: 403 }))).toBe(true);
    expect(isAccessDenied(new HttpErrorResponse({ status: 404 }))).toBe(false);
    expect(isAccessDenied(new Error('boom'))).toBe(false);
  });

  it('recognises a 409 conflict', () => {
    expect(isConflict(new HttpErrorResponse({ status: 409 }))).toBe(true);
    expect(isConflict(new HttpErrorResponse({ status: 400 }))).toBe(false);
  });

  it('returns undefined status for non-HTTP errors', () => {
    expect(httpStatus('nope')).toBeUndefined();
    expect(httpStatus(new HttpErrorResponse({ status: 500 }))).toBe(500);
  });
});
