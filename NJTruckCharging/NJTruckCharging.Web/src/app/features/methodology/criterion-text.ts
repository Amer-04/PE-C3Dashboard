import { CATEGORY_LABELS, ConditionOp, Criterion, OP_LABELS, RuleCondition, ScoringFieldInfo } from '../../core/methodology.models';
import type { CriterionCardItem } from './criteria-board.component';

/** One-line, human-readable summary of a (possibly unsaved) criterion — mirrors the server's CriterionText. */
export function describeCriterion(c: Criterion, fields: ScoringFieldInfo[]): string {
  const f = (k: string | null) => fields.find(x => x.key === k);
  const unit = (k: string | null) => (f(k)?.unit ? ' ' + f(k)!.unit : '');
  let text: string;
  if (c.kind === 'Linear') {
    const lower = (c.fullAt ?? 0) < (c.zeroAt ?? 0);
    text = `${pts(c.maxPoints ?? 0)} at ${lower ? '≤' : '≥'} ${num(c.fullAt)}${unit(c.field)}, linearly to 0 at ${lower ? '≥' : '≤'} ${num(c.zeroAt)}${unit(c.field)} (${f(c.field)?.label ?? c.field})`;
  } else if (c.kind === 'Percentile') {
    text = `${pts(c.maxPoints ?? 0)} × percentile within the segment of ${f(c.field)?.label ?? c.field}`;
  } else {
    text = [...c.cases.map(x => `${x.label || x.conditions.map(y => cond(y, fields)).join(' and ')}: ${pts(x.points)}`), `otherwise ${pts(c.defaultPoints)}`].join(' · ');
  }
  const keys = c.kind === 'Cases' ? c.cases.flatMap(x => x.conditions.map(y => y.field)) : [c.field];
  if (keys.some(k => (f(k)?.missing ?? 0) > 0 || !!f(k)?.missingMeaning))
    text += ` · missing data: ${c.missing.mode === 'FixedPoints' ? pts(c.missing.points) : '0'}`;
  return text;
}

/** [min, max] points this criterion can award. */
export function criterionRange(c: Criterion): [number, number] {
  const o = c.kind === 'Cases' ? [...c.cases.map(x => x.points), c.defaultPoints] : [0, c.maxPoints ?? 0];
  if (c.missing.mode === 'FixedPoints') o.push(c.missing.points);
  return [Math.min(...o), Math.max(...o)];
}

function cond(c: RuleCondition, fields: ScoringFieldInfo[]) {
  const f = fields.find(x => x.key === c.field);
  const label = f?.label ?? c.field;
  const u = f?.unit ? ' ' + f.unit : '';
  switch (c.op) {
    case 'IsTrue': return label;
    case 'IsFalse': return `not ${label}`;
    case 'Between': return `${label} ${num(c.value)}–${num(c.value2)}${u}`;
    case 'In': case 'NotIn': case 'AnyLotIn': return `${label} ${OP_LABELS[c.op]} ${c.values.join('/')}`;
    default: return `${label} ${OP_LABELS[c.op]} ${num(c.value)}${u}`;
  }
}

function pts(v: number) { return (v > 0 ? '+' : v < 0 ? '−' : '') + Math.abs(v).toLocaleString(undefined, { maximumFractionDigits: 2 }); }
function num(v: number | null) { return v === null ? '?' : v.toLocaleString(undefined, { maximumFractionDigits: 3 }); }

/** Fields this criterion reads that are missing for some sites (so its missing-data policy applies). */
export function missableFields(c: Criterion, fields: ScoringFieldInfo[]): ScoringFieldInfo[] {
  const keys = c.kind === 'Cases' ? c.cases.flatMap(x => x.conditions.map(y => y.field)) : c.field ? [c.field] : [];
  return [...new Set(keys)].map(k => fields.find(f => f.key === k)).filter((f): f is ScoringFieldInfo => !!f && (f.missing > 0 || !!f.missingMeaning));
}

/** Card model for a (possibly unsaved) draft criterion. */
export function cardItem(c: Criterion, fields: ScoringFieldInfo[]): CriterionCardItem {
  const [min, max] = criterionRange(c);
  const missing = missableFields(c, fields).length ? pts(c.missing.mode === 'FixedPoints' ? c.missing.points : 0) + ' pts' : null;
  return { key: c.key, name: c.name, explanation: c.explanation, category: CATEGORY_LABELS[c.category] ?? c.category, enabled: c.enabled,
    builtIn: c.builtIn, min, max, rule: describeCriterion(c, fields).replace(/ · missing data: .*$/, ''), missingPolicy: missing,
    breakdown: explainScoring(c, fields) };
}

