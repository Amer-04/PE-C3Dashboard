import { TestBed } from '@angular/core/testing';
import { SiteListItem, TopContact } from '../../core/models';
import { ContactCellComponent } from './contact-cell.component';

const contact: TopContact = {
  name: 'Olive Owner', jobTitle: 'Managing Member', organization: 'Owner LLC', role: 'PropertyOwner',
  email: 'olive@owner.example', phone: '(555) 010-0100', verified: true, sourceUrl: 'https://example.org', otherContacts: 2,
};
// Values from real C3 site 1429_739_5 (as shown in Site Details → Who to contact).
const row = (over: Partial<SiteListItem> = {}): SiteListItem => ({
  siteId: '1429_739_5', businesses: 'IMC powered by Parts Authority; Powpack', businessPhone: '8008748925',
  ownerMailAddress: '111 Littleton Rd Ste 100', ownerMailCity: 'Parsippany, Nj', ownerMailZip: '07054',
  topContact: null, businessListingConfirmed: false, ...over,
} as SiteListItem);

describe('ContactCellComponent', () => {
  function render(s: SiteListItem) {
    const f = TestBed.createComponent(ContactCellComponent);
    f.componentRef.setInput('site', s);
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }
  const dd = (el: HTMLElement, label: string) =>
    [...el.querySelectorAll('.cc-dl dt')].find(d => d.textContent!.trim() === label)!.nextElementSibling as HTMLElement;

  it('shows the imported business listing like Site Details, labelled unverified', () => {
    const el = render(row());
    expect(el.querySelector('.cc-listing-tag')!.textContent!.trim()).toBe('Unverified Business Listing');
    expect(dd(el, 'Business').textContent!.trim()).toBe('IMC powered by Parts Authority; Powpack');
    const tel = dd(el, 'Phone').querySelector('a')!;
    expect(tel.getAttribute('href')).toBe('tel:8008748925');
    expect(tel.textContent).toBe('(800) 874-8925');
    expect(dd(el, 'Email').textContent!.trim()).toBe('Not available');
    expect(dd(el, 'Owner mail').textContent!.trim()).toBe('111 Littleton Rd Ste 100, Parsippany, Nj 07054');
    expect(el.querySelector('.cc-name')).toBeNull();
    expect(el.textContent).not.toContain('No Contact');
  });

  it('shows "Not available" only for the missing field and never invents values', () => {
    const el = render(row({ businessPhone: null, businesses: null }));
    expect(dd(el, 'Phone').textContent!.trim()).toBe('Not available');
    expect(dd(el, 'Business').textContent!.trim()).toBe('Not available');
    expect(dd(el, 'Owner mail').textContent!.trim()).toContain('111 Littleton Rd');
    expect(el.querySelector('a')).toBeNull();
  });

  it('puts a verified decision-maker above the imported listing', () => {
    const el = render(row({ topContact: contact }));
    const first = el.firstElementChild as HTMLElement;
    expect(first.classList).toContain('cc-cell');
    expect(el.querySelector('.cc-name')!.textContent).toBe('Olive Owner');
    expect(el.querySelector('.cc-cell .rr-badge')!.textContent!.trim()).toBe('Verified');
    expect(el.querySelector('a[href="mailto:olive@owner.example"]')).not.toBeNull();
    expect(el.querySelector('.cc-listing.after')).not.toBeNull();
  });

  it('labels unverified leads and general lines distinctly', () => {
    expect(render(row({ topContact: { ...contact, verified: false } })).querySelector('.cc-cell .rr-badge')!.textContent!.trim()).toBe('Unverified lead');
    expect(render(row({ topContact: { ...contact, role: 'Other' } })).querySelector('.cc-cell .rr-badge')!.textContent!.trim()).toBe('Verified · general');
  });

  it('says the business is confirmed only when a reviewer recorded it, and keeps the phone unverified', () => {
    expect(render(row({ businessListingConfirmed: true })).querySelector('.cc-listing-tag')!.textContent!.trim())
      .toBe('Business confirmed at address · phone unverified');
  });

  it('shows the no-contact message only when nothing at all is on record', () => {
    const el = render(row({ businesses: null, businessPhone: null, ownerMailAddress: null, ownerMailCity: null, ownerMailZip: null }));
    expect(el.textContent!.trim()).toBe('No Contact — Email and phone unavailable.');
  });
});
