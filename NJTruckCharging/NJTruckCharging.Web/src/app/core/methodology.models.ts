// ---- Editable scoring methodology (mirrors NJTruckCharging.Domain.Methodology + Application.Methodology DTOs) ----

export type CriterionKind = 'Linear' | 'Cases' | 'Percentile';
export type CriterionCategory = 'Access' | 'Site' | 'TruckDemand' | 'ChargerGap' | 'Penalty' | 'Bonus';
export type ConditionOp = 'Lt' | 'Lte' | 'Gt' | 'Gte' | 'Eq' | 'Neq' | 'Between' | 'IsTrue' | 'IsFalse' | 'In' | 'NotIn' | 'AnyLotIn';
export type FieldType = 'Number' | 'Boolean' | 'Category' | 'LotCategory';
export type MissingDataMode = 'ZeroPoints' | 'FixedPoints';

export interface RuleCondition { field: string; op: ConditionOp; value: number | null; value2: number | null; values: string[]; }
export interface ScoreCase { label: string | null; conditions: RuleCondition[]; points: number; }
export interface MissingDataPolicy { mode: MissingDataMode; points: number; }
export interface Criterion {
  key: string; name: string; explanation: string; category: CriterionCategory; enabled: boolean; builtIn: boolean;
  kind: CriterionKind; field: string | null; fullAt: number | null; zeroAt: number | null; maxPoints: number | null;
  cases: ScoreCase[]; defaultPoints: number; missing: MissingDataPolicy;
}
export interface MethodologyRules { schemaVersion: number; criteria: Criterion[]; }

export interface ValidationIssue { severity: 'Error' | 'Warning'; path: string; message: string; }
export interface ValidationResult { valid: boolean; issues: ValidationIssue[]; minPossible: number; maxPossible: number; }

export interface VersionSummary {
  id: number; name: string; status: 'Draft' | 'Published'; isBaseline: boolean; isActive: boolean; isPreviousActive: boolean;
  rulesHash: string; changeSummary: string | null; basedOnVersionId: number | null;
  createdAtUtc: string; updatedAtUtc: string; publishedAtUtc: string | null; previewedAtUtc: string | null;
  previewCurrent: boolean; criteriaCount: number; enabledCount: number; identicalToBaseline: boolean;
  /** Optimistic-concurrency revision: sent back when saving or deleting a draft. */
  revision: number;
  createdByEmail: string | null; updatedByEmail: string | null; publishedByEmail: string | null;
}
export interface CriterionView {
  key: string; name: string; category: string; enabled: boolean; builtIn: boolean; kind: CriterionKind; rule: string;
  explanation: string; min: number; max: number; fields: string[]; usesMissableData: boolean; missingPolicy: string | null;
}
export interface ActiveMethodology {
  version: VersionSummary; criteria: CriterionView[]; minPossible: number; maxPossible: number;
  previousActiveVersionId: number | null; activatedAtUtc: string | null; dataVersion: number; tieBreak: string;
}
export interface RuleChange { key: string; name: string; change: 'Added' | 'Removed' | 'Modified' | 'Enabled' | 'Disabled'; details: string[]; }
export interface Mover {
  siteId: string; address: string; municipality: string; segment: number;
  activeScore: number; activeRank: number; proposedScore: number; proposedRank: number;
  baselineScore: number | null; baselineRank: number | null;
}
export interface SegmentPreview {
  segment: number; sites: number; avgActive: number; avgProposed: number; ranksChanged: number; topNKept: number;
  entering: Mover[]; leaving: Mover[]; proposedTop: Mover[];
}
export interface CriterionImpact {
  key: string; name: string; enabled: boolean; avgPoints: number; minPoints: number; maxPoints: number;
  sitesNonZero: number; sitesMissingData: number; missingPolicy: string | null;
}
export interface PreviewSummary {
  versionId: number; versionName: string; rulesHash: string; dataVersion: number; computedAtUtc: string; elapsedMs: number; sites: number;
  comparedToVersionId: number; comparedToName: string; identicalToActive: boolean;
  scoresChanged: number; ranksChanged: number; maxIncrease: number; maxDecrease: number; meanAbsChange: number;
  topN: number; segments: SegmentPreview[]; topRisers: Mover[]; topFallers: Mover[];
  criteria: CriterionImpact[]; changes: RuleChange[]; warnings: ValidationIssue[];
}
export interface VersionDetail {
  version: VersionSummary; rules: MethodologyRules; criteria: CriterionView[]; minPossible: number; maxPossible: number;
  changesVsActive: RuleChange[]; issues: ValidationIssue[]; preview: PreviewSummary | null;
}
export interface ScoringFieldInfo {
  key: string; label: string; type: FieldType; unit: string | null; group: string; description: string;
  missingMeaning: string | null; categories: string[] | null; operators: ConditionOp[];
  present: number; missing: number; zero: number; min: number | null; max: number | null; categoryCounts: Record<string, number> | null;
  isCustom?: boolean; customSource?: CustomFieldSource | null;
}

