import { TestBed } from '@angular/core/testing';
import { computed, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { MethodologyStore } from '../../core/methodology-store';
import { Criterion, VersionDetail, VersionSummary } from '../../core/methodology.models';
import { MethodologyEditorPage } from './methodology-editor.page';

const port: Criterion = {
  key: 'port', name: 'Port', explanation: 'Proximity to a port.', category: 'TruckDemand', enabled: true, builtIn: true, kind: 'Cases',
  field: null, fullAt: null, zeroAt: null, maxPoints: null, defaultPoints: 0, missing: { mode: 'ZeroPoints', points: 0 },
  cases: [{ label: null, conditions: [{ field: 'port', op: 'Lte', value: 5, value2: null, values: [] }], points: 4 }],
};
const version = (over: Partial<VersionSummary> = {}): VersionSummary => ({
  id: 7, name: 'Team draft', status: 'Draft', isBaseline: false, isActive: false, isPreviousActive: false, rulesHash: 'h1',
  changeSummary: null, basedOnVersionId: 1, createdAtUtc: '2026-11-11T09:00:00Z', updatedAtUtc: '2026-11-11T09:30:00Z',
  publishedAtUtc: null, previewedAtUtc: null, previewCurrent: false, criteriaCount: 1, enabledCount: 1, identicalToBaseline: false,
  revision: 3, createdByEmail: 'rules.editor@poweredison.com', updatedByEmail: 'second.editor@ev-edison.com', publishedByEmail: null, ...over,
});
const detail = (over: Partial<VersionSummary> = {}): VersionDetail => ({
  version: version(over), rules: { schemaVersion: 1, criteria: [port] }, criteria: [], minPossible: 0, maxPossible: 4,
  changesVsActive: [], issues: [], preview: null,
});

describe('MethodologyEditorPage (shared editing)', () => {
  function render(opts: { canEdit?: boolean; save?: () => unknown } = {}) {
    const editing = signal({ enabled: opts.canEdit ?? true, reason: opts.canEdit === false ? 'Scoring rules are read-only on this deployment.' : null });
    const calls = { saves: [] as unknown[][], loads: 0 };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: MethodologyStore, useValue: { versionId: signal(1), editing, canEdit: computed(() => editing().enabled), refresh: () => {}, workingDraft: () => of(7) } },
        { provide: ApiService, useValue: {
          methodologyFields: () => of([]),
          methodologyVersion: () => { calls.loads++; return of(detail({ revision: 3 + calls.loads - 1, updatedByEmail: calls.loads > 1 ? 'second.editor@ev-edison.com' : 'rules.editor@poweredison.com' })); },
          validateRules: () => of({ valid: true, issues: [], minPossible: 0, maxPossible: 4 }),
          methodologyChanges: () => of({ total: 0, page: 1, pageSize: 25, entries: [] }),
          saveDraft: (...args: unknown[]) => { calls.saves.push(args); return opts.save ? opts.save() : of(detail({ revision: 4 })); },
        } },
      ],
    });
    const f = TestBed.createComponent(MethodologyEditorPage);
    f.componentRef.setInput('id', 7);
    f.detectChanges();
    return { f, el: f.nativeElement as HTMLElement, calls };
  }
  const rename = (f: { nativeElement: HTMLElement; detectChanges(): void }, to: string) => {
    const input = f.nativeElement.querySelector('input.title') as HTMLInputElement;
    input.value = to;
    input.dispatchEvent(new Event('input'));
    f.detectChanges();
  };
  const saveButton = (el: HTMLElement) => [...el.querySelectorAll('button')].find(b => /Save methodology as draft/.test(b.textContent!)) as HTMLButtonElement;

  it('shows who created and last saved the draft, and sends the revision it was loaded with', () => {
    const { f, el, calls } = render();
    expect(el.querySelector('.attrib')!.textContent).toContain('rules.editor@poweredison.com');
    expect(el.querySelector('.attrib')!.textContent).toContain('revision 3');
    rename(f, 'Renamed');
    saveButton(el).click();
    f.detectChanges();
    expect(calls.saves.length).toBe(1);
    expect(calls.saves[0][4]).toBe(3);   // expectedRevision
  });

  it('on a concurrent-edit conflict keeps the user\'s edits, explains who changed it and offers to reload', () => {
    const conflict = new HttpErrorResponse({ status: 409, error: { title: 'This draft was changed by second.editor@ev-edison.com at 2026-11-11 09:40 UTC (revision 4) after you opened it. Your changes were NOT saved.', code: 'draft_conflict' } });
    const { f, el, calls } = render({ save: () => throwError(() => conflict) });
    rename(f, 'My edit');
    saveButton(el).click();
    f.detectChanges();
    const banner = el.querySelector('.conflict')!;
    expect(banner.getAttribute('role')).toBe('alert');
    expect(banner.textContent).toContain('second.editor@ev-edison.com');
    expect((el.querySelector('input.title') as HTMLInputElement).value).toBe('My edit');   // nothing lost yet

    const reload = [...banner.querySelectorAll('button')].find(b => b.textContent!.includes('Reload the latest version'))!;
    reload.click();
    f.detectChanges();
    expect(calls.loads).toBe(2);
    expect(el.querySelector('.conflict')).toBeNull();
    expect((el.querySelector('input.title') as HTMLInputElement).value).toBe('Team draft');
  });

  it('read-only deployments show the version without any editing action', () => {
    const { el } = render({ canEdit: false });
    expect(el.querySelector('.mm-actionbar')).toBeNull();
    expect(el.querySelector('input.title')).toBeNull();
    expect([...el.querySelectorAll('button')].some(b => /Delete draft/.test(b.textContent!))).toBe(false);
    expect(el.querySelector('.rl-edit')).toBeNull();
    expect(el.textContent).toContain('Scoring rules are read-only on this deployment.');
    expect(el.querySelector('app-change-log')).not.toBeNull();   // history stays visible
  });
});