/** A full plain-English sentence or two describing exactly how the criterion awards points. */
export function explainCriterion(c: Criterion, fields: ScoringFieldInfo[]): string {
  const f = (k: string | null) => fields.find(x => x.key === k);
  const label = (k: string | null) => f(k)?.label ?? k ?? 'the selected field';
  const u = (k: string | null) => (f(k)?.unit ? ' ' + f(k)!.unit : '');
  let text: string;
  if (c.kind === 'Linear') {
    const lower = (c.fullAt ?? 0) < (c.zeroAt ?? 0);
    text = `Sites earn ${pts(c.maxPoints ?? 0)} points when ${label(c.field)} is ${num(c.fullAt)}${u(c.field)} or ${lower ? 'less' : 'more'}, `
      + `falling evenly to 0 points at ${num(c.zeroAt)}${u(c.field)} or ${lower ? 'more' : 'less'}.`;
  } else if (c.kind === 'Percentile') {
    text = `Sites earn up to ${pts(c.maxPoints ?? 0)} points according to how they rank on ${label(c.field)} against the other sites in the same NOFA segment `
      + `— the highest site gets the full amount, a middle site about half (ties share the same rank).`;
  } else {
    const bands = c.cases.map(x => `${x.conditions.map(y => cond(y, fields)).join(' and ')} → ${pts(x.points)}`);
    text = c.cases.length === 1
      ? `Sites get ${pts(c.cases[0].points)} points if ${c.cases[0].conditions.map(y => cond(y, fields)).join(' and ')}; otherwise ${pts(c.defaultPoints)}.`
      : `Each site gets the points of the first band it matches: ${bands.join('; ')}; otherwise ${pts(c.defaultPoints)}.`;
  }
  const m = missableFields(c, fields);
  if (m.length) text += ` Sites with no ${m.map(x => x.label).join(' / ')} data get ${c.missing.mode === 'FixedPoints' ? pts(c.missing.points) : '0'} points — nothing is estimated.`;
  if (!c.enabled) text = 'This criterion is turned off, so it adds no points to any site. When on: ' + text;
  return text;
}

/** One readable row of a rule's scoring: which sites it covers on the left, the points they earn on the right. */
export interface ScoringRow { when: string; points: string; }
/**
 * A rule's scoring in plain words: an optional intro ("Based on …"), short rows, extra notes (e.g. what happens between
 * two thresholds) and the missing-data sentence.
 */
export interface ScoringBreakdown { intro: string | null; rows: ScoringRow[]; notes: string[]; missing: string | null; }

const NUMERIC_OPS: ConditionOp[] = ['Lt', 'Lte', 'Gt', 'Gte', 'Eq', 'Between'];
interface Interval { lo: number; loInc: boolean; hi: number; hiInc: boolean; }

/**
 * Explains exactly how a criterion awards points, following the scoring engine (MethodologyEngine):
 * Linear = full points at/beyond "full", 0 at/beyond "zero", a straight line in between; Percentile = max × the site's
 * percentile rank within its NOFA segment (ties share the average rank, sites without data are left out); Cases = the
 * FIRST matching row wins ("Between" includes both ends), otherwise the default. Display only — nothing is recalculated.
 */
