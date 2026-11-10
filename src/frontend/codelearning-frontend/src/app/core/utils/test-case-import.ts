import { CreateTestCaseRequest } from '../services/problem.service';

/** The API's limit on test cases per problem. */
export const MAX_TEST_CASES_PER_PROBLEM = 50;

/** Example shown next to the bulk import box. */
export const BULK_TEST_CASES_EXAMPLE = `[
  { "input": "2 3", "expectedOutput": "5", "isPublic": true },
  { "input": "10 -4", "expectedOutput": "6", "isPublic": false }
]`;

/**
 * Parses the bulk test-case import text: a JSON array of `{ input, expectedOutput, isPublic? }`.
 * `isPublic` defaults to false; numbers are accepted for input/output and turned into text.
 * Throws an Error whose message names the first problem found, ready to show to the teacher.
 */
export function parseBulkTestCases(text: string): CreateTestCaseRequest[] {
  if (!text.trim()) {
    throw new Error('Paste a JSON array of test cases first.');
  }

  let data: unknown;
  try {
    data = JSON.parse(text);
  } catch {
    throw new Error('The test cases are not valid JSON.');
  }

  if (!Array.isArray(data)) {
    throw new Error('Expected a JSON array of test cases.');
  }
  if (data.length === 0) {
    throw new Error('The array contains no test cases.');
  }
  if (data.length > MAX_TEST_CASES_PER_PROBLEM) {
    throw new Error(`A problem can have at most ${MAX_TEST_CASES_PER_PROBLEM} test cases.`);
  }

  return data.map((item, index) => {
    const label = `Test case ${index + 1}`;
    if (!item || typeof item !== 'object' || Array.isArray(item)) {
      throw new Error(`${label}: expected an object with "input" and "expectedOutput".`);
    }
    const { input, expectedOutput, isPublic } = item as Record<string, unknown>;
    return {
      input: toText(input, `${label}: "input"`),
      expectedOutput: toText(expectedOutput, `${label}: "expectedOutput"`),
      isPublic: toFlag(isPublic, `${label}: "isPublic"`)
    };
  });
}

function toText(value: unknown, label: string): string {
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  throw new Error(`${label} must be text.`);
}

function toFlag(value: unknown, label: string): boolean {
  if (value === undefined || value === null) return false;
  if (typeof value === 'boolean') return value;
  throw new Error(`${label} must be true or false.`);
}
