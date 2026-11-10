import { TestBed } from '@angular/core/testing';
import { SiteListItem } from '../../core/models';
import { websiteLink } from '../../shared/external-link';
import { BusinessCellComponent } from './business-cell.component';

// Values from real C3 site 0421_405_1 (as shown in Site Details).
const row = (over: Partial<SiteListItem> = {}): SiteListItem => ({
  siteId: '0421_405_1', businesses: 'The UPS Store; UPS Lawnside Hub', businessWebsite: 'http://www.theupsstore.com', ...over,
} as SiteListItem);

describe('websiteLink', () => {
  it('keeps http(s) URLs as recorded and labels them by host', () => {
    expect(websiteLink('http://www.theupsstore.com')).toEqual({ href: 'http://www.theupsstore.com/', label: 'theupsstore.com' });
    expect(websiteLink('https://freightliner.com/Dealer?code=AMXD&name=W.%20CAMPBELL')!.label).toBe('freightliner.com');
  });
  it('adds only https:// to bare domains from the source', () => {
    expect(websiteLink('kampack.com')).toEqual({ href: 'https://kampack.com/', label: 'kampack.com' });
    expect(websiteLink('www.lidl.com/s/en-US/stores/belleville/main-st-414/')!.href).toBe('https://www.lidl.com/s/en-US/stores/belleville/main-st-414/');
  });
  it('rejects missing, unsafe or shortened values', () => {
    for (const v of [null, undefined, '', '  ', 'javascript:alert(1)', 'data:text/html,x', 'www.phillips66.com/.../refining/Pages/Bayway-Refinery.aspx',
                     'not a url', 'ftp://example.com'])
      expect(websiteLink(v)).toBeNull();
  });
});

describe('BusinessCellComponent', () => {
  function render(s: SiteListItem) {
    const f = TestBed.createComponent(BusinessCellComponent);
    f.componentRef.setInput('site', s);
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }

  it('shows the business names with the listed website as a safe new-tab link', () => {
    const el = render(row());
    expect(el.querySelector('.bc-names')!.textContent!.trim()).toBe('The UPS Store; UPS Lawnside Hub');
    const a = el.querySelector('a.bc-web') as HTMLAnchorElement;
    expect(a.getAttribute('href')).toBe('http://www.theupsstore.com/');
    expect(a.getAttribute('target')).toBe('_blank');
    expect(a.getAttribute('rel')).toBe('noopener noreferrer');
    expect(a.textContent!.trim()).toBe('theupsstore.com ↗');
  });

  it('shows only the names when no usable website is recorded', () => {
    for (const w of [null, 'www.phillips66.com/.../refining/Pages/Bayway-Refinery.aspx', 'javascript:alert(1)']) {
      const el = render(row({ businessWebsite: w }));
      expect(el.querySelector('a')).toBeNull();
      expect(el.querySelector('.bc-names')!.textContent!.trim()).toBe('The UPS Store; UPS Lawnside Hub');
    }
  });

  it('does not let a website click or Enter reach the table row', () => {
    const el = render(row());
    let rowEvents = 0;
    el.addEventListener('click', () => rowEvents++);
    el.addEventListener('keydown', () => rowEvents++);
    const a = el.querySelector('a.bc-web') as HTMLAnchorElement;
    a.addEventListener('click', e => e.preventDefault()); // keep the test runner from navigating
    a.click();
    a.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(rowEvents).toBe(0);
  });
});
