/**
 * TypeScript mirrors of the C# records in DistrictSync.Contracts (camelCase JSON, enums as strings,
 * DateTimeOffset/DateOnly as ISO strings, Guid as string).
 */

// ----- Enums (serialized as strings) -----

export type IngestionBatchStatus = 'Received' | 'Processing' | 'Completed' | 'Failed' | 'Duplicate';
export type ReconciliationRunStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';
export type DiscrepancyType = 'MissingInLeft' | 'MissingInRight' | 'FieldConflict' | 'Duplicate';
export type DiscrepancyStatus = 'Open' | 'AutoResolved' | 'CaseOpened' | 'Resolved' | 'Ignored';
export type Severity = 'Low' | 'Medium' | 'High' | 'Critical';
export type CaseState = 'Open' | 'Assigned' | 'PendingApproval' | 'Approved' | 'Resolved' | 'Escalated' | 'Cancelled';
export type CasePriority = 'Low' | 'Normal' | 'High' | 'Urgent';
export type RuleActionKind = 'AutoResolve' | 'OpenCase' | 'Ignore';
export type ResolutionStrategy = 'PreferLeft' | 'PreferRight' | 'PreferNewest' | 'PreferHigherTrust';
export type SourceSystemKind = 'RestApi' | 'CsvUpload' | 'Webhook';
export type FieldNormalizer = 'None' | 'Trim' | 'CaseInsensitive' | 'Email' | 'PhoneDigits' | 'DateOnly' | 'Decimal' | 'WhitespaceCollapse';
export type WorkflowStatus = 'Scheduled' | 'Running' | 'Completed' | 'Failed';
export type RuleSetStatus = 'Draft' | 'Published' | 'Retired';

export const INGESTION_BATCH_STATUSES: IngestionBatchStatus[] = ['Received', 'Processing', 'Completed', 'Failed', 'Duplicate'];
export const RECONCILIATION_RUN_STATUSES: ReconciliationRunStatus[] = ['Queued', 'Running', 'Completed', 'Failed', 'Cancelled'];
export const DISCREPANCY_TYPES: DiscrepancyType[] = ['MissingInLeft', 'MissingInRight', 'FieldConflict', 'Duplicate'];
export const DISCREPANCY_STATUSES: DiscrepancyStatus[] = ['Open', 'AutoResolved', 'CaseOpened', 'Resolved', 'Ignored'];
export const SEVERITIES: Severity[] = ['Low', 'Medium', 'High', 'Critical'];
export const CASE_STATES: CaseState[] = ['Open', 'Assigned', 'PendingApproval', 'Approved', 'Resolved', 'Escalated', 'Cancelled'];
export const CASE_PRIORITIES: CasePriority[] = ['Low', 'Normal', 'High', 'Urgent'];
export const RULE_ACTION_KINDS: RuleActionKind[] = ['AutoResolve', 'OpenCase', 'Ignore'];
export const RESOLUTION_STRATEGIES: ResolutionStrategy[] = ['PreferLeft', 'PreferRight', 'PreferNewest', 'PreferHigherTrust'];
export const SOURCE_SYSTEM_KINDS: SourceSystemKind[] = ['RestApi', 'CsvUpload', 'Webhook'];
export const FIELD_NORMALIZERS: FieldNormalizer[] = ['None', 'Trim', 'CaseInsensitive', 'Email', 'PhoneDigits', 'DateOnly', 'Decimal', 'WhitespaceCollapse'];
export const WORKFLOW_STATUSES: WorkflowStatus[] = ['Scheduled', 'Running', 'Completed', 'Failed'];
export const ROLES = ['admin', 'analyst', 'approver', 'viewer'] as const;
export type Role = (typeof ROLES)[number];

// ----- Common -----

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  /** Computed server-side; present in the JSON because it is a public getter. */
  totalPages?: number;
}

export interface IdResponse {
  id: string;
}

export interface DevTokenRequest {
  tenantSlug: string;
  userId: string;
  roles: string[];
}

export interface DevTokenResponse {
  accessToken: string;
  expiresAt: string;
  tenantId: string;
}

