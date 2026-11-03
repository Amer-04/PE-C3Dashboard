import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { SiteStore } from '../../core/site-store';

@Component({
  selector: 'app-methodology-page',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="doc">
      <section class="panel">
        <h2>Who we are siting for</h2>
        <div class="about-grid">
          <div>
            <h3>Power Edison</h3>
            <p>New Jersey clean-energy company (CEO Dr. Shihab Kuran) that builds utility-grade <b>mobile battery
              storage</b> — the trailer-based TerraCharge platform with separate battery and power-conversion trailers
              (up to 33 kV). Mobile storage lets a charging site open <b>before slow utility grid upgrades</b> are finished,
              and supports peak shaving, back-up power and EV charging.</p>
          </div>
          <div>
            <h3>EV Edison</h3>
            <p>Power Edison's charging-hub business. Its flagship <b>Kearny Point hub</b> (near Port Newark, opened 2025)
              is billed as the largest multi-class charging hub on the East Coast: 30 × 180 kW ports and about 200
              heavy-duty sessions a day in Phase I, with Einride staging electric trucks there. The pipeline includes
              Irvington and Edison (letters of intent) and Paterson.</p>
          </div>
        </div>
        <h3>Power Edison sites on the map</h3>
        <table class="mini">
          <thead><tr><th>Site</th><th>Status</th><th class="num">Segment</th><th class="num">Candidates ≤ 2 mi</th></tr></thead>
          <tbody>
            @for (p of store.powerEdisonSites(); track p.id) {
              <tr><td><b>{{ p.name }}</b><br /><small class="muted">{{ p.notes }}</small></td><td>{{ p.status === 'LetterOfIntent' ? 'Letter of intent' : p.status }}</td>
                <td class="num">{{ p.segment }}</td><td class="num">{{ p.candidatesWithinTwoMiles | number }}</td></tr>
            }
          </tbody>
        </table>
        <p class="small muted">NJDEP may fund only one of two strong proposals in close proximity, so candidates within 2 mi of a
          Power Edison site are flagged rather than penalised.</p>
      </section>

      <section class="panel">
        <h2>Scoring algorithm (max ≈ 110)</h2>
        <p>The app's scoring engine re-computes every site's score from the raw attributes and reproduces the
          workbook for all 30,584 candidates (scores within ±0.05, identical ranks).</p>
        <table class="mini rules">
          <thead><tr><th>Category</th><th class="num">Max</th><th>How points are earned</th></tr></thead>
          <tbody>
            <tr><td>Distance to eligible exit</td><td class="num">20</td><td>20 within 0.5 mi, falling linearly to 0 at 5 mi</td></tr>
            <tr><td>Open land (lot − building footprints)</td><td class="num">20</td><td>≥3 ac 20 · 1.5–3 ac 16 · 1–1.5 ac 10 · 0.6–1 ac 3 · under 0.6 ac −15</td></tr>
            <tr><td>Property class (best among merged lots)</td><td class="num">15</td><td>Industrial 15 · Vacant 11 · Commercial 8</td></tr>
            <tr><td>Trucking on site</td><td class="num">10</td><td>Freight/logistics business 10 · other industrial 4 · neither 0</td></tr>
            <tr><td>Truck demand</td><td class="num">35</td><td>Fleets within 5 mi 12 · truck traffic 10 · large warehouses 6 (each a percentile within the segment) · truck stop ≤2 mi 3 · port ≤5 mi 4 / ≤10 mi 2</td></tr>
            <tr><td>No truck-capable charger nearby</td><td class="num">10</td><td>10 if nearest MHD charger &gt;10 mi, otherwise 4</td></tr>
            <tr><td>No fast charger nearby</td><td class="num">5</td><td>5 if nearest DC fast charger &gt;2 mi</td></tr>
            <tr><td>Lot shape</td><td class="num">5</td><td>5 if compactness ≥0.5 and short side ≥180 ft · 0 if short side 120–180 ft · −10 under 120 ft</td></tr>
            <tr class="pen"><td>Far from truck road</td><td class="num">−10</td><td>&gt;0.5 mi from a road carrying 500+ tractor-trailers/day</td></tr>
            <tr class="pen"><td>Highlands Preservation Area</td><td class="num">−15</td><td>New development restricted</td></tr>
            <tr class="pen"><td>Busy multi-tenant</td><td class="num">−5</td><td>Six or more businesses on the parcel</td></tr>
            <tr class="pen"><td>FEMA 100-yr flood zone</td><td class="num">−10</td><td>Checked for the top 150 per segment</td></tr>
          </tbody>
        </table>
        <h3>Two rules the workbook applies implicitly</h3>
        <ul>
          <li><b>Unchecked flood status:</b> only the top 150 per segment were checked against FEMA maps; every unchecked site carries
            the −10 flood penalty so it stays ranked below the verified shortlist.</li>
          <li><b>Busy multi-tenant:</b> the workbook lists at most three business names, not the count, so the importer recovers this
            flag (3,560 sites) from the delivered score.</li>
          <li><b>Rank ties</b> are broken by distance to the exit (closer first).</li>
        </ul>
      </section>

      <section class="panel">
        <h2>Data sources &amp; method notes</h2>
        <dl class="notes">
          @for (n of notes() ?? []; track n.id) { <dt>{{ n.topic }}</dt><dd>{{ n.detail }}</dd> }
        </dl>
      </section>
    </div>
  `,
})
export class MethodologyPage {
  protected readonly store = inject(SiteStore);
  protected readonly notes = toSignal(inject(ApiService).methodology());
}
