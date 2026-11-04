export interface SiteFilter {
  segments: number[];
  minScore: number | null;
  topN: number | null;
  propertyClasses: string[];
  minOpenAcres: number | null;
  maxMilesToExit: number | null;
  freightOnSite: boolean | null;
  excludeFloodZone: boolean;
  onlyFloodChecked: boolean;
  excludeHighlandsPreservation: boolean;
  excludeContaminated: boolean;
  excludeNearPowerEdison: boolean;
  excludeBusyMultiTenant: boolean;
  utility: string | null;
  county: string | null;
  highway: string | null;
  search: string | null;
  // Business-owner screening filters
  iosClasses: string[];                 // confirmed (manual) | indicated | potential | not | unknown
  minEstimatedParking: number | null;
  truckEntranceVerified: boolean;
  electricalVerified: boolean;
  parkingVerified: boolean;
  onlyWithVerifiedContact: boolean;
  screeningDecisions: string[];         // promising | research | unsuitable | notreviewed
  evidenceStates: string[];             // verified | preliminary | mismatch | unknown (research records only)
  contactStates: string[];              // relevant | general | lead | none (researched contacts only)
}

/** Only 'Confirmed' is manually verified; the rest are inferred from source data. */
export type IosClassification = 'Confirmed' | 'FreightIndicated' | 'Potential' | 'NotIos' | 'Unknown';
export type IosBasis = 'SourceDerived' | 'ManualReview';

export const IOS_LABELS: Record<IosClassification, string> = {
  Confirmed: 'Confirmed (manual review)',
  FreightIndicated: 'Freight indicated (unverified)',
  Potential: 'Potential (unverified)',
  NotIos: 'Not IOS',
  Unknown: 'Unknown',
};
export type FeasibilityVerdict = 'Verified' | 'Concern' | 'Unknown';

export const DEFAULT_FILTER: SiteFilter = {
  segments: [],
  minScore: null,
  topN: null,
  propertyClasses: [],
  minOpenAcres: null,
  maxMilesToExit: null,
  freightOnSite: null,
  excludeFloodZone: false,
  onlyFloodChecked: false,
  excludeHighlandsPreservation: false,
  excludeContaminated: false,
  excludeNearPowerEdison: false,
  excludeBusyMultiTenant: false,
  utility: null,
  county: null,
  highway: null,
  search: null,
  iosClasses: [],
  minEstimatedParking: null,
  truckEntranceVerified: false,
  electricalVerified: false,
  parkingVerified: false,
  onlyWithVerifiedContact: false,
  screeningDecisions: [],
  evidenceStates: [],
  contactStates: [],
};

// ---- Research readiness (derived from research records only — never from the C3 score) ----
export type SiteEvidence = 'Verified' | 'Preliminary' | 'Mismatch' | 'Unknown';
export type ContactReadiness = 'RelevantContactVerified' | 'GeneralBusinessContact' | 'UnverifiedLead' | 'NoContact';

export const SITE_EVIDENCE: { value: SiteEvidence; token: string; label: string; hint: string }[] = [
  { value: 'Verified', token: 'verified', label: 'Verified', hint: 'A reviewer checked the official parcel against the county tax map, with evidence' },
  { value: 'Preliminary', token: 'preliminary', label: 'Preliminary', hint: 'Some research recorded (screening or feasibility checks), parcel not verified yet' },
  { value: 'Mismatch', token: 'mismatch', label: 'Mismatch', hint: 'A reviewer recorded a parcel or business-at-address discrepancy' },
  { value: 'Unknown', token: 'unknown', label: 'Unknown', hint: 'No research recorded yet (a high C3 score does not verify anything)' },
];
export const CONTACT_READINESS: { value: ContactReadiness; token: string; label: string; short: string; hint: string }[] = [
  { value: 'RelevantContactVerified', token: 'relevant', label: 'Relevant Contact Verified', short: 'Relevant verified',
    hint: 'Owner, broker, facilities, sustainability or energy manager — verified from a cited source' },
  { value: 'GeneralBusinessContact', token: 'general', label: 'General Business Contact', short: 'General business',
    hint: 'A verified general company line or non-decision role — not a site decision-maker' },
  { value: 'UnverifiedLead', token: 'lead', label: 'Unverified Lead', short: 'Unverified lead', hint: 'A contact is recorded but not verified yet' },
  { value: 'NoContact', token: 'none', label: 'No Contact', short: 'No contact', hint: 'No researched contact on record' },
];

