import { Observable, merge, startWith, switchMap, takeUntil, takeWhile, timer } from 'rxjs';
import { FINAL_SUBMISSION_STATUSES, Submission } from '../models/submission.model';

export interface SubmissionTrackingOptions {
  /** Reads the submission from the server. */
  fetch: () => Observable<Submission>;
  /** Emits when the server says this submission changed. */
  statusChanged$: Observable<unknown>;
  /** Emits when the push connection is back after an outage, during which events were lost. */
  reconnected$: Observable<unknown>;
  /** With nothing heard for this long the submission is read anyway, in case an event never came. */
  silenceCheckMs: number;
  /** Tracking stops after this long even without a final status. */
  timeoutMs: number;
}

/**
 * Follows a submission until it has a final status, without polling: it is read once at the start
 * (the result may already be in), then whenever the server pushes a status change. Push is not
 * guaranteed delivery, so it is also read after a reconnect and after a long silence.
 *
 * Emits every state read; the last one is final unless tracking timed out, in which case the
 * stream just completes.
 */
export function trackSubmission(options: SubmissionTrackingOptions): Observable<Submission> {
  return merge(options.statusChanged$, options.reconnected$).pipe(
    startWith(undefined),
    // Each signal reads now and re-arms the silence check. switchMap also drops a read that was
    // in flight when the signal arrived: it may have been answered before the change
    switchMap(() => timer(0, options.silenceCheckMs).pipe(switchMap(() => options.fetch()))),
    takeWhile(submission => !FINAL_SUBMISSION_STATUSES.includes(submission.status), true),
    takeUntil(timer(options.timeoutMs))
  );
}
