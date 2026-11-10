import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { SubmissionStatus } from '../models/submission.model';
import {
  SUBMISSION_HUB_CONNECTION,
  SubmissionEventsService,
  SubmissionStatusEvent
} from './submission-events.service';

/** The part of HubConnection the service uses, driven by the test. */
class FakeHubConnection {
  state = HubConnectionState.Disconnected;
  starts = 0;
  stops = 0;

  private readonly handlers = new Map<string, (payload: unknown) => void>();
  private reconnected: () => void = () => undefined;

  constructor(private startFailures: number) {}

  start(): Promise<void> {
    this.starts++;
    if (this.startFailures > 0) {
      this.startFailures--;
      return Promise.reject(new Error('401'));
    }
    this.state = HubConnectionState.Connected;
    return Promise.resolve();
  }

  stop(): Promise<void> {
    this.stops++;
    this.state = HubConnectionState.Disconnected;
    return Promise.resolve();
  }

  on(method: string, handler: (payload: unknown) => void): void {
    this.handlers.set(method, handler);
  }

  onreconnected(handler: () => void): void {
    this.reconnected = handler;
  }

  push(method: string, payload: unknown): void {
    this.handlers.get(method)?.(payload);
  }

  dropAndReconnect(): void {
    this.reconnected();
  }

  drop(): void {
    this.state = HubConnectionState.Disconnected;
  }
}

describe('SubmissionEventsService', () => {
  let service: SubmissionEventsService;
  let backend: HttpTestingController;
  let connections: FakeHubConnection[];
  // How many times the next connection refuses to start
  let startFailures: number;

  beforeEach(() => {
    connections = [];
    startFailures = 0;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: SUBMISSION_HUB_CONNECTION,
          useValue: () => {
            const connection = new FakeHubConnection(startFailures);
            connections.push(connection);
            return Promise.resolve(connection as unknown as HubConnection);
          }
        }
      ]
    });
    service = TestBed.inject(SubmissionEventsService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('does not connect until asked', () => {
    expect(connections).toHaveLength(0);
  });

  it('opens one connection however often connect is called', async () => {
    await Promise.all([service.connect(), service.connect()]);
    await service.connect();

    expect(connections).toHaveLength(1);
    expect(connections[0].starts).toBe(1);
  });

  it('passes pushed status changes on', async () => {
    const received: SubmissionStatusEvent[] = [];
    service.statusChanged$.subscribe(event => received.push(event));
    await service.connect();

    connections[0].push('SubmissionStatusChanged', { submissionId: 's1', status: SubmissionStatus.Completed });

    expect(received).toEqual([{ submissionId: 's1', status: SubmissionStatus.Completed }]);
  });

  it('announces an automatic reconnect', async () => {
    let reconnects = 0;
    service.reconnected$.subscribe(() => reconnects++);
    await service.connect();
    expect(reconnects).toBe(0);

    connections[0].dropAndReconnect();

    expect(reconnects).toBe(1);
  });

  it('announces a reconnect when a closed connection is opened again', async () => {
    let reconnects = 0;
    service.reconnected$.subscribe(() => reconnects++);
    await service.connect();

    // Automatic reconnecting gave up
    connections[0].drop();
    await service.connect();

    expect(connections).toHaveLength(1);
    expect(connections[0].starts).toBe(2);
    expect(reconnects).toBe(1);
  });

  it('refreshes the session and retries when the handshake is rejected', async () => {
    startFailures = 1;
    const connecting = service.connect();

    // The failed start asks for the session, a request the auth interceptor refreshes on 401
    await vi.waitFor(() => backend.expectOne('/api/auth/me').flush({}));
    await connecting;

    expect(connections[0].starts).toBe(2);
    expect(connections[0].state).toBe(HubConnectionState.Connected);
  });

  it('stays usable when the connection cannot be opened', async () => {
    startFailures = 2;
    const connecting = service.connect();

    await vi.waitFor(() => backend.expectOne('/api/auth/me').flush({}));

    await expect(connecting).resolves.toBeUndefined();
    expect(connections[0].state).toBe(HubConnectionState.Disconnected);
  });

  it('opens the same connection again after a failed start, announcing it as a reconnect', async () => {
    startFailures = 2;
    const connecting = service.connect();
    await vi.waitFor(() => backend.expectOne('/api/auth/me').flush({}));
    await connecting;

    const reconnected = firstValueFrom(service.reconnected$);
    await service.connect();

    await expect(reconnected).resolves.toBeUndefined();
    expect(connections).toHaveLength(1);
    expect(connections[0].state).toBe(HubConnectionState.Connected);
  });

  it('closes the connection on disconnect and opens a new one afterwards', async () => {
    await service.connect();

    await service.disconnect();
    expect(connections[0].stops).toBe(1);

    await service.connect();
    expect(connections).toHaveLength(2);
    expect(connections[1].state).toBe(HubConnectionState.Connected);
  });
});
