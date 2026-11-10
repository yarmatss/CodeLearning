import { Injectable, InjectionToken, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import type { HubConnection } from '@microsoft/signalr';
import { Observable, Subject, firstValueFrom } from 'rxjs';
import { SubmissionStatus } from '../models/submission.model';

/** Mirrors CodeLearning.Api.Realtime.SubmissionStatusNotification. */
export interface SubmissionStatusEvent {
  submissionId: string;
  status: SubmissionStatus;
}

const HUB_URL = '/api/hubs/submissions';
const STATUS_CHANGED = 'SubmissionStatusChanged';

// HubConnectionState values, compared as text so the SignalR library is needed only as a type here
const LIVE_STATES: readonly string[] = ['Connected', 'Reconnecting'];

/**
 * Builds the hub connection. The access token travels in its HttpOnly cookie, as for the REST API,
 * so no token is handed to SignalR (and none ends up in a WebSocket query string).
 *
 * The library is imported on first use: this service is reachable from AuthService, and a static
 * import would put SignalR in the initial bundle of every page.
 */
export const SUBMISSION_HUB_CONNECTION = new InjectionToken<() => Promise<HubConnection>>('SUBMISSION_HUB_CONNECTION', {
  providedIn: 'root',
  factory: () => async () => {
    const { HubConnectionBuilder, LogLevel } = await import('@microsoft/signalr');

    return new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
  }
});

/**
 * Server push for submission statuses. An event only says that a submission changed; whoever is
 * interested fetches the submission itself. Events are not queued for a client that is offline, so
 * consumers must also fetch after `reconnected$` (see `trackSubmission`).
 */
@Injectable({
  providedIn: 'root'
})
export class SubmissionEventsService {
  private readonly http = inject(HttpClient);
  private readonly createConnection = inject(SUBMISSION_HUB_CONNECTION);

  private connection: HubConnection | null = null;
  private starting: Promise<void> | null = null;

  private readonly statusChangedSubject = new Subject<SubmissionStatusEvent>();
  private readonly reconnectedSubject = new Subject<void>();

  readonly statusChanged$: Observable<SubmissionStatusEvent> = this.statusChangedSubject.asObservable();

  /** Emits when the connection is (back) up after having been down, i.e. events may have been missed. */
  readonly reconnected$: Observable<void> = this.reconnectedSubject.asObservable();

  /**
   * Opens the connection unless it is already open or opening. Never rejects: push is an
   * optimisation, and callers keep working from their own fetches when it is unavailable.
   */
  connect(): Promise<void> {
    if (this.connection && LIVE_STATES.includes(this.connection.state)) {
      return Promise.resolve();
    }

    this.starting ??= this.start().finally(() => {
      this.starting = null;
    });
    return this.starting;
  }

  /** Closes the connection, e.g. on logout: it was authenticated as the user who is leaving. */
  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    try {
      await connection?.stop();
    } catch {
      // Already closed
    }
  }

  private async start(): Promise<void> {
    // An existing connection here is one that was open before and has closed
    const wasDown = this.connection !== null;
    let connection: HubConnection;

    try {
      connection = this.connection ?? this.register(await this.createConnection());
      this.connection = connection;
      await this.startWithFreshSession(connection);
    } catch {
      // Left disconnected; the next connect() tries again
      return;
    }

    // Abandoned by a logout while it was starting
    if (this.connection !== connection) {
      await connection.stop().catch(() => undefined);
      return;
    }

    if (wasDown) {
      this.reconnectedSubject.next();
    }
  }

  // The handshake is not an Angular HTTP request, so the interceptor that refreshes an expired access
  // token does not see its 401. One request through HttpClient lets it refresh, then the start is retried
  private async startWithFreshSession(connection: HubConnection): Promise<void> {
    try {
      await connection.start();
    } catch {
      await firstValueFrom(this.http.get('/api/auth/me'));
      await connection.start();
    }
  }

  private register(connection: HubConnection): HubConnection {
    connection.on(STATUS_CHANGED, (event: SubmissionStatusEvent) => this.statusChangedSubject.next(event));
    connection.onreconnected(() => this.reconnectedSubject.next());
    return connection;
  }
}
