import vectors from './employee-email.vectors.json';
import { COMPANY_DOMAINS, isEmployeeEmail, passwordProblems } from './employee-email';

describe('Employee e-mail rule (same vectors as the API and the Supabase hook)', () => {
  it.each(vectors.allowed)('allows %j', (email) => expect(isEmployeeEmail(email)).toBe(true));
  it.each(vectors.refused)('refuses %j', (email) => expect(isEmployeeEmail(email)).toBe(false));

  it('knows exactly the two company domains', () => {
    expect(COMPANY_DOMAINS).toEqual(['poweredison.com', 'ev-edison.com']);
    expect(isEmployeeEmail(null)).toBe(false);
    expect(isEmployeeEmail(undefined)).toBe(false);
  });
});

describe('New password rules (convenience; Supabase enforces its own policy)', () => {
  it('needs 12+ characters with a letter and a number, and at most 72 bytes', () => {
    expect(passwordProblems('Correct-Horse-42')).toEqual([]);
    expect(passwordProblems('short1')).toContain('at least 12 characters');
    expect(passwordProblems('123456789012')).toContain('a letter');
    expect(passwordProblems('abcdefghijkl')).toContain('a number');
    expect(passwordProblems('a1' + 'é'.repeat(40))).toContain('no more than 72 characters');   // 82 bytes
  });
});
