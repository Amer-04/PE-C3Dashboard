import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { CONTACT_READINESS, SiteSuitability, SiteValidation, SuitabilityAnswer, SuitabilityBasis } from '../../core/models';

const ANSWER: Record<SuitabilityAnswer, { icon: string; label: string }> = {
  Yes: { icon: '✓', label: 'Yes' },
  NeedsCheck: { icon: '!', label: 'Check' },
  No: { icon: '✕', label: 'No' },
  Unknown: { icon: '?', label: 'Unknown' },
};
const BASIS: Record<SuitabilityBasis, string> = {
  Verified: 'Verified',
  Estimate: 'Estimate',
  SourceData: 'Public records',
  Unverified: 'Unverified lead',
  Missing: 'No data',
};

/**
 * "At a glance" answers to the business owner's questions for one site, with where each answer comes from and
 * what still has to be verified. Informational only — it does not score, rank or decide.
 */
@Component({
  selector: 'app-site-summary',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (summary(); as s) {
      <section class="suit" aria-label="Site suitability at a glance">
        <div class="suit-head">
          <h3>At a glance</h3>
          <span class="suit-counts small">
            <b>{{ s.verifiedAnswers }}</b> of {{ s.items.length }} answers verified ·
            {{ s.yes }} yes · {{ s.needsCheck }} to check · {{ s.no }} no · {{ s.unknown }} unknown
          </span>
        </div>
        @if (validation(); as v) {
          <div class="val" aria-label="Who and what is confirmed">
            <div class="val-head">
              <b>What is confirmed</b>
              <span class="small">
                Site evidence <span class="rr-badge" [attr.data-ev]="v.siteEvidence">{{ v.siteEvidence }}</span>
                · Contact <span class="rr-badge" [attr.data-cr]="v.contactReadiness">{{ readiness(v.contactReadiness) }}</span>
              </span>
            </div>
            <div class="val-grid">
              @for (sec of v.sections; track sec.key) {
                <section class="val-sec" [attr.data-key]="sec.key" [attr.data-st]="sec.status">
                  <div class="val-sec-head">
                    <span class="val-title">{{ sec.title }}</span>
                    <span class="val-st" [attr.data-st]="sec.status">{{ sec.statusLabel }}</span>
                  </div>
                  <dl class="val-facts">
                    @for (f of sec.facts; track $index) { <dt>{{ f.label }}</dt><dd>{{ f.value }}</dd> }
                  </dl>
                  @if (sec.finding) { <p class="val-finding">{{ sec.finding }}</p> }
                  @if (sec.discrepancies.length) {
                    <ul class="val-disc">@for (d of sec.discrepancies; track d) { <li>⚠ {{ d }}</li> }</ul>
                  }
                  <div class="val-meta small">
                    <span>Checked: {{ sec.checkedAtUtc ? (sec.checkedAtUtc | date: 'MMM d, y') : 'never' }}</span>
                    @for (l of sec.evidenceLinks; track l) { · <a [href]="l" target="_blank" rel="noopener">evidence ↗</a> }
                  </div>
                </section>
              }
            </div>
          </div>
        }
        <ol class="suit-list">
          @for (i of s.items; track i.key; let n = $index) {
            <li [attr.data-a]="i.answer" [attr.data-key]="i.key">
              <button type="button" class="suit-row" (click)="toggle(i.key)" [attr.aria-expanded]="open().has(i.key)">
                <span class="suit-icon" [attr.data-a]="i.answer" [attr.aria-label]="answer(i.answer).label">{{ answer(i.answer).icon }}</span>
                <span class="suit-text">
                  <span class="suit-q">{{ n + 1 }}. {{ i.question }}</span>
                  <span class="suit-a">{{ i.headline }}</span>
                </span>
                <span class="suit-basis" [attr.data-b]="i.basis">{{ basis(i.basis) }}</span>
              </button>
              @if (open().has(i.key) && (i.detail || i.nextStep)) {
                <div class="suit-detail small">
                  @if (i.detail) { <p>{{ i.detail }}</p> }
                  @if (i.nextStep) { <p class="suit-next"><b>Next:</b> {{ i.nextStep }}</p> }
                </div>
              }
            </li>
          }
        </ol>
        @if (s.preferences.length) {
          <div class="suit-prefs" aria-label="Preliminary business preferences">
            <div class="suit-prefs-head">
              <b>Business preferences</b>
              <span class="small muted">{{ s.preferencesMet }} of {{ s.preferences.length }} met · preliminary, not part of the C3 score</span>
            </div>
            <ul>
              @for (p of s.preferences; track p.key) {
                <li [attr.data-key]="p.key" [title]="p.detail">
                  <span class="suit-icon sm" [attr.data-a]="p.answer" [attr.aria-label]="answer(p.answer).label">{{ answer(p.answer).icon }}</span>
                  <span class="suit-text"><span class="suit-q">{{ p.question.replace('Preference: ', '') }}</span><span class="suit-a">{{ p.headline }}</span></span>
                  <span class="suit-basis" [attr.data-b]="p.basis">{{ basis(p.basis) }}</span>
                </li>
              }
            </ul>
          </div>
        }
        @if (s.remainingToVerify.length) {
          <div class="suit-remaining">
            <b>9. What remains to be verified</b>
            <ul>@for (r of s.remainingToVerify; track r) { <li>{{ r }}</li> }</ul>
          </div>
        } @else {
          <div class="suit-remaining done"><b>9. What remains to be verified:</b> nothing — every answer is verified.</div>
        }
        <p class="hint">Estimates and public records are starting points, not confirmations. Click a row for the details.
          Record checks in the feasibility review below; nothing here changes the C3 score.</p>
      </section>
    }
  `,
})
export class SiteSummaryComponent {
  readonly summary = input<SiteSuitability | null | undefined>(null);
  /** Parcel / business / owner / suitability, kept separate (optional so existing hosts keep working). */
  readonly validation = input<SiteValidation | null | undefined>(null);
  protected readonly readiness = (r: string) => CONTACT_READINESS.find(x => x.value === r)?.label ?? r;
  protected readonly open = signal<Set<string>>(new Set());

  protected readonly answer = (a: SuitabilityAnswer) => ANSWER[a] ?? ANSWER.Unknown;
  protected readonly basis = (b: SuitabilityBasis) => BASIS[b] ?? b;

  protected toggle(key: string) {
    this.open.update(s => { const n = new Set(s); n.has(key) ? n.delete(key) : n.add(key); return n; });
  }

  /** Exposed for tests: number of rows with a given answer. */
  readonly answerCount = computed(() => this.summary()?.items.length ?? 0);
}