export interface TenantDto {
  id: string;
  name: string;
  slug: string;
  isActive: boolean;
  maxConcurrentReconciliations: number;
  maxConcurrentIngestions: number;
  ingestionRequestsPerMinute: number;
  createdAt: string;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
  [key: string]: unknown;
}

// ----- Source systems -----

export interface SourceSystemDto {
  id: string;
  name: string;
  code: string;
  kind: SourceSystemKind;
  trustRank: number;
  configJson: string;
  isEnabled: boolean;
  hasApiKey: boolean;
  createdAt: string;
}

export interface CreateSourceSystemRequest {
  name: string;
  code: string;
  kind: SourceSystemKind;
  trustRank: number;
  configJson: string | null;
}

export interface SourceSystemCreatedResponse {
  sourceSystem: SourceSystemDto;
  apiKey: string | null;
}

export interface ApiKeyResponse {
  apiKey: string;
}

// ----- Ingestion -----

export interface IngestionBatchDto {
  id: string;
  sourceSystemId: string;
  sourceSystemCode: string;
  entityType: string;
  status: IngestionBatchStatus;
  fileName: string | null;
  sizeBytes: number;
  totalRecords: number;
  acceptedRecords: number;
  rejectedRecords: number;
  duplicateRecords: number;
  receivedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  failureReason: string | null;
  submittedBy: string;
}

export interface StartPullRequest {
  sourceSystemId: string;
  entityType: string;
}

// ----- Reconciliation -----

export interface CreateReconciliationRunRequest {
  entityType: string;
  leftSourceSystemId: string;
  rightSourceSystemId: string;
  comparisonProfileId: string | null;
}

export interface ReconciliationStatsDto {
  leftRecords: number;
  rightRecords: number;
  matched: number;
  missingInLeft: number;
  missingInRight: number;
  fieldConflicts: number;
  duplicates: number;
  autoResolved: number;
  casesOpened: number;
  ignored: number;
}

export interface ReconciliationRunDto {
  id: string;
  entityType: string;
  leftSourceSystemId: string;
  leftSourceSystemCode: string;
  rightSourceSystemId: string;
  rightSourceSystemCode: string;
  status: ReconciliationRunStatus;
  stats: ReconciliationStatsDto;
  ruleSetId: string | null;
  ruleSetVersion: number | null;
  requestedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  failureReason: string | null;
  requestedBy: string;
  checkpoint: string | null;
}

export interface FieldDifferenceDto {
  field: string;
  leftValue: string | null;
  rightValue: string | null;
  isCritical: boolean;
}

export interface DiscrepancyDto {
  id: string;
  runId: string;
  entityType: string;
  type: DiscrepancyType;
  status: DiscrepancyStatus;
  severity: Severity;
  matchKey: string;
  leftSourceSystemId: string;
  rightSourceSystemId: string;
  leftRecordId: string | null;
  rightRecordId: string | null;
  differences: FieldDifferenceDto[];
  appliedRuleId: string | null;
  resolutionNote: string | null;
  detectedAt: string;
  resolvedAt: string | null;
}

export interface SourceRecordDto {
  id: string;
  sourceSystemId: string;
  externalId: string;
  matchKey: string;
  attributes: Record<string, string | null>;
  sourceUpdatedAt: string | null;
  ingestedAt: string;
  isDeleted: boolean;
}

export interface DiscrepancyDetailDto {
  discrepancy: DiscrepancyDto;
  left: SourceRecordDto | null;
  right: SourceRecordDto | null;
  caseId: string | null;
}

export interface ComparedFieldDto {
  name: string;
  normalizer: FieldNormalizer;
  isCritical: boolean;
}

export interface ComparisonProfileDto {
  id: string;
  name: string;
  entityType: string;
  fields: ComparedFieldDto[];
  isDefault: boolean;
  createdAt: string;
}

export interface CreateComparisonProfileRequest {
  name: string;
  entityType: string;
  fields: ComparedFieldDto[];
  isDefault: boolean;
}

// ----- Rules -----

export interface RuleActionDto {
  kind: RuleActionKind;
  strategy: ResolutionStrategy | null;
  priority: CasePriority | null;
  assignToRole: string | null;
  note: string | null;
}