export type ValidationStatus = 'Unknown' | 'Unconfirmed' | 'Verified' | 'Mismatch';
export interface ValidationSection {
  key: 'Parcel' | 'Business' | 'Owner' | 'Suitability'; title: string;
  status: ValidationStatus; statusLabel: string;
  facts: { label: string; value: string }[];
  finding: string | null; evidenceLinks: string[]; checkedAtUtc: string | null;
  discrepancies: string[]; nextStep: string | null;
}
export interface SiteValidation {
  siteEvidence: SiteEvidence; contactReadiness: ContactReadiness;
  sections: ValidationSection[]; missingVerification: string[];
}

export type SiteSort = 'Score' | 'Rank' | 'OpenAcres' | 'MilesToExit' | 'TrucksBased5Mi' | 'TruckTraffic' | 'MilesToPowerEdison';

export interface SiteMapPoint {
  id: number; lat: number; lng: number; score: number; segment: number; rank: number;
  openAcres: number; freight: boolean; flood: boolean; nearPe: boolean;
}

export interface SiteListItem {
  id: number; siteId: string; segment: number; rank: number; score: number; fileScore: number;
  address: string; municipality: string; county: string; propClass: string;
  acres: number; openAcres: number; milesToExit: number; nearestHighway: string | null; nearestExit: string | null;
  trucksBased5Mi: number; truckTraffic: number; largeWarehouses2Mi: number;
  businesses: string | null; freight: boolean; utility: string | null;
  inFloodZone: boolean; floodChecked: boolean; isContaminated: boolean; inHighlandsPreservation: boolean;
  nearPowerEdison: boolean; nearestPowerEdisonSite: string | null; milesToPowerEdison: number;
  lat: number; lng: number;
  estimatedParking: number | null;   // preliminary estimate; null = Unknown
  iosClassification: IosClassification;
  iosBasis: IosBasis;
  screeningDecision: ScreeningDecision;
  siteEvidence: SiteEvidence;
  contactReadiness: ContactReadiness;
  /** Most relevant researched contact (null = none recorded). Same rule as the research CSV. */
  topContact?: TopContact | null;
  /** Business phone listed in the C3 data (general line, not a verified decision-maker). */
  businessPhone?: string | null;
  /** Owner mailing address from the C3 parcel record (same as Site Details → Who to contact). */
  ownerMailAddress?: string | null; ownerMailCity?: string | null; ownerMailZip?: string | null;
  /** A reviewer verified, with evidence, that the listed business operates at the address. */
  businessListingConfirmed?: boolean;
}

/** Most relevant researched contact for a site. `verified` = Verified status with a cited source. */
export interface TopContact {
  name: string; jobTitle: string | null; organization: string | null; role: ContactType;
  email: string | null; phone: string | null; verified: boolean; sourceUrl: string | null; otherContacts: number;
}

export interface PagedResult<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }

export interface ScoreLine { key: string; label: string; group: string; points: number; max: number; min: number; basis: string; }

export interface NearbyFleet {
  carrier: string; dba: string | null; trucks: number; tractors: number; drivers: number; address: string | null;
  approxMiles: number; phone: string | null; email: string | null; usDot: string | null; operation: string | null; cargo: string | null;
}

export interface SiteDetail {
  summary: SiteListItem;
  scoreLines: ScoreLine[];
  truckDemandPoints: number;
  sitesInSegment: number;
  pamsPin: string | null; block: string | null; lot: string | null; qual: string | null;
  lotsInSite: number; memberLots: string | null; recordedAcres: number; buildingCoverage: number; zoning: string | null;
  shortSideFt: number; compactness: number; buildingDescription: string | null; landDescription: string | null;
  yearBuilt: number | null; landValue: number; improvementValue: number; annualTax: number; lastSalePrice: number; lastDeedDate: string | null;
  ownerName: string | null; ownerMailAddress: string | null; ownerMailCity: string | null; ownerMailZip: string | null; ownerOccupiedHint: boolean;
  businessCategories: string | null; businessPhone: string | null; businessWebsite: string | null; industrialUse: boolean; busyMultiTenant: boolean;
  demandSummary: string | null; trucksBased3Mi: number; trucksBased10Mi: number; carriersBased5Mi: number; tractorsBased5Mi: number;
  truckAadtAdjacent: number; truckAadtAtExit: number; milesToTruckRoad: number; truckStops2Mi: number; milesToTruckStop: number;
  milesToPort: number; nearestPort: string | null; exitName: string | null;
  milesToTruckDcfc: number; milesToAnyDcfc: number; nearestDcfc: string | null;
  highlands: string | null; floodZone100Yr: string | null; knownContaminatedSites: number | null;
  // Site evaluation (research & feasibility)
  iosEvidence: string;
  iosDerivedClassification: IosClassification; iosDerivedEvidence: string; iosReviewStatus: FeasibilityVerdict;
  truckEntranceStatus: FeasibilityVerdict; truckEntranceEvidence: string | null;
  electricalStatus: FeasibilityVerdict; electricalEvidence: string | null;
  parkingStatus: FeasibilityVerdict; verifiedParkingEvidence: string | null;
  verifiedContactCount: number; totalContactCount: number;
  screeningEvidence: string | null; screeningUpdatedAtUtc: string | null;
  suitability: SiteSuitability | null;
  validation: SiteValidation | null;
  googleMapsUrl: string; streetViewUrl: string; satelliteUrl: string;
  nearbyFleets: NearbyFleet[];
}