// ---- User-defined custom data fields ("Other — Custom Criterion") ----
export type CustomFieldType = 'Number' | 'Boolean' | 'Category';
export type CustomFieldSource = 'Manual' | 'Imported';
export interface CustomField {
  key: string; label: string; description: string | null; type: CustomFieldType; source: CustomFieldSource; unit: string | null;
  minValue: number | null; maxValue: number | null; categories: string[]; valueCount: number; siteCount: number;
  usedByActive: boolean; usedByVersions: number[]; createdAtUtc: string;
}
export interface CustomFieldRequest {
  label: string; description: string | null; type: CustomFieldType; source: CustomFieldSource; unit: string | null;
  minValue: number | null; maxValue: number | null; categories: string[] | null;
}
export interface CustomValue {
  fieldKey: string; label: string; type: CustomFieldType; source: CustomFieldSource; unit: string | null; categories: string[];
  minValue: number | null; maxValue: number | null; description: string | null;
  status: 'Unknown' | 'Recorded'; display: string | null; numberValue: number | null; boolValue: boolean | null; categoryValue: string | null;
  evidence: string | null; importBatch: string | null; updatedAtUtc: string | null; usedByActive: boolean;
}
export interface ImportIssue { line: number; siteId: string | null; message: string; }
export interface ImportSampleRow { line: number; siteId: string; value: string; current: string | null; change: 'new' | 'update' | 'unchanged'; }
export interface ImportPreview {
  fieldKey: string; fileName: string; rows: number; validRows: number; inserts: number; updates: number; unchanged: number;
  unknownSiteIds: number; duplicates: number; invalidValues: number; errors: ImportIssue[]; sample: ImportSampleRow[];
  token: string; canCommit: boolean; sitesWithoutValueAfter: number;
}
export interface ImportResult { inserted: number; updated: number; batch: string; }
export interface ComparisonPage { total: number; page: number; pageSize: number; versionId: number; activeVersionId: number | null; rows: Mover[]; }
export interface Activation {
  id: number; versionId: number; versionName: string; previousVersionId: number | null;
  reason: 'Initial' | 'Publish' | 'Restore' | 'Reimport' | 'Rescore'; atUtc: string; sitesScored: number; note: string | null;
  actorUserId: string | null; actorEmail: string | null;
}
export type ChangeAction = 'CreateDraft' | 'SaveDraft' | 'DeleteDraft' | 'Preview' | 'Publish' | 'Restore';
/** One audit entry: who changed which version, when, and the previous → new values. */
export interface ChangeLogEntry {
  id: number; versionId: number; versionName: string; action: ChangeAction; atUtc: string;
  actorUserId: string | null; actorEmail: string | null; revisionBefore: number | null; revisionAfter: number | null;
  rulesHashBefore: string | null; rulesHashAfter: string | null; previousActiveVersionId: number | null;
  changes: RuleChange[]; note: string | null; hasSnapshots: boolean;
}
export interface ChangeLogPage { total: number; page: number; pageSize: number; entries: ChangeLogEntry[]; }
export interface VersionSnapshot { name: string; changeSummary: string | null; rules: MethodologyRules; }
export interface ChangeSnapshot { id: number; before: VersionSnapshot | null; after: VersionSnapshot | null; }
export interface EditingStatus { enabled: boolean; reason: string | null; }
export interface PublishResult { activeVersionId: number; previousVersionId: number | null; sitesUpdated: number; elapsedMs: number; }

export const CATEGORY_LABELS: Record<CriterionCategory, string> = {
  Access: 'Access', Site: 'Site', TruckDemand: 'Truck demand', ChargerGap: 'Charger gap', Penalty: 'Penalty', Bonus: 'Bonus',
};

export const OP_LABELS: Record<ConditionOp, string> = {
  Lt: '<', Lte: '≤', Gt: '>', Gte: '≥', Eq: '=', Neq: '≠', Between: 'between', IsTrue: 'is yes', IsFalse: 'is no',
  In: 'is one of', NotIn: 'is not one of', AnyLotIn: 'any lot is',
};
