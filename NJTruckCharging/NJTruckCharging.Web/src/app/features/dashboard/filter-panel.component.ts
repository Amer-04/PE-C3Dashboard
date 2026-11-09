import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { CONTACT_READINESS, RESEARCH_STAGES, SCREENING_DECISIONS, SEGMENT_COUNTIES, SITE_EVIDENCE, SiteFilter } from '../../core/models';
import { SiteStore } from '../../core/site-store';
import { MethodologyStore } from '../../core/methodology-store';

@Component({
  selector: 'app-filter-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <aside class="panel filters" aria-label="Filters">
      <div class="panel-head">
        <h2>Filters</h2>
        @if (store.activeFilterCount() > 0) {
          <button type="button" class="btn-ghost small" (click)="store.reset()">Clear ({{ store.activeFilterCount() }})</button>
        }
      </div>

      <label class="field">
        <span>Search</span>
        <input type="search" placeholder="Address, town, business, site id"
               [value]="f().search ?? ''" (input)="store.patch({ search: val($event) || null })" />
      </label>

      <fieldset class="field">
        <legend>NOFA segment</legend>
        <div class="chips">
          @for (s of segments; track s) {
            <button type="button" class="chip" [class.on]="f().segments.includes(s)"
                    [attr.aria-pressed]="f().segments.includes(s)" [title]="counties[s]"
                    (click)="store.toggleSegment(s)">{{ s }}</button>
          }
        </div>
        <small class="hint">Scores are percentile-ranked within each segment.</small>
      </fieldset>

      <label class="field">
        <span>Shortlist</span>
        <select [value]="f().topN ?? ''" (change)="store.patch({ topN: num($event) })">
          <option value="">All candidates</option>
          <option value="10">Top 10 per segment</option>
          <option value="25">Top 25 per segment</option>
          <option value="50">Top 50 per segment</option>
          <option value="100">Top 100 per segment</option>
          <option value="150">Top 150 per segment (flood-checked)</option>
        </select>
      </label>

      <label class="field">
        <span>Minimum score <b>{{ f().minScore ?? 0 }}</b></span>
        <input type="range" min="0" [max]="methodology.maxPossible()" step="5" [value]="f().minScore ?? 0"
               (change)="store.patch({ minScore: +val($event) || null })" />
      </label>

      <fieldset class="field">
        <legend>Property class</legend>
        <div class="chips">
          @for (c of classes; track c.key) {
            <button type="button" class="chip" [class.on]="f().propertyClasses.includes(c.key)"
                    [attr.aria-pressed]="f().propertyClasses.includes(c.key)" (click)="toggleClass(c.key)">{{ c.label }}</button>
          }
        </div>
      </fieldset>

      <div class="field-row">
        <label class="field">
          <span>Min open acres</span>
          <input type="number" min="0" step="0.5" [value]="f().minOpenAcres ?? ''" (change)="store.patch({ minOpenAcres: num($event) })" />
        </label>
        <label class="field">
          <span>Max mi to exit</span>
          <input type="number" min="0" max="5" step="0.25" [value]="f().maxMilesToExit ?? ''" (change)="store.patch({ maxMilesToExit: num($event) })" />
        </label>
      </div>

      <label class="field">
        <span>Trucking on site</span>
        <select [value]="f().freightOnSite === null ? '' : f().freightOnSite ? 'yes' : 'no'"
                (change)="store.patch({ freightOnSite: val($event) === '' ? null : val($event) === 'yes' })">
          <option value="">Any</option>
          <option value="yes">Freight / logistics business on parcel</option>
          <option value="no">No freight business</option>
        </select>
      </label>

      <fieldset class="field">
        <legend>Screening decision</legend>
        <div class="chips">
          @for (o of screeningOptions; track o.token) {
            <button type="button" class="chip" [class.on]="f().screeningDecisions.includes(o.token)"
                    [attr.aria-pressed]="f().screeningDecisions.includes(o.token)" [title]="o.hint" (click)="toggleScreening(o.token)">{{ o.label }}</button>
          }
        </div>
      </fieldset>

      <fieldset class="field">
        <legend>Site evidence</legend>
        <div class="chips">
          @for (o of evidenceOptions; track o.token) {
            <button type="button" class="chip" [class.on]="f().evidenceStates.includes(o.token)"
                    [attr.aria-pressed]="f().evidenceStates.includes(o.token)" [title]="o.hint" (click)="toggle('evidenceStates', o.token)">{{ o.label }}</button>
          }
        </div>
      </fieldset>

      <fieldset class="field">
        <legend>Contact readiness</legend>
        <div class="chips">
          @for (o of contactOptions; track o.token) {
            <button type="button" class="chip" [class.on]="f().contactStates.includes(o.token)"
                    [attr.aria-pressed]="f().contactStates.includes(o.token)" [title]="o.hint" (click)="toggle('contactStates', o.token)">{{ o.short }}</button>
          }
        </div>
        <small class="hint">From the team's research records only — a high C3 score never verifies a site or contact.</small>
      </fieldset>

      <fieldset class="field">
        <legend>Research completeness</legend>
        <div class="chips">
          @for (o of researchOptions; track o.token) {
            <button type="button" class="chip" [class.on]="f().researchStages.includes(o.token)"
                    [attr.aria-pressed]="f().researchStages.includes(o.token)" [title]="o.hint" (click)="toggle('researchStages', o.token)">{{ o.label }}</button>
          }
        </div>
        <small class="hint">Eight core verification checks. Separate from the C3 score.</small>
      </fieldset>

      <fieldset class="field">
        <legend>IOS classification</legend>
        <div class="chips">
          @for (o of iosOptions; track o.key) {
            <button type="button" class="chip" [class.on]="f().iosClasses.includes(o.key)"
                    [attr.aria-pressed]="f().iosClasses.includes(o.key)" [title]="o.hint" (click)="toggleIos(o.key)">{{ o.label }}</button>
          }
        </div>
        <small class="hint">Only <b>Confirmed</b> is manually verified (site review with evidence). Freight indicated &amp; Potential are inferred from source data.</small>
      </fieldset>

      <label class="field">
        <span>Min est. truck parking <small class="muted">(preliminary)</small></span>
        <input type="number" min="0" step="5" placeholder="trailers" [value]="f().minEstimatedParking ?? ''"
               (change)="store.patch({ minEstimatedParking: num($event) })" />
        <small class="hint">Rough screening estimate from open acres (~15 trailers/acre) — not verified, and not all may suit tractor-trailers.</small>
      </label>

      <fieldset class="field">
        <legend>Evidence-backed only</legend>
        <label class="check"><input type="checkbox" [checked]="f().truckEntranceVerified" (change)="store.patch({ truckEntranceVerified: chk($event) })" /> Truck entrance verified</label>
        <label class="check"><input type="checkbox" [checked]="f().electricalVerified" (change)="store.patch({ electricalVerified: chk($event) })" /> Electrical service verified</label>
        <label class="check"><input type="checkbox" [checked]="f().parkingVerified" (change)="store.patch({ parkingVerified: chk($event) })" /> Parking capacity verified</label>
        <label class="check"><input type="checkbox" [checked]="f().onlyWithVerifiedContact" (change)="store.patch({ onlyWithVerifiedContact: chk($event) })" /> Verified decision-maker on record</label>
        <small class="hint">Narrows to sites your team has researched and verified.</small>
      </fieldset>

      <fieldset class="field">
        <legend>Exclude</legend>
        <label class="check"><input type="checkbox" [checked]="f().excludeFloodZone" (change)="store.patch({ excludeFloodZone: chk($event) })" /> In FEMA 100-yr flood zone</label>
        <label class="check"><input type="checkbox" [checked]="f().excludeContaminated" (change)="store.patch({ excludeContaminated: chk($event) })" /> Known contaminated site</label>
        <label class="check"><input type="checkbox" [checked]="f().excludeHighlandsPreservation" (change)="store.patch({ excludeHighlandsPreservation: chk($event) })" /> Highlands Preservation Area</label>
        <label class="check"><input type="checkbox" [checked]="f().excludeNearPowerEdison" (change)="store.patch({ excludeNearPowerEdison: chk($event) })" /> Within 2 mi of a Power Edison site</label>
        <label class="check"><input type="checkbox" [checked]="f().excludeBusyMultiTenant" (change)="store.patch({ excludeBusyMultiTenant: chk($event) })" /> Busy multi-tenant (6+ businesses)</label>
        <label class="check"><input type="checkbox" [checked]="f().onlyFloodChecked" (change)="store.patch({ onlyFloodChecked: chk($event) })" /> Only flood-checked sites</label>
      </fieldset>

      <label class="field">
        <span>County</span>
        <select [value]="f().county ?? ''" (change)="store.patch({ county: val($event) || null })">
          <option value="">All counties</option>
          @for (c of store.options().counties; track c) { <option [value]="c">{{ c }}</option> }
        </select>
      </label>

      <label class="field">
        <span>Interstate</span>
        <select [value]="f().highway ?? ''" (change)="store.patch({ highway: val($event) || null })">
          <option value="">All</option>
          @for (h of store.options().highways; track h) { <option [value]="h">{{ h }}</option> }
        </select>
      </label>

      <label class="field">
        <span>Electric utility</span>
        <select [value]="f().utility ?? ''" (change)="store.patch({ utility: val($event) || null })">
          <option value="">All utilities</option>
          @for (u of store.options().utilities; track u) { <option [value]="u">{{ u }}</option> }
        </select>
      </label>
    </aside>
  `,
})
export class FilterPanelComponent {
  protected readonly store = inject(SiteStore);
  protected readonly methodology = inject(MethodologyStore);
  protected readonly f = this.store.filter;
  protected readonly segments = [1, 2, 3, 4, 5, 6];
  protected readonly counties = SEGMENT_COUNTIES;
  protected readonly classes = [
    { key: 'industrial', label: 'Industrial' },
    { key: 'vacant', label: 'Vacant' },
    { key: 'commercial', label: 'Commercial' },
  ];
  protected readonly iosOptions = [
    { key: 'confirmed', label: 'Confirmed', hint: 'Manually confirmed outdoor-storage use (site review with evidence)' },
    { key: 'indicated', label: 'Freight indicated', hint: 'Source data: freight/logistics business listed on a parcel with open land — NOT verified IOS' },
    { key: 'potential', label: 'Potential', hint: 'Source data: industrial/vacant land with open acreage; use unknown' },
    { key: 'not', label: 'Not IOS', hint: 'Assessed — does not qualify' },
    { key: 'unknown', label: 'Unknown', hint: 'No open-acreage estimate' },
  ];

  protected val(e: Event) { return (e.target as HTMLInputElement).value; }
  protected chk(e: Event) { return (e.target as HTMLInputElement).checked; }
  protected num(e: Event) { const v = this.val(e); return v === '' ? null : Number(v); }

  protected toggleClass(key: string) {
    const cur = this.f().propertyClasses;
    this.store.patch({ propertyClasses: cur.includes(key) ? cur.filter(c => c !== key) : [...cur, key] });
  }

  protected readonly screeningOptions = SCREENING_DECISIONS;
  protected readonly evidenceOptions = SITE_EVIDENCE;
  protected readonly contactOptions = CONTACT_READINESS;
  protected readonly researchOptions = RESEARCH_STAGES;

  protected toggle(key: 'evidenceStates' | 'contactStates' | 'researchStages', token: string) {
    const cur = this.f()[key];
    this.store.patch({ [key]: cur.includes(token) ? cur.filter(c => c !== token) : [...cur, token] } as Partial<SiteFilter>);
  }

  protected toggleScreening(token: string) {
    const cur = this.f().screeningDecisions;
    this.store.patch({ screeningDecisions: cur.includes(token) ? cur.filter(c => c !== token) : [...cur, token] });
  }

  protected toggleIos(key: string) {
    const cur = this.f().iosClasses;
    this.store.patch({ iosClasses: cur.includes(key) ? cur.filter(c => c !== key) : [...cur, key] });
  }
}