export interface Kpis {
  sites: number; totalSites: number; averageScore: number; topScore: number; medianOpenAcres: number;
  freightOnSite: number; inFloodZone: number; contaminated: number; nearPowerEdison: number;
  highlandsPreservation: number; withinHalfMileOfExit: number;
}

export interface SegmentStat {
  segment: number; counties: string; sites: number; averageScore: number; topScore: number;
  topSiteId: number | null; topSiteAddress: string | null; topSiteMunicipality: string | null;
  freightOnSite: number; nearPowerEdison: number; powerEdisonSites: string | null;
}

export interface Bucket { label: string; count: number; }

export interface DashboardSummary {
  kpis: Kpis; segments: SegmentStat[]; scoreHistogram: Bucket[]; propertyClasses: Bucket[];
  utilities: Bucket[]; highways: Bucket[]; daysToProposalDeadline: number; proposalDeadline: string;
}

export interface PowerEdisonSite {
  id: number; name: string; shortName: string; address: string | null; municipality: string; segment: number;
  status: 'Operating' | 'LetterOfIntent' | 'Planned'; notes: string | null; lat: number; lng: number; candidatesWithinTwoMiles: number;
}

export interface FilterOptions { counties: string[]; utilities: string[]; highways: string[]; }
export interface MapsConfig { apiKey: string; mapId: string; }
export interface MethodologyNote { id: number; sortOrder: number; topic: string; detail: string; }
export interface SegmentInfo { id: number; counties: string; candidates: number; mergedSites: number; powerEdisonSitesInSegment: string | null; }

// ---- Contact intelligence ----
export type ContactType =
  'PropertyOwner' | 'Broker' | 'FacilityManager' | 'SustainabilityManager' |
  'EnergyEfficiencyManager' | 'FleetRepresentative' | 'Other';

export const CONTACT_TYPES: { value: ContactType; label: string }[] = [
  { value: 'PropertyOwner', label: 'Property Owner' },
  { value: 'Broker', label: 'Broker' },
  { value: 'FacilityManager', label: 'Facility Manager' },
  { value: 'SustainabilityManager', label: 'Sustainability Manager' },
  { value: 'EnergyEfficiencyManager', label: 'Energy Efficiency Manager' },
  { value: 'FleetRepresentative', label: 'Fleet Representative' },
  { value: 'Other', label: 'Other' },
];

export type ContactVerificationStatus = 'Unverified' | 'Verified' | 'Invalid';
export type SiteContactState = 'None' | 'NearbyCarrierOnly' | 'PotentialUnverified' | 'VerifiedDecisionMaker';

export interface SiteContact {
  id: number; siteId: string; name: string; organization: string | null; jobTitle: string | null;
  contactType: ContactType; businessPhone: string | null; email: string | null; sourceUrl: string | null;
  verificationStatus: ContactVerificationStatus; lastVerifiedUtc: string | null; verifiedBy: string | null;
  researchNotes: string | null; createdAtUtc: string; updatedAtUtc: string; createdBy: string | null;
}

export interface SiteContactSummary {
  siteId: string; total: number; verified: number; unverified: number; invalid: number;
  nearbyCarriers: number; state: SiteContactState;
}