export function explainScoring(c: Criterion, fields: ScoringFieldInfo[]): ScoringBreakdown {
  const fi = (k: string | null) => fields.find(x => x.key === k);
  const label = (k: string | null) => fi(k)?.label ?? k ?? 'the selected data';
  // Scales such as "0–1" are not units: leave them out of the sentence.
  const unit = (k: string | null) => { const u = fi(k)?.unit; return u && !/\d/.test(u) ? ' ' + u : ''; };
  const P = (v: number) => `${pts(v)} pts`;
  const m = missableFields(c, fields);
  const award = c.missing.mode === 'FixedPoints' ? c.missing.points : 0;
  const missing = m.length
    ? `Sites with no ${m.map(x => x.label).join(' or ')} data get ${P(award)}${c.kind === 'Percentile' ? ' and are left out of the ranking' : ''}. Values are never estimated.`
    : null;

  if (c.kind === 'Linear') {
    const F = c.fullAt ?? 0, Z = c.zeroAt ?? 0, M = c.maxPoints ?? 0, u = unit(c.field), lower = F < Z;
    return {
      intro: `Based on ${label(c.field)}.`,
      rows: [
        { when: `${num(F)}${u} or ${lower ? 'less' : 'more'}`, points: P(M) },
        { when: `Between ${num(Math.min(F, Z))} and ${num(Math.max(F, Z))}${u}`, points: `${pts(M)} to 0 pts` },
        { when: `${num(Z)}${u} or ${lower ? 'more' : 'less'}`, points: P(0) },
      ],
      notes: [`Between ${num(F)}${u} and ${num(Z)}${u}, points change evenly from ${P(M)} to 0 pts. For example, a site at ${num((F + Z) / 2)}${u} earns ${P(M / 2)}.`],
      missing,
    };
  }

  if (c.kind === 'Percentile') {
    const M = c.maxPoints ?? 0;
    return {
      intro: `Based on ${label(c.field)}, compared only with other sites in the same NOFA segment.`,
      rows: [
        { when: 'Highest in its segment', points: P(M) },
        { when: 'Middle of its segment', points: `about ${P(M / 2)}` },
        { when: 'Lowest in its segment', points: 'just above 0 pts' },
      ],
      notes: [`Each site earns ${P(M)} × the share of sites in its segment with the same or a lower value (counting itself). Sites with equal values share the same points.`],
      missing,
    };
  }

  // Cases: exact ranges when every row tests the same single number field; otherwise each row's conditions in words.
  const single = c.cases.length > 0 && c.cases.every(x => x.conditions.length === 1 && NUMERIC_OPS.includes(x.conditions[0].op))
    && new Set(c.cases.map(x => x.conditions[0].field)).size === 1;
  if (single) {
    const field = c.cases[0].conditions[0].field, u = unit(field);
    const nonNegative = (fi(field)?.min ?? -1) >= 0;   // counts, distances, acres: "0 or less" simply means 0
    const seen: Interval[] = [];
    const rows = c.cases.map(x => {
      const iv = toInterval(x.conditions[0]);
      const left = minus([iv], seen);
      seen.push(iv);
      return { when: left.length ? left.map(v => describe(v, u, nonNegative)).join(' or ') : 'Never reached (covered by a row above)', points: P(x.points) };
    });
    const rest = minus([{ lo: -Infinity, loInc: false, hi: Infinity, hiInc: false }], seen);
    if (rest.length) rows.push({ when: rest.map(v => describe(v, u, nonNegative)).join(' or '), points: P(c.defaultPoints) });
    return { intro: `Based on ${label(field)}.`, rows, notes: [], missing };
  }
  const allNumeric = (x: { conditions: RuleCondition[] }) => x.conditions.every(y => NUMERIC_OPS.includes(y.op));
  const rows = c.cases.map(x => ({
    when: allNumeric(x) || !x.label ? x.conditions.map(y => condWords(y, label(y.field), unit(y.field))).join(' and ') : x.label,
    points: P(x.points),
  }));
  rows.push({ when: 'All other sites', points: P(c.defaultPoints) });
  return {
    intro: null, rows,
    notes: c.cases.length > 1 ? ['Rows are checked from top to bottom; each site gets the points of the first row it matches.'] : [],
    missing,
  };
}

