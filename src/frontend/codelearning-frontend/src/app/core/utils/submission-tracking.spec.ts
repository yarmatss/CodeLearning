import { Observable, Subject, of } from 'rxjs';
import { Submission, SubmissionStatus } from '../models/submission.model';
import { trackSubmission } from './submission-tracking';

const SILENCE_CHECK_MS = 60_000;
const TIMEOUT_MS = 180_000;

function submission(status: SubmissionStatus): Submission {
  return { id: 's1', problemId: 'p1', status, score: status === SubmissionStatus.Completed ? 100 : 0 } as Submission;
}

describe('trackSubmission', () => {
  let serverStatus: SubmissionStatus;
  let fetches: number;
  let statusChanged$: Subject<void>;
  let reconnected$: Subject<void>;
  let seen: SubmissionStatus[];
  let completed: boolean;

  const fetch = (): Observable<Submission> => {
    fetches++;
    return of(submission(serverStatus));
  };

  function start(fetchFn: () => Observable<Submission> = fetch): void {
    trackSubmission({
      fetch: fetchFn,
      statusChanged$,
      reconnected$,
      silenceCheckMs: SILENCE_CHECK_MS,
      timeoutMs: TIMEOUT_MS
    }).subscribe({
      next: s => seen.push(s.status),
      complete: () => (completed = true)
    });
    // The first read is scheduled as timer(0)
    vi.advanceTimersByTime(0);
  }

  beforeEach(() => {
    vi.useFakeTimers();
    serverStatus = SubmissionStatus.Pending;
    fetches = 0;
    statusChanged$ = new Subject<void>();
    reconnected$ = new Subject<void>();
    seen = [];
    completed = false;
  });

  afterEach(() => vi.useRealTimers());

  it('reads the submission once at the start', () => {
    start();

    expect(fetches).toBe(1);
    expect(seen).toEqual([SubmissionStatus.Pending]);
    expect(completed).toBe(false);
  });

  it('finishes at once when the result was already in', () => {
    serverStatus = SubmissionStatus.Completed;

    start();

    expect(seen).toEqual([SubmissionStatus.Completed]);
    expect(completed).toBe(true);
  });

  it('does not poll while the server is silent', () => {
    start();

    vi.advanceTimersByTime(SILENCE_CHECK_MS - 1);

    expect(fetches).toBe(1);
  });

  it('reads again on each pushed status change and stops at the final one', () => {
    start();

    serverStatus = SubmissionStatus.Running;
    statusChanged$.next();
    vi.advanceTimersByTime(0);

    serverStatus = SubmissionStatus.Completed;
    statusChanged$.next();
    vi.advanceTimersByTime(0);

    expect(seen).toEqual([SubmissionStatus.Pending, SubmissionStatus.Running, SubmissionStatus.Completed]);
    expect(completed).toBe(true);
    expect(statusChanged$.observed).toBe(false);
  });

  it('reads again after a reconnect, when events may have been missed', () => {
    start();

    serverStatus = SubmissionStatus.RuntimeError;
    reconnected$.next();
    vi.advanceTimersByTime(0);

    expect(seen).toEqual([SubmissionStatus.Pending, SubmissionStatus.RuntimeError]);
    expect(completed).toBe(true);
  });

  it('reads again after a long silence, in case an event never came', () => {
    start();
    serverStatus = SubmissionStatus.Completed;

    vi.advanceTimersByTime(SILENCE_CHECK_MS);

    expect(fetches).toBe(2);
    expect(seen).toEqual([SubmissionStatus.Pending, SubmissionStatus.Completed]);
    expect(completed).toBe(true);
  });

  it('restarts the silence window on every pushed change', () => {
    start();

    vi.advanceTimersByTime(SILENCE_CHECK_MS - 1000);
    serverStatus = SubmissionStatus.Running;
    statusChanged$.next();
    vi.advanceTimersByTime(0);
    expect(fetches).toBe(2);

    vi.advanceTimersByTime(SILENCE_CHECK_MS - 1);
    expect(fetches).toBe(2);

    vi.advanceTimersByTime(1);
    expect(fetches).toBe(3);
  });

  it('discards a read that was in flight when a change was pushed', () => {
    const responses: Subject<Submission>[] = [];
    start(() => {
      const response = new Subject<Submission>();
      responses.push(response);
      return response;
    });

    // The first read is still unanswered when the final status is pushed
    statusChanged$.next();
    vi.advanceTimersByTime(0);
    expect(responses).toHaveLength(2);

    // Its answer predates the change and must not be shown
    responses[0].next(submission(SubmissionStatus.Running));
    expect(seen).toEqual([]);

    responses[1].next(submission(SubmissionStatus.Completed));
    expect(seen).toEqual([SubmissionStatus.Completed]);
    expect(completed).toBe(true);
  });

  it('gives up after the timeout without a final status', () => {
    start();

    vi.advanceTimersByTime(TIMEOUT_MS);

    expect(completed).toBe(true);
    expect(seen.every(status => status === SubmissionStatus.Pending)).toBe(true);
    expect(statusChanged$.observed).toBe(false);
  });
});