export interface CreateContactRequest {
  siteId: string; name: string; organization?: string | null; jobTitle?: string | null;
  contactType: ContactType; businessPhone?: string | null; email?: string | null; sourceUrl?: string | null;
  verificationStatus?: ContactVerificationStatus | null; researchNotes?: string | null;
}
export interface UpdateContactRequest {
  name: string; organization: string | null; jobTitle: string | null; contactType: ContactType;
  businessPhone: string | null; email: string | null; sourceUrl: string | null;
  verificationStatus: ContactVerificationStatus; researchNotes: string | null;
}

// ---- Feasibility checklist ----
export type FeasibilityItemKey =
  'TruckEntranceAccess' | 'TurningManeuveringSpace' | 'UsableLand' | 'TruckParkingCapacity' |
  'EligibleHighwayAccess' | 'ElectricalService' | 'FloodEnvironmental' | 'Zoning' |
  'OwnershipLeaseAvailability' | 'PropertyOwnerInterest' | 'OutdoorStorageUse' | 'ParcelIdentity' | 'BusinessAtAddress';

export type FeasibilityStatus = 'Unknown' | 'Verified' | 'Concern';

export const FEASIBILITY_ITEMS: { key: FeasibilityItemKey; label: string }[] = [
  { key: 'ParcelIdentity', label: 'Official parcel identification — block/lot & boundary checked on the county tax map' },
  { key: 'BusinessAtAddress', label: 'Business operating at the address — confirmed (signage, Street View, website)' },
  { key: 'TruckEntranceAccess', label: 'Truck entrance & access' },
  { key: 'TurningManeuveringSpace', label: 'Turning & maneuvering space (53-ft tractor-trailer)' },
  { key: 'UsableLand', label: 'Actual usable land' },
  { key: 'TruckParkingCapacity', label: 'Truck parking capacity — record the number of truck-suitable spaces' },
  { key: 'EligibleHighwayAccess', label: 'Eligible highway access' },
  { key: 'ElectricalService', label: 'Electrical service' },
  { key: 'FloodEnvironmental', label: 'Flood & environmental constraints' },
  { key: 'Zoning', label: 'Zoning' },
  { key: 'OwnershipLeaseAvailability', label: 'Ownership / lease availability' },
  { key: 'PropertyOwnerInterest', label: 'Property-owner interest' },
  { key: 'OutdoorStorageUse', label: 'Outdoor-storage (IOS) use — confirms IOS' },
];

export interface FeasibilityItem {
  itemKey: FeasibilityItemKey; status: FeasibilityStatus; evidence: string | null;
  reviewer: string | null; reviewedAtUtc: string | null; updatedAtUtc: string | null;
}
export interface FeasibilitySummary {
  siteId: string; total: number; verified: number; concern: number; unknown: number;
  reviewed: number; lastReviewedAtUtc: string | null;
}
export interface UpsertFeasibilityRequest {
  siteId: string; itemKey: FeasibilityItemKey; status: FeasibilityStatus; evidence: string | null;
}

// ---- Saved sites (bookmarks) ----
export interface SavedSite {
  id: number; siteId: string; notes: string | null;
  createdAtUtc: string; updatedAtUtc: string;
  candidateFound: boolean;
  candidateId: number | null; segment: number | null; rank: number | null; score: number | null;
  address: string | null; municipality: string | null; county: string | null; propClass: string | null;
  openAcres: number | null; milesToExit: number | null; nearestHighway: string | null;
  ownerName: string | null; ownerMailAddress: string | null; ownerMailCity: string | null; ownerMailZip: string | null;
  businessPhone: string | null; lat: number | null; lng: number | null;
}

export interface SavedSiteRef { id: number; siteId: string; }
export interface SaveSiteRequest { siteId: string; notes?: string | null; }
export interface UpdateSavedSiteRequest { notes: string | null; }

export const SEGMENT_COUNTIES: Record<number, string> = {
  1: 'Camden · Gloucester · Salem',
  2: 'Burlington · Mercer',
  3: 'Middlesex · Union',
  4: 'Essex · Hudson',
  5: 'Morris · Somerset',
  6: 'Bergen · Passaic',
};

// ---- Site comparison (2–5 sites, keyed by stable SiteId) ----
export const COMPARE_MIN = 2;
export const COMPARE_MAX = 5;

export type ScreeningStatus = 'NotChecked' | 'Clear' | 'Caution' | 'Flagged';
/** Cell tone from the API: verified | concern | unknown | source | clear | caution | flag | '' (neutral). */
export type CompareTone = 'verified' | 'concern' | 'unknown' | 'source' | 'clear' | 'caution' | 'flag' | '';

