import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { SiteStore } from '../../core/site-store';
import { FilterPanelComponent } from './filter-panel.component';
import { InsightsComponent } from './insights.component';
import { KpiStripComponent } from './kpi-strip.component';
import { SiteDetailComponent } from './site-detail.component';
import { SiteMapComponent } from './site-map.component';
import { SitesTableComponent } from './sites-table.component';

@Component({
  selector: 'app-dashboard-page',
  imports: [FilterPanelComponent, KpiStripComponent, SiteMapComponent, SiteDetailComponent, InsightsComponent, SitesTableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (store.error(); as e) { <div class="banner error" role="alert">{{ e }}</div> }
    <app-kpi-strip />
    <div class="workspace" [class.with-detail]="store.selectedId() !== null">
      <app-filter-panel />
      <app-site-map />
      <app-site-detail />
    </div>
    <app-insights />
    <app-sites-table />
  `,
})
export class DashboardPage {
  protected readonly store = inject(SiteStore);
}