function toInterval(c: RuleCondition): Interval {
  const v = c.value ?? 0;
  switch (c.op) {
    case 'Lt': return { lo: -Infinity, loInc: false, hi: v, hiInc: false };
    case 'Lte': return { lo: -Infinity, loInc: false, hi: v, hiInc: true };
    case 'Gt': return { lo: v, loInc: false, hi: Infinity, hiInc: false };
    case 'Gte': return { lo: v, loInc: true, hi: Infinity, hiInc: false };
    case 'Eq': return { lo: v, loInc: true, hi: v, hiInc: true };
    default: return { lo: v, loInc: true, hi: c.value2 ?? v, hiInc: true };   // Between: both ends included (engine: >= and <=)
  }
}
const isEmpty = (a: Interval) => a.lo > a.hi || (a.lo === a.hi && !(a.loInc && a.hiInc));
function overlaps(a: Interval, b: Interval) {
  const before = (x: Interval, y: Interval) => x.hi < y.lo || (x.hi === y.lo && !(x.hiInc && y.loInc));
  return !before(a, b) && !before(b, a);
}
/** a minus b: up to two pieces. */
function cut(a: Interval, b: Interval): Interval[] {
  if (!overlaps(a, b)) return [a];
  const out: Interval[] = [];
  if (a.lo < b.lo || (a.lo === b.lo && a.loInc && !b.loInc)) out.push({ lo: a.lo, loInc: a.loInc, hi: b.lo, hiInc: !b.loInc });
  if (b.hi < a.hi || (b.hi === a.hi && a.hiInc && !b.hiInc)) out.push({ lo: b.hi, loInc: !b.hiInc, hi: a.hi, hiInc: a.hiInc });
  return out.filter(x => !isEmpty(x));
}
function minus(set: Interval[], remove: Interval[]) {
  return remove.reduce((acc, r) => acc.flatMap(a => cut(a, r)), set.filter(x => !isEmpty(x)));
}
function describe(v: Interval, u: string, nonNegative = false): string {
  const a = num(v.lo), b = num(v.hi);
  if (nonNegative && v.lo === -Infinity && v.hi === 0 && v.hiInc) return `0${u}`;
  if (v.lo === -Infinity && v.hi === Infinity) return 'Any value';
  if (v.lo === -Infinity) return v.hiInc ? `${b}${u} or less` : `Less than ${b}${u}`;
  if (v.hi === Infinity) return v.loInc ? `${a}${u} or more` : `More than ${a}${u}`;
  if (v.lo === v.hi) return `Exactly ${a}${u}`;
  if (v.loInc && v.hiInc) return `${a} to ${b}${u}`;
  if (v.loInc) return `${a}${u} up to, but not including, ${b}${u}`;
  if (v.hiInc) return `More than ${a}${u}, up to ${b}${u}`;
  return `More than ${a}${u} and less than ${b}${u}`;
}
function condWords(c: RuleCondition, label: string, u: string): string {
  switch (c.op) {
    case 'Lt': return `${label} less than ${num(c.value)}${u}`;
    case 'Lte': return `${label} ${num(c.value)}${u} or less`;
    case 'Gt': return `${label} more than ${num(c.value)}${u}`;
    case 'Gte': return `${label} ${num(c.value)}${u} or more`;
    case 'Eq': return `${label} exactly ${num(c.value)}${u}`;
    case 'Neq': return `${label} not ${num(c.value)}${u}`;
    case 'Between': return `${label} ${num(c.value)} to ${num(c.value2)}${u}`;
    case 'IsTrue': return label;
    case 'IsFalse': return `Not: ${label}`;
    case 'In': return `${label}: ${c.values.join(' or ')}`;
    case 'NotIn': return `${label}: anything except ${c.values.join(' or ')}`;
    default: return `Any lot's ${label}: ${c.values.join(' or ')}`;
  }
}

/**
 * Fallback when only the one-line rule text is available (server CriterionText / {@link describeCriterion}, both
 * "cond: pts · … · otherwise pts"): splits it into rows without changing any value. Unknown text becomes one row.
 */
export function scoringBreakdown(text: string): ScoringBreakdown {
  const ptsLabel = (p: string) => `${p.trim()} pts`;
  const m = / · missing data: (.+)$/.exec(text);
  const missing = m ? `Sites without this data get ${ptsLabel(m[1])}. Values are never estimated.` : null;
  const rule = (m ? text.slice(0, m.index) : text).trim();
  const linear = /^([+−-]?[\d.,]+) at ([≤≥]) (.+?), (?:falling )?linearly to 0 at ([≤≥]) (.+?) \((.+)\)$/.exec(rule);
  if (linear) {
    const [, max, op1, v1, op2, v2, field] = linear;
    return { intro: `Based on ${field}.`, missing, notes: [`Between ${v1} and ${v2}, points change evenly from ${ptsLabel(max)} to 0 pts.`],
      rows: [{ when: `${op1} ${v1}`, points: ptsLabel(max) }, { when: `${op2} ${v2}`, points: ptsLabel('0') }] };
  }
  const pct = /^([+−-]?[\d.,]+) × percentile within the segment of (.+)$/.exec(rule);
  if (pct) {
    const [, max, field] = pct;
    return { intro: `Based on ${field}, compared only with other sites in the same NOFA segment.`, missing, notes: [],
      rows: [{ when: 'Highest in its segment', points: ptsLabel(max) }, { when: 'Lowest in its segment', points: 'just above 0 pts' }] };
  }
  const rows = rule.split(' · ').map(part => {
    const other = /^otherwise (.+)$/.exec(part);
    if (other) return { when: 'All other sites', points: ptsLabel(other[1]) };
    const i = part.lastIndexOf(': ');
    return i > 0 && /^[+−-]?[\d.,]+$/.test(part.slice(i + 2).trim())
      ? { when: part.slice(0, i), points: ptsLabel(part.slice(i + 2)) }
      : { when: part, points: '' };
  });
  return { intro: null, rows, notes: [], missing };
}