export interface Verification { status: FeasibilityStatus; evidence: string | null; reviewedAtUtc: string | null; }
export interface Screening { status: ScreeningStatus; detail: string; }

export interface ComparedSite {
  siteId: string; candidateId: number; address: string; municipality: string; county: string;
  segment: number; segmentCounties: string; score: number; segmentRank: number; sitesInSegment: number;
  propClass: string; propertyClassLabel: string; totalAcres: number; openAcresEst: number | null;
  ios: { classification: IosClassification; basis: IosBasis; evidence: string; derivedClassification: IosClassification; reviewStatus: FeasibilityStatus };
  nearestHighway: string | null; nearestExit: string | null; exitName: string | null; milesToExit: number;
  highwayAccess: Verification; truckEntrance: Verification;
  estimatedParkingPreliminary: number | null; verifiedParking: Verification;
  utility: string | null; electrical: Verification;
  flood: Screening; contamination: Screening; highlands: Screening; floodEnvironmentalReview: Verification;
  missingVerification: string[]; concerns: string[];
  screeningDecision: ScreeningDecision; screeningEvidence: string | null;
  lat: number; lng: number; googleMapsUrl: string; satelliteUrl: string; streetViewUrl: string;
}

export interface ComparisonRow {
  key: string; group: string; label: string; hint: string | null;
  values: string[]; tones: CompareTone[]; comparable: boolean; differs: boolean;
}

export interface SiteComparison {
  sites: ComparedSite[]; rows: ComparisonRow[]; missingSiteIds: string[]; mixedSegments: boolean; scoreNote: string;
}

/** A site picked for comparison (label is a display hint only; SiteId is the key). */
export interface CompareItem { siteId: string; label: string; segment: number | null; }

// ---- Site screening (suitability decision — NOT a CRM pipeline stage) ----
export type ScreeningDecision = 'NotReviewed' | 'Promising' | 'NeedsMoreResearch' | 'Unsuitable';

export const SCREENING_DECISIONS: { value: ScreeningDecision; label: string; token: string; hint: string }[] = [
  { value: 'Promising', label: 'Promising', token: 'promising', hint: 'Imagery/records support the site — worth pursuing' },
  { value: 'NeedsMoreResearch', label: 'Needs more research', token: 'research', hint: 'Something must be confirmed before deciding' },
  { value: 'Unsuitable', label: 'Unsuitable', token: 'unsuitable', hint: 'A clear blocker was observed' },
  { value: 'NotReviewed', label: 'Not reviewed', token: 'notreviewed', hint: 'No decision recorded yet' },
];

export const SCREENING_LABELS: Record<ScreeningDecision, string> = {
  NotReviewed: 'Not reviewed', Promising: 'Promising', NeedsMoreResearch: 'Needs more research', Unsuitable: 'Unsuitable',
};

export interface ScreeningEntry { decision: ScreeningDecision; evidence: string | null; recordedAtUtc: string; }
export interface SiteScreening {
  siteId: string; decision: ScreeningDecision; evidence: string | null; updatedAtUtc: string | null; history: ScreeningEntry[];
}
export interface UpsertScreeningRequest { siteId: string; decision: ScreeningDecision; evidence: string | null; }
export interface ScreeningCounts { total: number; notReviewed: number; promising: number; needsMoreResearch: number; unsuitable: number; reviewed: number; }
export interface SegmentScreeningProgress extends ScreeningCounts { segment: number; }
/** Overall + per-segment progress; `topN` (when set) limits the scope to each segment's top-N ranks. */
export interface ScreeningSummary extends ScreeningCounts { topN: number | null; segments: SegmentScreeningProgress[]; }

// ---- Site Details "at a glance" (informational; never scores or decides) ----
export type SuitabilityAnswer = 'Yes' | 'NeedsCheck' | 'No' | 'Unknown';
export type SuitabilityBasis = 'Verified' | 'Estimate' | 'SourceData' | 'Unverified' | 'Missing';
export interface SuitabilityItem {
  key: string; question: string; answer: SuitabilityAnswer; basis: SuitabilityBasis;
  headline: string; detail: string; nextStep: string | null;
}
export interface SiteSuitability {
  items: SuitabilityItem[]; remainingToVerify: string[];
  yes: number; needsCheck: number; no: number; unknown: number; verifiedAnswers: number;
  /** Preliminary business preferences — separate from the C3 score and the answers above. */
  preferences: SuitabilityItem[]; preferencesMet: number;
}
