import { CATEGORY_LABELS, Criterion, OP_LABELS, RuleCondition, ScoringFieldInfo } from '../../core/methodology.models';
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
    builtIn: c.builtIn, min, max, rule: describeCriterion(c, fields).replace(/ · missing data: .*$/, ''), missingPolicy: missing };
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
