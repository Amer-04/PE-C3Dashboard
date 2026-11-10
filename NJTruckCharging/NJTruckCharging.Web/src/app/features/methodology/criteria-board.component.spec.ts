import { TestBed } from '@angular/core/testing';
import { Criterion, RuleCondition, ScoringFieldInfo } from '../../core/methodology.models';
import { CriteriaBoardComponent, CriterionCardItem } from './criteria-board.component';
import { explainScoring, scoringBreakdown } from './criterion-text';

const item = (over: Partial<CriterionCardItem>): CriterionCardItem => ({
  key: 'k', name: 'Rule', explanation: 'Plain description', category: 'Site', enabled: true, builtIn: true,
  min: 0, max: 20, rule: '≥ 3 ac: +20', missingPolicy: null, ...over,
});

describe('CriteriaBoardComponent (rule list)', () => {
  function render(items: CriterionCardItem[], editable = false) {
    const f = TestBed.createComponent(CriteriaBoardComponent);
    f.componentRef.setInput('items', items);
    f.componentRef.setInput('editable', editable);
    f.detectChanges();
    return f;
  }
  const rowsOf = (det: Element) => [...det.querySelectorAll('.rl-bands li')].map(r =>
    [r.querySelector('.rl-when')!.textContent!.trim(), r.querySelector('.rl-band-pts')?.textContent!.trim() ?? '']);

  it('lists rules in one column, grouped by category: name and points on one line, description, then the buttons', () => {
    const f = render([
      item({ key: 'a', name: 'Open land', category: 'Site', min: 0, max: 20 }),
      item({ key: 'b', name: 'Flood zone', category: 'Penalty', min: -15, max: -15 }),
      item({ key: 'c', name: 'Truck counts', category: 'Truck demand', min: -5, max: 10 }),
    ]);
    const el: HTMLElement = f.nativeElement;
    expect([...el.querySelectorAll('.rl-group-head h3')].map(h => h.textContent!.trim())).toEqual(['Site', 'Truck demand', 'Penalty']);
    expect(el.querySelector('.mm-cards')).toBeNull();

    const li = el.querySelector('[data-key="a"]')!;
    expect(li.querySelector('.rl-head .rl-name')!.textContent).toContain('Open land');
    expect(li.querySelector('.rl-head .rl-points')!.textContent!.trim()).toBe('0 to +20 pts');
    const order = [...li.children].map(c => c.className.split(' ')[0]);
    expect(order.indexOf('rl-desc')).toBeGreaterThan(order.indexOf('rl-head'));
    expect(order.indexOf('rl-actions')).toBeGreaterThan(order.indexOf('rl-desc'));
    expect([...li.querySelectorAll('.rl-actions button')].map(b => b.textContent!.trim())).toEqual(['Show More', 'Edit Rule']);

    expect(el.querySelector('[data-key="b"] .rl-points')!.textContent!.trim()).toBe('−15 pts');
    expect(el.querySelector('[data-key="c"] .rl-points')!.textContent!.trim()).toBe('−5 to +10 pts');
  });

  it('starts with every rule collapsed and no scoring details on the page', () => {
    const f = render([item({ key: 'a' }), item({ key: 'b' }), item({ key: 'c', category: 'Penalty' })]);
    const el: HTMLElement = f.nativeElement;
    expect(el.querySelectorAll('.rl-details').length).toBe(0);
    expect([...el.querySelectorAll('.rl-more')].every(b => b.getAttribute('aria-expanded') === 'false')).toBe(true);
    expect(el.textContent).not.toContain('≥ 3 ac');
    expect(el.textContent).not.toContain('How points are calculated');
  });

  it('expands and collapses each rule independently, with aria-expanded, aria-controls and a chevron', () => {
    const f = render([
      item({ key: 'a', rule: '≥ 3 ac: +20 · 1.5–3 ac: +16 · otherwise −15 · missing data: −10' }),
      item({ key: 'b', rule: 'At least one within 2 mi: +3 · otherwise 0' }),
    ]);
    const el: HTMLElement = f.nativeElement;
    const more = (k: string) => el.querySelector(`[data-key="${k}"] .rl-more`) as HTMLButtonElement;
    expect(more('a').getAttribute('aria-controls')).toBeNull();

    more('a').click(); f.detectChanges();
    expect(more('a').getAttribute('aria-expanded')).toBe('true');
    expect(more('a').textContent!.trim()).toBe('Show Less');
    expect(more('a').querySelector('.rl-chev.up')).not.toBeNull();
    const det = el.querySelector('[data-key="a"] .rl-details')!;
    expect(more('a').getAttribute('aria-controls')).toBe(det.id);
    expect(det.textContent).toContain('How points are calculated');
    expect(rowsOf(det)).toEqual([['≥ 3 ac', '+20 pts'], ['1.5–3 ac', '+16 pts'], ['All other sites', '−15 pts']]);
    expect(det.textContent).toContain('When data is missing');

    more('b').click(); f.detectChanges();
    expect(el.querySelectorAll('.rl-details').length).toBe(2);

    more('a').click(); f.detectChanges();
    expect(more('a').getAttribute('aria-expanded')).toBe('false');
    expect(more('a').textContent!.trim()).toBe('Show More');
    expect(el.querySelector('[data-key="a"] .rl-details')).toBeNull();
    expect(el.querySelector('[data-key="b"] .rl-details')).not.toBeNull();
  });

  it('prefers the exact structured explanation over the one-line text when it is provided', () => {
    const breakdown = { intro: 'Based on Open land.', rows: [{ when: '3 ac or more', points: '+20 pts' }], notes: ['A note'], missing: null };
    const f = render([item({ key: 'a', breakdown })]);
    (f.nativeElement.querySelector('.rl-more') as HTMLButtonElement).click(); f.detectChanges();
    const det = f.nativeElement.querySelector('.rl-details')!;
    expect(rowsOf(det)).toEqual([['3 ac or more', '+20 pts']]);
    expect(det.textContent).toContain('Based on Open land.');
    expect(det.textContent).toContain('A note');
    expect(det.textContent).not.toContain('When data is missing');
  });

  it('gives Show More and Edit Rule the same button style, readable labels and the rule name for screen readers', () => {
    const f = render([item({ key: 'a', name: 'Port' })]);
    const [more, edit] = [...(f.nativeElement as HTMLElement).querySelectorAll('.rl-actions button')] as HTMLButtonElement[];
    for (const b of [more, edit]) {
      expect(b.tagName).toBe('BUTTON');            // native buttons: Enter and Space work
      expect(b.type).toBe('button');
      expect(b.classList.contains('btn')).toBe(true);
      expect(b.classList.contains('rl-btn')).toBe(true);
    }
    expect(more.getAttribute('aria-label')).toBe('Show More: Port');
    expect(edit.getAttribute('aria-label')).toBe('Edit Rule: Port');
    expect(edit.classList.contains('rl-more')).toBe(false);
  });

  it('emits edit with the rule key', () => {
    const f = render([item({ key: 'a' })]);
    let got = '';
    f.componentInstance.edit.subscribe(k => (got = k));
    (f.nativeElement.querySelector('.rl-actions .rl-edit') as HTMLButtonElement).click();
    expect(got).toBe('a');
  });

  it('shows on/off switches and Remove only for added rules in a draft', () => {
    const f = render([item({ key: 'a' }), item({ key: 'x', builtIn: false })], true);
    const el: HTMLElement = f.nativeElement;
    expect(el.querySelectorAll('.rl-switch').length).toBe(2);
    expect(el.querySelector('[data-key="a"]')!.textContent).not.toContain('Remove');
    expect(el.querySelector('[data-key="x"]')!.textContent).toContain('Remove');
  });
});

