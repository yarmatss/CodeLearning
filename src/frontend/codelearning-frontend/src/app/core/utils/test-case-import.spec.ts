import { BULK_TEST_CASES_EXAMPLE, MAX_TEST_CASES_PER_PROBLEM, parseBulkTestCases } from './test-case-import';

describe('parseBulkTestCases', () => {
  it('parses the example', () => {
    expect(parseBulkTestCases(BULK_TEST_CASES_EXAMPLE)).toEqual([
      { input: '2 3', expectedOutput: '5', isPublic: true },
      { input: '10 -4', expectedOutput: '6', isPublic: false }
    ]);
  });

  it('allows an empty expected output and defaults isPublic to false', () => {
    expect(parseBulkTestCases('[{"input":"","expectedOutput":""}]')).toEqual([
      { input: '', expectedOutput: '', isPublic: false }
    ]);
  });

  it('turns numbers into text', () => {
    expect(parseBulkTestCases('[{"input":1,"expectedOutput":2,"isPublic":true}]')).toEqual([
      { input: '1', expectedOutput: '2', isPublic: true }
    ]);
  });

  it('rejects text that is not JSON, not an array or empty', () => {
    expect(() => parseBulkTestCases('')).toThrow(/Paste/);
    expect(() => parseBulkTestCases('{nope')).toThrow(/not valid JSON/);
    expect(() => parseBulkTestCases('{"input":"1"}')).toThrow(/JSON array/);
    expect(() => parseBulkTestCases('[]')).toThrow(/no test cases/);
  });

  it('names the test case with a missing or invalid field', () => {
    expect(() => parseBulkTestCases('[{"input":"1","expectedOutput":"1"},{"input":"2"}]'))
      .toThrow('Test case 2: "expectedOutput" must be text.');
    expect(() => parseBulkTestCases('[{"input":"1","expectedOutput":"1","isPublic":"yes"}]'))
      .toThrow('Test case 1: "isPublic" must be true or false.');
    expect(() => parseBulkTestCases('[1]')).toThrow(/Test case 1: expected an object/);
  });

  it('enforces the per-problem limit', () => {
    const many = JSON.stringify(
      Array.from({ length: MAX_TEST_CASES_PER_PROBLEM + 1 }, () => ({ input: '', expectedOutput: '' }))
    );
    expect(() => parseBulkTestCases(many)).toThrow(/at most/);
  });
});
