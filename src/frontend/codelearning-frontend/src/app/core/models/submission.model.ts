export interface Submission {
  id: string;
  problemId: string;
  problemTitle: string;
  languageId: string;
  languageName: string;
  status: SubmissionStatus;
  score: number;
  executionTimeMs: number;
  memoryUsedKB: number;
  compilationError?: string;
  runtimeError?: string;
  createdAt: Date;
  completedAt?: Date;
  testResults: TestResult[];
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
  input: string;
  expectedOutput: string;
  actualOutput: string;
  errorMessage?: string;
  executionTimeMs: number;
  memoryUsedKB: number;
  isPublic: boolean;
}

export enum TestResultStatus {
  Accepted = 1,
  WrongAnswer = 2,
  RuntimeError = 3,
  TimeLimitExceeded = 4,
  MemoryLimitExceeded = 5,
  CompilationError = 6
}

export interface SubmitCodeRequest {
  problemId: string;
  languageId: string;
  code: string;
}
