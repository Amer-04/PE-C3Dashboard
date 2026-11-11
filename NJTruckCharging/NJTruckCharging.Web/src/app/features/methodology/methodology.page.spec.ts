import { TestBed } from '@angular/core/testing';
import { computed, signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { MethodologyStore } from '../../core/methodology-store';
import { SiteStore } from '../../core/site-store';
import { Activation, ActiveMethodology, ChangeLogEntry, Criterion, ScoringFieldInfo, VersionSummary } from '../../core/methodology.models';
import { MethodologyPage } from './methodology.page';

const summary = (over: Partial<VersionSummary>): VersionSummary => ({
  id: 1, name: 'Original C3 workbook methodology', status: 'Published', isBaseline: true, isActive: true, isPreviousActive: false,
  rulesHash: 'h', changeSummary: null, basedOnVersionId: null, createdAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-10-01T00:00:00Z',
  publishedAtUtc: '2026-10-01T00:00:00Z', previewedAtUtc: null, previewCurrent: false, criteriaCount: 1, enabledCount: 1, identicalToBaseline: true,
  revision: 0, createdByEmail: null, updatedByEmail: null, publishedByEmail: null, ...over,
});
const port: Criterion = {
  key: 'port', name: 'Port', explanation: 'Proximity to a port.', category: 'TruckDemand', enabled: true, builtIn: true, kind: 'Cases',
  field: null, fullAt: null, zeroAt: null, maxPoints: null, defaultPoints: 0, missing: { mode: 'ZeroPoints', points: 0 },
  cases: [{ label: null, conditions: [{ field: 'port', op: 'Lte', value: 5, value2: null, values: [] }], points: 4 },
          { label: null, conditions: [{ field: 'port', op: 'Lte', value: 10, value2: null, values: [] }], points: 2 }],
};
const active: ActiveMethodology = {
  version: summary({}), minPossible: 0, maxPossible: 4, previousActiveVersionId: null, activatedAtUtc: null, dataVersion: 1, tieBreak: 'x',
  criteria: [{ key: 'port', name: 'Port', category: 'Truck demand', enabled: true, builtIn: true, kind: 'Cases', rule: '≤ 5 mi: +4 · ≤ 10 mi: +2 · otherwise 0',
    explanation: 'Proximity to a port.', min: 0, max: 4, fields: ['port'], usesMissableData: false, missingPolicy: null }],
};
const fields = [{ key: 'port', label: 'Distance to port', unit: 'mi', type: 'Number', missing: 0, present: 10, missingMeaning: null, isCustom: false,
  group: 'Demand', description: '' }] as unknown as ScoringFieldInfo[];

describe('MethodologyPage (header and navigation)', () => {
  function render(tab: string | undefined, versions: VersionSummary[], canEdit = true, activations: Activation[] = [], changes: ChangeLogEntry[] = []) {
    const act = signal<ActiveMethodology | null>(active);
    const editing = signal({ enabled: canEdit, reason: canEdit ? null : 'Scoring rules are read-only on this deployment.' });
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SiteStore, useValue: { powerEdisonSites: signal([]) } },
        { provide: MethodologyStore, useValue: { active: act, versionId: computed(() => act()?.version.id ?? null), workingDraft: () => of(6),
          editing, canEdit: computed(() => editing().enabled), epoch: signal(0) } },
        { provide: ApiService, useValue: {
          methodology: () => of([]), methodologyVersions: () => of(versions), methodologyActivations: () => of(activations),
          methodologyChanges: () => of({ total: changes.length, page: 1, pageSize: 25, entries: changes }),
          methodologyFields: () => of(fields), customFields: () => of([]),
          methodologyVersion: (id: number) => of({ version: summary({ id }), rules: { schemaVersion: 1, criteria: [port] }, criteria: [], minPossible: 0, maxPossible: 4,
            changesVsActive: [], issues: [], preview: null }),
        } },
      ],
    });
    const f = TestBed.createComponent(MethodologyPage);
    if (tab) f.componentRef.setInput('tab', tab);
    f.detectChanges();
    return f;
  }
  const tabs = (el: HTMLElement) => [...el.querySelectorAll('.mm-tab')].map(t => t.childNodes[0].textContent!.trim());

  it('shows four plain-language sections, with Data Fields and Reference under Advanced Options', () => {
    const el: HTMLElement = render(undefined, [summary({})]).nativeElement;
    expect(tabs(el)).toEqual(['Scoring Rules', 'Review Changes', 'Previous Methods', 'Advanced Options']);
    expect(el.querySelector('.mm-tab.on')!.textContent).toContain('Scoring Rules');
    expect(el.querySelector('.mm-adv')).toBeNull();
  });

  it('keeps old Data Fields / Reference links working inside Advanced Options', () => {
    for (const t of ['fields', 'reference', 'advanced']) {
      TestBed.resetTestingModule();
      const el: HTMLElement = render(t, [summary({})]).nativeElement;
      expect(el.querySelector('.mm-tab.on')!.textContent).toContain('Advanced Options');
      const sub = [...el.querySelectorAll('.mm-adv .mm-subtabs button')];
      expect(sub.map(b => b.textContent!.trim())).toEqual(['Data Fields', 'Reference']);
      expect(sub.find(b => b.classList.contains('on'))!.textContent!.trim()).toBe(t === 'reference' ? 'Reference' : 'Data Fields');
    }
  });

  it('header shows only the active scoring method and the unapplied-change status', () => {
    let el: HTMLElement = render(undefined, [summary({})]).nativeElement;
    expect(el.querySelectorAll('.mm-stat').length).toBe(0);
    const items = [...el.querySelectorAll('.mm-status-item')];
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Active scoring method');
    expect(items[0].textContent).toContain('Original C3 workbook methodology');
    expect(items[1].textContent).toContain('None');

    TestBed.resetTestingModule();
    el = render(undefined, [summary({}), summary({ id: 6, name: 'Draft', status: 'Draft', isBaseline: false, isActive: false, basedOnVersionId: 1 })]).nativeElement;
    const pending = el.querySelectorAll('.mm-status-item')[1];
    expect(pending.classList.contains('pending')).toBe(true);
    expect(pending.textContent).toContain('Draft in progress');
    expect(pending.querySelector('button')!.textContent!.trim()).toBe('Review Changes');
  });

  it('rules start collapsed and Show More explains the rule exactly from the structured rules', () => {
    const f = render(undefined, [summary({})]);
    const el: HTMLElement = f.nativeElement;
    expect(el.querySelectorAll('.rl-details').length).toBe(0);
    (el.querySelector('.rl-more') as HTMLButtonElement).click();
    f.detectChanges();
    const rows = [...el.querySelectorAll('.rl-details .rl-bands li')].map(r => r.textContent!.replace(/\s+/g, ' ').trim());
    expect(rows).toEqual(['5 mi or less+4 pts', 'More than 5 mi, up to 10 mi+2 pts', 'More than 10 mi0 pts']);
  });

  it('offers Add Rule and Edit Rule and says the method is shared when editing is enabled', () => {
    const el: HTMLElement = render(undefined, [summary({})], true).nativeElement;
    expect([...el.querySelectorAll('button')].some(b => b.textContent!.includes('Add Rule'))).toBe(true);
    expect(el.querySelector('.rl-edit')).not.toBeNull();
    expect(el.textContent).toContain('shared by the whole C3 team');
  });

  it('hides every editing control and explains why when this deployment is read-only', () => {
    const el: HTMLElement = render(undefined, [summary({})], false).nativeElement;
    expect([...el.querySelectorAll('button')].some(b => b.textContent!.includes('Add Rule'))).toBe(false);
    expect(el.querySelector('.rl-edit')).toBeNull();
    expect(el.textContent).toContain('Scoring rules are read-only on this deployment.');
  });

  it('history shows who applied each version and the team audit trail with e-mail and user id', () => {
    const acts: Activation[] = [{ id: 9, versionId: 6, versionName: 'Higher port points', previousVersionId: 1, reason: 'Publish',
      atUtc: '2026-11-11T10:00:00Z', sitesScored: 30584, note: null, actorUserId: 'e1e1e1e1-0000-4000-8000-0000000000e1', actorEmail: 'rules.editor@poweredison.com' }];
    const changes: ChangeLogEntry[] = [{ id: 41, versionId: 6, versionName: 'Higher port points', action: 'SaveDraft', atUtc: '2026-11-11T09:00:00Z',
      actorUserId: 'a11ce000-0000-4000-8000-00000000ad01', actorEmail: 'c3.admin.personal@gmail.com', revisionBefore: 1, revisionAfter: 2,
      rulesHashBefore: 'aaaaaaaa11', rulesHashAfter: 'bbbbbbbb22', previousActiveVersionId: null, note: null, hasSnapshots: true,
      changes: [{ key: 'port', name: 'Port', change: 'Modified', details: ['Rule: ≤ 5 mi: +4  →  ≤ 5 mi: +6'] }] }];
    const f = render('history', [summary({ publishedByEmail: 'rules.editor@poweredison.com' })], true, acts, changes);
    const el: HTMLElement = f.nativeElement;
    expect(el.querySelector('.mm-log')!.textContent).toContain('rules.editor@poweredison.com');
    expect(el.querySelector('.mm-version .vmeta')!.textContent).toContain('by rules.editor@poweredison.com');
    const log = el.querySelector('app-change-log')!;
    expect(log.textContent).toContain('Draft saved');
    expect(log.textContent).toContain('c3.admin.personal@gmail.com');
    expect(log.textContent).toContain('a11ce000');
    expect(log.textContent).toContain('revision 1 → 2');
    expect(log.textContent).toContain('≤ 5 mi: +4  →  ≤ 5 mi: +6');
  });
});