export interface RuleDto {
  id: string;
  name: string;
  description: string | null;
  order: number;
  condition: string;
  action: RuleActionDto;
  isEnabled: boolean;
}

export interface RuleSetDto {
  id: string;
  familyId: string;
  name: string;
  entityType: string;
  version: number;
  status: RuleSetStatus;
  rules: RuleDto[];
  createdAt: string;
  publishedAt: string | null;
  publishedBy: string | null;
  changeNote: string | null;
}

export interface RuleSetSummaryDto {
  id: string;
  familyId: string;
  name: string;
  entityType: string;
  version: number;
  status: RuleSetStatus;
  ruleCount: number;
  createdAt: string;
  publishedAt: string | null;
}

export interface CreateRuleSetRequest {
  name: string;
  entityType: string;
}

export interface UpsertRuleRequest {
  name: string;
  description: string | null;
  condition: string;
  action: RuleActionDto;
  isEnabled: boolean;
  order: number | null;
}

export interface PublishRuleSetRequest {
  changeNote: string | null;
}

export interface ValidateRuleRequest {
  condition: string;
}

export interface ValidateRuleResponse {
  isValid: boolean;
  error: string | null;
  errorPosition: number | null;
  referencedFacts: string[];
}

export interface DryRunRequest {
  discrepancyId: string;
}

export interface RuleEvaluationTraceDto {
  ruleId: string;
  ruleName: string;
  matched: boolean;
  error: string | null;
}

export interface DryRunResponse {
  discrepancyId: string;
  matchedRuleId: string | null;
  matchedRuleName: string | null;
  action: RuleActionDto | null;
  trace: RuleEvaluationTraceDto[];
}

// ----- Cases -----

export interface CaseDto {
  id: string;
  discrepancyId: string;
  runId: string;
  title: string;
  priority: CasePriority;
  state: CaseState;
  assignedRole: string | null;
  assigneeId: string | null;
  escalationLevel: number;
  dueAt: string;
  openedAt: string;
  closedAt: string | null;
  isOverdue: boolean;
  eTag: string;
}

export interface CaseHistoryEntryDto {
  at: string;
  actor: string;
  action: string;
  fromState: string | null;
  toState: string;
  note: string | null;
}

export interface ProposedResolutionDto {
  strategy: ResolutionStrategy | null;
  manualValuesJson: string | null;
  note: string;
  proposedBy: string;
  proposedAt: string;
}

export interface CaseDetailDto {
  case: CaseDto;
  discrepancy: DiscrepancyDetailDto;
  proposal: ProposedResolutionDto | null;
  approvedBy: string | null;
  history: CaseHistoryEntryDto[];
}

export interface AssignCaseRequest {
  assigneeId: string;
}

export interface ProposeResolutionRequest {
  strategy: ResolutionStrategy | null;
  manualValues: Record<string, string | null> | null;
  note: string;
}

export interface RejectProposalRequest {
  reason: string;
}

export interface CancelCaseRequest {
  reason: string;
}

export interface AddCommentRequest {
  note: string;
}

// ----- Operations / dashboard -----

export interface CountByKeyDto {
  key: string;
  count: number;
}

export interface DailyCountDto {
  /** DateOnly serialized as yyyy-MM-dd. */
  day: string;
  detected: number;
  resolved: number;
}

export interface DashboardSummaryDto {
  ingestionInProgress: number;
  ingestionCompletedToday: number;
  ingestionFailedToday: number;
  recordsIngestedToday: number;
  reconciliationRunning: number;
  reconciliationCompletedToday: number;
  openDiscrepancies: number;
  autoResolvedToday: number;
  casesOpen: number;
  casesOverdue: number;
  casesResolvedToday: number;
  deadLetters: number;
  outboxPending: number;
  openDiscrepanciesByType: CountByKeyDto[];
  openCasesByPriority: CountByKeyDto[];
  discrepanciesLast14Days: DailyCountDto[];
}

export interface DeadLetterDto {
  id: string;
  queue: string;
  routingKey: string;
  messageType: string;
  reason: string;
  attempts: number;
  deadLetteredAt: string;
  replayedAt: string | null;
  payload: string;
}

