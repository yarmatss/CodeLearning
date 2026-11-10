export interface Submission {
  id: string;
  problemId: string;
  problemTitle: string;
  languageId: string;
  languageName: string;
  code: string;
  status: SubmissionStatus;
  score: number;
  totalTestCases: number;
  passedTestCases: number;
  executionTimeMs?: number | null;
  memoryUsedKB?: number | null;
  compilationError?: string;
  runtimeError?: string;
  createdAt: Date;
  completedAt?: Date;
  testResults?: TestResult[] | null;
}

export enum SubmissionStatus {
  Pending = 0,
  Running = 1,
  Completed = 2,
  CompilationError = 3,
  RuntimeError = 4,
  TimeLimitExceeded = 5,
  MemoryLimitExceeded = 6
}

export interface TestResult {
  testCaseId: string;
  status: TestResultStatus;
  input?: string | null;
  expectedOutput?: string | null;
  actualOutput?: string | null;
  errorMessage?: string | null;
  executionTimeMs?: number | null;
  memoryUsedKB?: number | null;
  isPublic: boolean;
}

// Mirrors CodeLearning.Core.Enums.TestResultStatus (serialized as its number)
export enum TestResultStatus {
  Passed = 1,
  Failed = 2,
  RuntimeError = 3,
  TimeLimitExceeded = 4,
  MemoryLimitExceeded = 5
}

/** Statuses after which a submission no longer changes. */
export const FINAL_SUBMISSION_STATUSES: readonly SubmissionStatus[] = [
  SubmissionStatus.Completed,
  SubmissionStatus.CompilationError,
  SubmissionStatus.RuntimeError,
  SubmissionStatus.TimeLimitExceeded,
  SubmissionStatus.MemoryLimitExceeded
];

/** A submission that passed every test, which is what completes a problem block. */
export function isAcceptedSubmission(submission: Pick<Submission, 'status' | 'score'>): boolean {
  return submission.status === SubmissionStatus.Completed && submission.score === 100;
}

export interface SubmitCodeRequest {
  problemId: string;
  languageId: string;
  code: string;
}
