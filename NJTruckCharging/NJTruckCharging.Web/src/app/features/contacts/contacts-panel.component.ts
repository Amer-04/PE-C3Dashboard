import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { RecordedByStore } from '../../core/recorded-by';
import {
  CONTACT_TYPES, ContactType, ContactVerificationStatus, SiteContact, SiteContactState, SiteContactSummary,
} from '../../core/models';

/**
 * Researched decision-maker contacts for one site: add / edit / verify / delete, with a clear
 * four-state distinction (verified decision-maker · potential · nearby carrier only · none).
 * Reusable via the [siteId] input (dashboard detail and the saved workflow).
 */
@Component({
  selector: 'app-contacts-panel',
  imports: [FormsModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="contacts">
      <div class="contacts-head">
        <h3>Decision-maker contacts</h3>
        @if (!formOpen()) { <button type="button" class="btn-ghost small" (click)="openAdd()">+ Add contact</button> }
      </div>

      @if (summary(); as s) {
        <div class="contact-state" [attr.data-state]="s.state">{{ stateLabel(s.state) }}</div>
      }
      @if (error()) { <div class="banner error small" role="alert">{{ error() }}</div> }

      @if (formOpen()) {
        <form class="contact-form" (ngSubmit)="save()">
          <div class="cf-row">
            <label class="field"><span>Name *</span><input type="text" name="name" [(ngModel)]="name" required /></label>
            <label class="field"><span>Type</span>
              <select name="type" [(ngModel)]="contactType">
                @for (t of types; track t.value) { <option [value]="t.value">{{ t.label }}</option> }
              </select>
            </label>
          </div>
          <div class="cf-row">
            <label class="field"><span>Organization</span><input type="text" name="org" [(ngModel)]="organization" /></label>
            <label class="field"><span>Job title</span><input type="text" name="title" [(ngModel)]="jobTitle" /></label>
          </div>
          <div class="cf-row">
            <label class="field"><span>Business phone</span><input type="text" name="phone" [(ngModel)]="businessPhone" /></label>
            <label class="field"><span>Professional email</span><input type="email" name="email" [(ngModel)]="email" /></label>
          </div>
          <label class="field"><span>Source URL (evidence){{ verificationStatus() === 'Verified' ? ' *' : '' }}</span><input type="url" name="src" [(ngModel)]="sourceUrl" placeholder="https://… where the name, role and phone/email are published" /></label>
          <label class="field"><span>Verification</span>
            <select name="vs" [(ngModel)]="verificationStatus">
              <option value="Unverified">Potential — needs verification</option>
              <option value="Verified">Verified decision-maker</option>
              <option value="Invalid">Checked — not valid</option>
            </select>
          </label>
          <label class="field"><span>Research notes</span><textarea rows="2" name="notes" [(ngModel)]="researchNotes" placeholder="How this was found / confirmed…"></textarea></label>
          <div class="cf-actions">
            @if (verificationStatus() === 'Verified' && !sourceUrl().trim()) {
              <small class="hint">A source URL is required to mark a contact Verified.</small>
            }
            <button type="submit" class="btn primary small" [disabled]="!name().trim() || busy() || (verificationStatus() === 'Verified' && !sourceUrl().trim())">{{ editingId() ? 'Save contact' : 'Add contact' }}</button>
            <button type="button" class="btn-ghost small" (click)="cancel()">Cancel</button>
          </div>
        </form>
      }

      @for (c of contacts(); track c.id) {
        <div class="contact-card" [attr.data-vs]="c.verificationStatus">
          <div class="cc-top">
            <div>
              <b>{{ c.name }}</b> <span class="vs-badge" [attr.data-vs]="c.verificationStatus">{{ vsLabel(c.verificationStatus) }}</span>
              @if (!isRelevant(c.contactType)) { <span class="vs-badge" title="Not a site decision-maker role">General contact</span> }
              <div class="muted small">{{ typeLabel(c.contactType) }}@if (c.jobTitle) { · {{ c.jobTitle }} }@if (c.organization) { · {{ c.organization }} }</div>
            </div>
          </div>
          <dl class="facts small">
            @if (c.businessPhone) { <dt>Phone</dt><dd><a [href]="'tel:' + c.businessPhone">{{ c.businessPhone }}</a></dd> }
            @if (c.email) { <dt>Email</dt><dd><a [href]="'mailto:' + c.email">{{ c.email }}</a></dd> }
            @if (c.sourceUrl) { <dt>Source</dt><dd><a [href]="c.sourceUrl" target="_blank" rel="noopener">evidence ↗</a></dd> }
            @if (!c.sourceUrl) { <dt>Source</dt><dd class="muted">none recorded</dd> }
            @if (c.verificationStatus === 'Verified' && c.lastVerifiedUtc) { <dt>Verified</dt><dd>{{ c.lastVerifiedUtc | date: 'MMM d, y' }}@if (c.verifiedBy) { by {{ c.verifiedBy }} }</dd> }
            <dt>Last checked</dt><dd>{{ (c.lastVerifiedUtc ?? c.updatedAtUtc) | date: 'MMM d, y' }}</dd>
          </dl>
          @if (c.researchNotes) { <p class="small cc-notes">{{ c.researchNotes }}</p> }
          <div class="cc-actions">
            @if (c.verificationStatus !== 'Verified') {
              <button type="button" class="btn-ghost small" (click)="verify(c)" [title]="c.sourceUrl ? 'Mark verified' : 'Add a source URL first'">✓ Mark verified</button>
            }
            <button type="button" class="btn-ghost small" (click)="openEdit(c)">Edit</button>
            <button type="button" class="btn-ghost small danger" (click)="remove(c)">Delete</button>
          </div>
        </div>
      } @empty {
        @if (!formOpen()) {
          <p class="muted small">No researched contacts yet.
            @if (summary()?.nearbyCarriers) { Nearby carriers are listed below — they are near the site, <b>not confirmed to operate on it</b>. }
            Use “Add contact” once you’ve identified a decision-maker.</p>
          <p class="hint">Look for the property owner, an industrial broker, or the facilities, sustainability or energy-efficiency manager.
            A general company phone is not a verified site decision-maker.</p>
        }
      }
    </div>
  `,
})
export class ContactsPanelComponent {
  private readonly api = inject(ApiService);
  private readonly recordedBy = inject(RecordedByStore);
  protected readonly types = CONTACT_TYPES;

  readonly siteId = input.required<string>();
  /** Emits after a contact is added, edited or removed so hosts can refresh summaries. */
  readonly changed = output<void>();

  protected readonly contacts = signal<SiteContact[]>([]);
  protected readonly summary = signal<SiteContactSummary | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editingId = signal<number | null>(null);

  // Editable form fields.
  protected readonly name = signal('');
  protected readonly organization = signal('');
  protected readonly jobTitle = signal('');
  protected readonly contactType = signal<ContactType>('PropertyOwner');
  protected readonly businessPhone = signal('');
  protected readonly email = signal('');
  protected readonly sourceUrl = signal('');
  protected readonly verificationStatus = signal<ContactVerificationStatus>('Unverified');
  protected readonly researchNotes = signal('');

  readonly verifiedCount = computed(() => this.summary()?.verified ?? 0);

  constructor() {
    effect(() => { const id = this.siteId(); if (id) this.load(id); });
  }

  private load(siteId: string) {
    this.formOpen.set(false);
    this.api.contacts(siteId).subscribe({ next: c => this.contacts.set(c), error: e => this.error.set(describe(e)) });
    this.api.contactSummary(siteId).subscribe({ next: s => this.summary.set(s), error: () => {} });
  }

  protected openAdd() {
    this.editingId.set(null);
    this.name.set(''); this.organization.set(''); this.jobTitle.set('');
    this.contactType.set('PropertyOwner'); this.businessPhone.set(''); this.email.set('');
    this.sourceUrl.set(''); this.verificationStatus.set('Unverified'); this.researchNotes.set('');
    this.error.set(null);
    this.formOpen.set(true);
  }

  protected openEdit(c: SiteContact) {
    this.editingId.set(c.id);
    this.name.set(c.name); this.organization.set(c.organization ?? ''); this.jobTitle.set(c.jobTitle ?? '');
    this.contactType.set(c.contactType); this.businessPhone.set(c.businessPhone ?? ''); this.email.set(c.email ?? '');
    this.sourceUrl.set(c.sourceUrl ?? ''); this.verificationStatus.set(c.verificationStatus); this.researchNotes.set(c.researchNotes ?? '');
    this.error.set(null);
    this.formOpen.set(true);
  }

  protected cancel() { this.formOpen.set(false); }

  protected save() {
    if (!this.name().trim() || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    const body = {
      name: this.name().trim(),
      organization: nn(this.organization()), jobTitle: nn(this.jobTitle()),
      contactType: this.contactType(), businessPhone: nn(this.businessPhone()),
      email: nn(this.email()), sourceUrl: nn(this.sourceUrl()),
      verificationStatus: this.verificationStatus(), researchNotes: nn(this.researchNotes()),
      recordedBy: this.recordedBy.value(),
    };
    const id = this.editingId();
    const req$ = id
      ? this.api.updateContact(id, body)
      : this.api.createContact({ siteId: this.siteId(), ...body });
    req$.subscribe({
      next: () => { this.busy.set(false); this.load(this.siteId()); this.changed.emit(); },
      error: e => { this.busy.set(false); this.error.set(describe(e)); },
    });
  }

  protected isRelevant(t: ContactType) {
    return t === 'PropertyOwner' || t === 'Broker' || t === 'FacilityManager' || t === 'SustainabilityManager' || t === 'EnergyEfficiencyManager';
  }

  protected verify(c: SiteContact) {
    // Verification needs a cited source: open the editor instead of claiming it without one.
    if (!c.sourceUrl) { this.openEdit(c); this.verificationStatus.set('Verified'); this.error.set('Add the source URL where this contact is published, then save.'); return; }
    this.api.updateContact(c.id, {
      name: c.name, organization: c.organization, jobTitle: c.jobTitle, contactType: c.contactType,
      businessPhone: c.businessPhone, email: c.email, sourceUrl: c.sourceUrl,
      verificationStatus: 'Verified', researchNotes: c.researchNotes, recordedBy: this.recordedBy.value(),
    }).subscribe({ next: () => { this.load(this.siteId()); this.changed.emit(); }, error: e => this.error.set(describe(e)) });
  }

  protected remove(c: SiteContact) {
    this.api.deleteContact(c.id).subscribe({ next: () => { this.load(this.siteId()); this.changed.emit(); }, error: e => this.error.set(describe(e)) });
  }

  protected typeLabel(t: ContactType) { return CONTACT_TYPES.find(x => x.value === t)?.label ?? t; }
  protected vsLabel(v: ContactVerificationStatus) { return v === 'Verified' ? 'Verified' : v === 'Invalid' ? 'Invalid' : 'Potential'; }
  protected stateLabel(s: SiteContactState) {
    switch (s) {
      case 'VerifiedDecisionMaker': return '✓ Verified site decision-maker';
      case 'PotentialUnverified': return '◐ Potential contact — needs verification';
      case 'NearbyCarrierOnly': return '○ Only nearby carriers known (not confirmed on-site)';
      default: return '○ No contact identified';
    }
  }
}

function nn(v: string): string | null { const t = v.trim(); return t ? t : null; }
function describe(err: unknown): string {
  const e = err as { status?: number; error?: unknown; message?: string };
  if (e?.status === 0) return 'Cannot reach the API.';
  if (typeof e?.error === 'string' && e.error) return e.error;
  return e?.message ?? 'Could not save the contact.';
}
