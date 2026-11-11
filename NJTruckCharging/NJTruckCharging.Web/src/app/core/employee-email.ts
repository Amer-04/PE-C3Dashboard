/**
 * Employee e-mail rule for the forms. USER EXPERIENCE ONLY: the real gates are the Supabase Before User Created hook
 * (account creation) and the API (every request). Same rule as EmployeeEmailPolicy.cs and the hook, tested against the
 * same vectors (employee-email.vectors.json).
 */
export const COMPANY_DOMAINS: readonly string[] = ['poweredison.com', 'ev-edison.com'];

export function isEmployeeEmail(email: string | null | undefined): boolean {
  if (!email || email.length > 254) return false;
  if (!/^[!-~]+$/.test(email)) return false;                 // printable ASCII only: no spaces or lookalike characters
  const at = email.indexOf('@');
  if (at <= 0 || at !== email.lastIndexOf('@') || at === email.length - 1) return false;
  return COMPANY_DOMAINS.includes(email.slice(at + 1).toLowerCase());
}

export const PASSWORD_MIN = 12;
/** Supabase stores passwords with bcrypt, which only uses the first 72 bytes; longer ones are refused. */
export const PASSWORD_MAX_BYTES = 72;

/** What is still wrong with a new password (empty = acceptable). Supabase enforces its own policy as well. */
export function passwordProblems(password: string): string[] {
  const problems: string[] = [];
  if (password.length < PASSWORD_MIN) problems.push(`at least ${PASSWORD_MIN} characters`);
  if (!/[A-Za-z]/.test(password)) problems.push('a letter');
  if (!/[0-9]/.test(password)) problems.push('a number');
  if (new TextEncoder().encode(password).length > PASSWORD_MAX_BYTES) problems.push(`no more than ${PASSWORD_MAX_BYTES} characters`);
  return problems;
}