export interface OutboxHealthDto {
  pending: number;
  failed: number;
  oldestPendingAgeSeconds: number | null;
}

export interface WorkflowInstanceDto {
  id: string;
  workflowType: string;
  subjectId: string;
  status: WorkflowStatus;
  wakeUpAt: string;
  lockOwner: string | null;
  lockExpiresAt: string | null;
  attempts: number;
  lastError: string | null;
  updatedAt: string;
}

// ----- SignalR live updates (DistrictSync.Contracts.Events) -----

export interface IntegrationEventBase {
  eventId: string;
  tenantId: string;
  occurredAt: string;
}

export interface IngestionBatchReceivedEvent extends IntegrationEventBase {
  batchId: string;
  sourceSystemId: string;
  entityType: string;
}
export interface IngestionProgressEvent extends IntegrationEventBase {
  batchId: string;
  processed: number;
  accepted: number;
  rejected: number;
  duplicates: number;
}
export interface IngestionBatchCompletedEvent extends IntegrationEventBase {
  batchId: string;
  sourceSystemId: string;
  entityType: string;
  total: number;
  accepted: number;
  rejected: number;
  duplicates: number;
}
export interface IngestionBatchFailedEvent extends IntegrationEventBase {
  batchId: string;
  sourceSystemId: string;
  reason: string;
}
export interface ReconciliationRequestedEvent extends IntegrationEventBase {
  runId: string;
  entityType: string;
  leftSourceSystemId: string;
  rightSourceSystemId: string;
}
export interface ReconciliationProgressEvent extends IntegrationEventBase {
  runId: string;
  leftRead: number;
  rightRead: number;
  discrepancies: number;
  lastKey: string | null;
}
export interface ReconciliationCompletedEvent extends IntegrationEventBase {
  runId: string;
  entityType: string;
  stats: ReconciliationStatsDto;
}
export interface ReconciliationFailedEvent extends IntegrationEventBase {
  runId: string;
  reason: string;
}
export interface RuleSetPublishedEvent extends IntegrationEventBase {
  ruleSetId: string;
  familyId: string;
  entityType: string;
  version: number;
}
export interface CaseOpenedEvent extends IntegrationEventBase {
  caseId: string;
  discrepancyId: string;
  runId: string;
  priority: CasePriority;
  assignedRole: string | null;
  dueAt: string;
}
export interface CaseStateChangedEvent extends IntegrationEventBase {
  caseId: string;
  fromState: CaseState;
  toState: CaseState;
  actor: string;
}
export interface CaseApprovedEvent extends IntegrationEventBase {
  caseId: string;
  discrepancyId: string;
  approvedBy: string;
}
export interface CaseEscalatedEvent extends IntegrationEventBase {
  caseId: string;
  escalationLevel: number;
  newDueAt: string;
}
export interface MessageDeadLetteredEvent extends IntegrationEventBase {
  deadLetterId: string;
  queue: string;
  messageType: string;
  reason: string;
}

/** Wire names from EventCatalog, mapped to their payload types. */
export interface LiveEventMap {
  'ingestion.batch-received': IngestionBatchReceivedEvent;
  'ingestion.progress': IngestionProgressEvent;
  'ingestion.batch-completed': IngestionBatchCompletedEvent;
  'ingestion.batch-failed': IngestionBatchFailedEvent;
  'reconciliation.requested': ReconciliationRequestedEvent;
  'reconciliation.progress': ReconciliationProgressEvent;
  'reconciliation.completed': ReconciliationCompletedEvent;
  'reconciliation.failed': ReconciliationFailedEvent;
  'rules.published': RuleSetPublishedEvent;
  'cases.opened': CaseOpenedEvent;
  'cases.state-changed': CaseStateChangedEvent;
  'cases.approved': CaseApprovedEvent;
  'cases.escalated': CaseEscalatedEvent;
  'operations.dead-lettered': MessageDeadLetteredEvent;
}

export type LiveEventKind = keyof LiveEventMap;

/** Payload of the `liveUpdate` client method on /hubs/operations. */
export interface LiveUpdate<K extends LiveEventKind = LiveEventKind> {
  kind: K;
  at: string;
  payload: LiveEventMap[K];
}
