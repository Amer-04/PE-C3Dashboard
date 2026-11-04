import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CONTACT_TYPES, SiteListItem } from '../../core/models';

/**
 * "Contact readiness" cell of the ranked table. Shows, from the row the table already loaded (no extra requests):
 *  1. the most relevant researched contact (verified first), when one exists;
 *  2. the business listing imported with the C3 data — the same fields Site Details → "Who to contact" shows
 *     (business names, listed phone, owner mailing address). The listing is labelled "Unverified Business Listing".
 * Only recorded values are shown; a missing field reads "Not available".
 */
@Component({
  selector: 'app-contact-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (site(); as s) {
      @if (s.topContact; as c) {
        <div class="cc-cell" [attr.data-verified]="c.verified">
          <div class="cc-line">
            <b class="cc-name" [title]="c.name">{{ c.name }}</b>
            <span class="rr-badge" [attr.data-cr]="c.verified ? (relevant() ? 'RelevantContactVerified' : 'GeneralBusinessContact') : 'UnverifiedLead'"
                  [title]="badgeHint()">{{ badge() }}</span>
          </div>
          <div class="cc-sub" [title]="subtitle()">{{ subtitle() }}</div>
          <div class="cc-links">
            @if (c.email) {
              <a [href]="'mailto:' + c.email" (click)="$event.stopPropagation()" [title]="c.email">{{ c.email }}</a>
            } @else { <span class="cc-missing">Email not available</span> }
            @if (c.phone) {
              <a [href]="'tel:' + tel(c.phone)" (click)="$event.stopPropagation()">{{ c.phone }}</a>
            } @else { <span class="cc-missing">Phone not available</span> }
          </div>
          @if (c.otherContacts > 0) { <div class="cc-more">+{{ c.otherContacts }} more in Site Details</div> }
        </div>
      }

      @if (hasListing()) {
        <div class="cc-listing" [class.after]="!!s.topContact">
          <span class="cc-listing-tag" [attr.data-confirmed]="s.businessListingConfirmed"
                [title]="s.businessListingConfirmed
                  ? 'A reviewer confirmed this business operates at the address; the listed phone is still unverified'
                  : 'Imported with the C3 data (business listing + parcel record); not verified by the team'">{{
              s.businessListingConfirmed ? 'Business confirmed at address · phone unverified' : 'Unverified Business Listing' }}</span>
          <dl class="cc-dl">
            <dt>Business</dt>
            <dd class="cc-biz-names" [title]="s.businesses ?? ''">@if (s.businesses) {{{ s.businesses }}} @else {<span class="cc-missing">Not available</span>}</dd>
            <dt>Phone</dt>
            <dd>@if (s.businessPhone; as bp) {<a [href]="'tel:' + tel(bp)" (click)="$event.stopPropagation()" [title]="bp">{{ pretty(bp) }}</a>} @else {<span class="cc-missing">Not available</span>}</dd>
            <dt>Email</dt>
            <dd><span class="cc-missing">Not available</span></dd>
            <dt>Owner mail</dt>
            <dd [title]="ownerMail() ?? ''">@if (ownerMail(); as m) {{{ m }}} @else {<span class="cc-missing">Not available</span>}</dd>
          </dl>
        </div>
      } @else if (!s.topContact) {
        <span class="cc-none">No Contact — Email and phone unavailable.</span>
      }
    }
  `,
})
export class ContactCellComponent {
  /** The table row (already loaded with the page — no per-row API calls). */
  readonly site = input<SiteListItem | null | undefined>(null);

  private static readonly RELEVANT = ['PropertyOwner', 'Broker', 'FacilityManager', 'SustainabilityManager', 'EnergyEfficiencyManager'];
  private readonly contact = computed(() => this.site()?.topContact ?? null);
  protected readonly relevant = computed(() => !!this.contact() && ContactCellComponent.RELEVANT.includes(this.contact()!.role));

  /** Owner mailing address as Site Details shows it: "address, city zip". */
  protected readonly ownerMail = computed(() => {
    const s = this.site();
    if (!s) return null;
    const line = [s.ownerMailAddress, s.ownerMailCity].filter(x => !!x && x.trim()).join(', ');
    const full = s.ownerMailZip?.trim() ? `${line} ${s.ownerMailZip.trim()}`.trim() : line;
    return full || null;
  });
  protected readonly hasListing = computed(() => {
    const s = this.site();
    return !!s && (!!s.businesses || !!s.businessPhone || !!this.ownerMail());
  });

  protected readonly badge = computed(() => {
    const c = this.contact();
    if (!c) return '';
    return c.verified ? (this.relevant() ? 'Verified' : 'Verified · general') : 'Unverified lead';
  });
  protected readonly badgeHint = computed(() => {
    const c = this.contact();
    if (!c) return '';
    if (!c.verified) return 'Recorded by the team but not verified from a cited source';
    return this.relevant() ? 'Verified decision-making role, with a cited source' : 'Verified, but not a site decision-maker (general business contact)';
  });
  protected readonly subtitle = computed(() => {
    const c = this.contact();
    if (!c) return '';
    const role = CONTACT_TYPES.find(t => t.value === c.role)?.label ?? c.role;
    return [c.jobTitle || role, c.organization].filter(Boolean).join(' · ');
  });

  /** Dial string for tel: links (digits, plus sign and extension marker only). */
  protected tel(phone: string) { return phone.replace(/[^\d+x,;]/gi, ''); }

  /** Display-only formatting of US numbers (+1XXXXXXXXXX / XXXXXXXXXX); anything else is shown as recorded. */
  protected pretty(phone: string) {
    const d = phone.replace(/\D/g, '');
    const n = d.length === 11 && d.startsWith('1') ? d.slice(1) : d;
    return n.length === 10 ? `(${n.slice(0, 3)}) ${n.slice(3, 6)}-${n.slice(6)}` : phone;
  }
}
