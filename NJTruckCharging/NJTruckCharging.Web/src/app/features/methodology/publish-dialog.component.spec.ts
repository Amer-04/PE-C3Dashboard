import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PreviewSummary } from '../../core/methodology.models';
import { PublishDialogComponent } from './publish-dialog.component';

const preview = (versionId: number): PreviewSummary => ({
  versionId, versionName: 'Higher port points', rulesHash: 'HASH-' + versionId, dataVersion: 1, computedAtUtc: '2026-11-10T00:00:00Z', elapsedMs: 10,
  sites: 30584, comparedToVersionId: 1, comparedToName: 'Original C3 workbook methodology', identicalToActive: false,
  scoresChanged: 1200, ranksChanged: 3400, maxIncrease: 2, maxDecrease: 0, meanAbsChange: 0.1, topN: 10, segments: [], topRisers: [], topFallers: [],
  criteria: [], changes: [], warnings: [],
} as PreviewSummary);

describe('PublishDialogComponent (Apply Scoring confirmation)', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function render(versionId: number, restore = false) {
    const f = TestBed.createComponent(PublishDialogComponent);
    f.componentRef.setInput('preview', preview(versionId));
    f.componentRef.setInput('restore', restore);
    f.detectChanges();
    return f;
  }
  const el = (f: { nativeElement: HTMLElement }) => f.nativeElement;
  const phraseBox = (f: { nativeElement: HTMLElement }) => el(f).querySelector('#pub-phrase') as HTMLInputElement;
  const type = (f: { nativeElement: HTMLElement; detectChanges(): void }, v: string) => {
    phraseBox(f).value = v; phraseBox(f).dispatchEvent(new Event('input')); f.detectChanges();
  };
  const confirmBtn = (f: { nativeElement: HTMLElement }) => el(f).querySelectorAll('.actions button')[1] as HTMLButtonElement;

  it('asks for APPLY v{version}, built from the reviewed version (not hard-coded)', () => {
    for (const id of [6, 9, 12]) {
      const f = render(id);
      expect(el(f).querySelector('code')!.textContent).toBe(`APPLY v${id}`);
      expect(el(f).querySelector('#pub-title')!.textContent).toContain('Apply Scoring');
      expect(confirmBtn(f).textContent!.trim()).toBe('Apply Scoring');
      f.destroy();
    }
  });

  it('explains that applying recalculates every site\'s score and ranking, and what is not changed', () => {
    const f = render(6);
    const text = el(f).querySelector('#pub-what')!.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('recalculates the score and ranking of all 30,584 candidate sites');
    expect(text).toContain('1,200 scores and 3,400 rankings change');
    expect(el(f).querySelector('#pub-kept')!.textContent).toContain('not changed');
    expect(el(f).textContent).not.toMatch(/publish/i);
  });

  it('keeps the exact typed confirmation: the old PUBLISH phrase and partial text are refused', () => {
    const f = render(6);
    expect(confirmBtn(f).disabled).toBe(true);
    for (const wrong of ['PUBLISH v6', 'APPLY', 'APPLY v7', 'APPLY v66']) {
      type(f, wrong);
      expect(confirmBtn(f).disabled).toBe(true);
    }
    expect(el(f).querySelector('#pub-hint')!.textContent).toContain('type APPLY v6');
    type(f, '  apply v6 ');                      // case and surrounding spaces are forgiven, the words are not
    expect(confirmBtn(f).disabled).toBe(false);
  });

  it('sends the reviewed rules and expected active version so the server can refuse stale results', () => {
    const f = render(6);
    type(f, 'APPLY v6');
    confirmBtn(f).click();
    const req = http.expectOne('/api/methodology/versions/6/publish');
    expect(req.request.body).toEqual({ confirm: true, expectedActiveVersionId: 1, rulesHash: 'HASH-6', note: null });
    req.flush({ title: 'The rules changed after the results were reviewed. Review Results again before applying scoring.' }, { status: 409, statusText: 'Conflict' });
    f.detectChanges();
    expect(el(f).querySelector('.err[role="alert"]')!.textContent).toContain('Review Results again');
  });

  it('uses RESTORE v{version} for restoring a previous method', () => {
    const f = render(1, true);
    expect(el(f).querySelector('code')!.textContent).toBe('RESTORE v1');
    expect(confirmBtn(f).textContent!.trim()).toBe('Restore version 1');
  });

  it('is an accessible modal: labelled, described, Escape and Cancel close it without sending anything', () => {
    const f = render(6);
    const dlg = el(f).querySelector('[role="dialog"]')!;
    expect(dlg.getAttribute('aria-modal')).toBe('true');
    expect(dlg.getAttribute('aria-labelledby')).toBe('pub-title');
    expect(dlg.getAttribute('aria-describedby')).toBe('pub-what pub-kept');
    expect((el(f).querySelector('label[for="pub-phrase"]') as HTMLLabelElement).textContent).toContain('To confirm, type');
    let cancelled = 0;
    f.componentInstance.cancel.subscribe(() => cancelled++);
    dlg.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    (el(f).querySelectorAll('.actions button')[0] as HTMLButtonElement).click();
    expect(cancelled).toBe(2);
    http.expectNone('/api/methodology/versions/6/publish');
  });
});
