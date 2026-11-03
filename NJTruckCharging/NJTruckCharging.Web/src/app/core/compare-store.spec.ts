import { TestBed } from '@angular/core/testing';
import { CompareStore } from './compare-store';
import { CompareItem } from './models';

const item = (n: number, segment = 1): CompareItem => ({ siteId: `S-${n}`, label: `${n} Main St`, segment });

describe('CompareStore', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
  });

  const create = () => TestBed.inject(CompareStore);

  it('adds, de-duplicates and removes sites by SiteId', () => {
    const store = create();
    store.add(item(1));
    store.add(item(1));
    store.add(item(2, 3));
    expect(store.siteIds()).toEqual(['S-1', 'S-2']);
    expect(store.canCompare()).toBe(true);
    store.remove('S-1');
    expect(store.siteIds()).toEqual(['S-2']);
    expect(store.canCompare()).toBe(false);
  });

  it('refuses a sixth site and explains why', () => {
    const store = create();
    for (let i = 1; i <= 5; i++) expect(store.add(item(i))).toBe(true);
    expect(store.isFull()).toBe(true);
    expect(store.add(item(6))).toBe(false);
    expect(store.count()).toBe(5);
    expect(store.notice()).toContain('up to 5');
  });

  it('toggle adds then removes', () => {
    const store = create();
    store.toggle(item(1));
    expect(store.has('S-1')).toBe(true);
    store.toggle(item(1));
    expect(store.has('S-1')).toBe(false);
  });

  it('replace keeps order, drops duplicates and caps at five', () => {
    const store = create();
    store.replace([item(1), item(2), item(1), item(3), item(4), item(5), item(6)]);
    expect(store.siteIds()).toEqual(['S-1', 'S-2', 'S-3', 'S-4', 'S-5']);
    expect(store.notice()).toContain('first 5');
  });

  it('persists the selection to localStorage and restores it', () => {
    const store = create();
    store.add(item(7));
    store.add(item(8));
    TestBed.tick(); // flush the persistence effect
    expect(JSON.parse(localStorage.getItem(CompareStore.KEY)!).map((i: CompareItem) => i.siteId)).toEqual(['S-7', 'S-8']);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    expect(TestBed.inject(CompareStore).siteIds()).toEqual(['S-7', 'S-8']);
  });

  it('ignores corrupt stored data', () => {
    localStorage.setItem(CompareStore.KEY, '{not json');
    expect(create().count()).toBe(0);
  });
});