// ---- Exact explanations (mirror MethodologyEngine: first matching row wins, Between includes both ends) ----
const field = (key: string, label: string, unit: string | null, missing = 0): ScoringFieldInfo =>
  ({ key, label, unit, type: 'Number', missing, missingMeaning: null } as unknown as ScoringFieldInfo);
const FIELDS = [field('open', 'Open land', 'ac'), field('exit', 'Distance to eligible exit', 'mi'), field('port', 'Distance to port', 'mi'),
  field('fleets', 'Trucks based within 5 mi', null, 120), field('flood', 'In flood zone', null, 900), field('short', 'Short side', 'ft'), field('compact', 'Compactness', null)];
const cond = (f: string, op: RuleCondition['op'], value: number | null, value2: number | null = null): RuleCondition => ({ field: f, op, value, value2, values: [] });
const crit = (over: Partial<Criterion>): Criterion => ({
  key: 'c', name: 'C', explanation: '', category: 'Site', enabled: true, builtIn: true, kind: 'Cases', field: null, fullAt: null, zeroAt: null,
  maxPoints: null, cases: [], defaultPoints: 0, missing: { mode: 'ZeroPoints', points: 0 }, ...over,
});

describe('explainScoring', () => {
  it('banded rules: shows the exact boundaries left after earlier rows, and what "all other sites" really means', () => {
    const b = explainScoring(crit({
      field: null, defaultPoints: -15, cases: [
        { label: null, conditions: [cond('open', 'Gte', 3)], points: 20 },
        { label: null, conditions: [cond('open', 'Between', 1.5, 3)], points: 16 },
        { label: null, conditions: [cond('open', 'Between', 1, 1.5)], points: 10 },
        { label: null, conditions: [cond('open', 'Between', 0.6, 1)], points: 3 },
      ],
    }), FIELDS);
    expect(b.intro).toBe('Based on Open land.');
    expect(b.rows).toEqual([
      { when: '3 ac or more', points: '+20 pts' },
      { when: '1.5 ac up to, but not including, 3 ac', points: '+16 pts' },
      { when: '1 ac up to, but not including, 1.5 ac', points: '+10 pts' },
      { when: '0.6 ac up to, but not including, 1 ac', points: '+3 pts' },
      { when: 'Less than 0.6 ac', points: '−15 pts' },
    ]);
    expect(b.missing).toBeNull();
  });

  it('banded rules with "or less" steps (Port) give "more than … up to …" ranges', () => {
    const b = explainScoring(crit({ cases: [
      { label: null, conditions: [cond('port', 'Lte', 5)], points: 4 },
      { label: null, conditions: [cond('port', 'Lte', 10)], points: 2 },
    ] }), FIELDS);
    expect(b.rows).toEqual([
      { when: '5 mi or less', points: '+4 pts' },
      { when: 'More than 5 mi, up to 10 mi', points: '+2 pts' },
      { when: 'More than 10 mi', points: '0 pts' },
    ]);
  });

  it('sliding-scale rules explain what happens between the two thresholds, with a worked example', () => {
    const b = explainScoring(crit({ kind: 'Linear', field: 'exit', fullAt: 0.5, zeroAt: 5, maxPoints: 20 }), FIELDS);
    expect(b.rows).toEqual([
      { when: '0.5 mi or less', points: '+20 pts' },
      { when: 'Between 0.5 and 5 mi', points: '+20 to 0 pts' },
      { when: '5 mi or more', points: '0 pts' },
    ]);
    expect(b.notes[0]).toBe('Between 0.5 mi and 5 mi, points change evenly from +20 pts to 0 pts. For example, a site at 2.75 mi earns +10 pts.');
  });

  it('ranking rules: the lowest site gets just above 0 (never exactly 0), and missing data is excluded from the ranking', () => {
    const b = explainScoring(crit({ kind: 'Percentile', field: 'fleets', maxPoints: 12, missing: { mode: 'FixedPoints', points: -2 } }), FIELDS);
    expect(b.rows.map(r => r.points)).toEqual(['+12 pts', 'about +6 pts', 'just above 0 pts']);
    expect(b.intro).toContain('same NOFA segment');
    expect(b.missing).toBe('Sites with no Trucks based within 5 mi data get −2 pts and are left out of the ranking. Values are never estimated.');
  });

  it('rows with several conditions are written in words, with the first-match order explained', () => {
    const b = explainScoring(crit({ cases: [
      { label: 'Short side < 120 ft', conditions: [cond('short', 'Lt', 120)], points: -10 },
      { label: null, conditions: [cond('compact', 'Gte', 0.5), cond('short', 'Gte', 180)], points: 5 },
    ] }), FIELDS);
    expect(b.rows).toEqual([
      { when: 'Short side less than 120 ft', points: '−10 pts' },
      { when: 'Compactness 0.5 or more and Short side 180 ft or more', points: '+5 pts' },
      { when: 'All other sites', points: '0 pts' },
    ]);
    expect(b.notes[0]).toContain('first row it matches');
  });

  it('describes the missing-data policy only when the rule reads data some sites lack', () => {
    const flood = crit({ cases: [{ label: 'In flood zone', conditions: [{ field: 'flood', op: 'IsTrue', value: null, value2: null, values: [] }], points: -10 }],
      missing: { mode: 'FixedPoints', points: -10 } });
    const b = explainScoring(flood, FIELDS);
    expect(b.rows).toEqual([{ when: 'In flood zone', points: '−10 pts' }, { when: 'All other sites', points: '0 pts' }]);
    expect(b.missing).toBe('Sites with no In flood zone data get −10 pts. Values are never estimated.');
  });

  it('leaves scales such as "0–1" out of the wording and says "0" (not "0 or less") for counts', () => {
    const fields = [...FIELDS, { ...field('stops', 'Truck stops within 2 mi', 'stops'), min: 0 } as ScoringFieldInfo, field('cmp', 'Lot compactness', '0–1')];
    expect(explainScoring(crit({ cases: [{ label: 'At least one', conditions: [cond('stops', 'Gt', 0)], points: 3 }] }), fields).rows).toEqual([
      { when: 'More than 0 stops', points: '+3 pts' }, { when: '0 stops', points: '0 pts' },
    ]);
    expect(explainScoring(crit({ cases: [{ label: null, conditions: [cond('cmp', 'Gte', 0.5), cond('short', 'Gte', 180)], points: 5 }] }), fields).rows[0])
      .toEqual({ when: 'Lot compactness 0.5 or more and Short side 180 ft or more', points: '+5 pts' });
  });
});

