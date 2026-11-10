import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { SiteListItem } from '../../core/models';
import { websiteLink } from '../../shared/external-link';

/**
 * "Business on site" cell of the ranked table: the business names imported with the C3 data and, when the
 * listing has one, its website (the same value Site Details opens with "Business site ↗").
 * The source records one website per parcel without saying which listed business it belongs to, so the link is
 * shown for the parcel's listing rather than attached to a single name. No link is shown when none is recorded
 * or the recorded value is not a usable address.
 */
@Component({
  selector: 'app-business-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (site(); as s) {
      <span class="bc-names" [title]="s.businesses ?? ''">{{ s.businesses || '—' }}</span>
      @if (link(); as l) {
        <a class="bc-web" [href]="l.href" target="_blank" rel="noopener noreferrer"
           (click)="$event.stopPropagation()" (keydown.enter)="$event.stopPropagation()"
           [title]="l.href + ' — website listed with this parcel\\'s business listing (C3 / Overture Maps data); opens in a new tab'">{{ l.label }} ↗</a>
      }
    }
  `,
})
export class BusinessCellComponent {
  /** The table row (already loaded with the page — no per-row API calls). */
  readonly site = input<SiteListItem | null | undefined>(null);
  protected readonly link = computed(() => websiteLink(this.site()?.businessWebsite));
}