describe('scoringBreakdown (fallback from the one-line text)', () => {
  it('keeps every value verbatim and moves the missing-data suffix out of the rows', () => {
    expect(scoringBreakdown('In flood zone: −10 · otherwise 0 · missing data: −10')).toEqual({ intro: null, notes: [],
      missing: 'Sites without this data get −10 pts. Values are never estimated.',
      rows: [{ when: 'In flood zone', points: '−10 pts' }, { when: 'All other sites', points: '0 pts' }] });
  });

  it('handles sliding-scale and ranking text and never claims the lowest ranked site gets 0', () => {
    const lin = scoringBreakdown('+20 at ≤ 0.5 mi, falling linearly to 0 at ≥ 5 mi (Distance to eligible exit)');
    expect(lin.rows).toEqual([{ when: '≤ 0.5 mi', points: '+20 pts' }, { when: '≥ 5 mi', points: '0 pts' }]);
    expect(lin.notes[0]).toContain('change evenly');
    const pct = scoringBreakdown('+12 × percentile within the segment of Trucks based within 5 mi');
    expect(pct.rows[1]).toEqual({ when: 'Lowest in its segment', points: 'just above 0 pts' });
    expect(scoringBreakdown('Something new').rows).toEqual([{ when: 'Something new', points: '' }]);
  });
});
