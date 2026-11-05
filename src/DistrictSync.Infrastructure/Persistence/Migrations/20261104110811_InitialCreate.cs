using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistrictSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ds");

            migrationBuilder.CreateTable(
                name: "Cases",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscrepancyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AssignedRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AssigneeId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EscalationLevel = table.Column<int>(type: "int", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProposalStrategy = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ProposalManualValuesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    ProposalNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ProposalProposedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProposalProposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OpenedByRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComparisonProfiles",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeadLetterMessages",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Queue = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RoutingKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    DeadLetteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReplayedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReplayedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadLetterMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Discrepancies",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MatchKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_BIN2"),
                    LeftSourceSystemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RightSourceSystemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeftRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RightRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppliedRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DifferencesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Discrepancies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "ds",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Consumer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FirstReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => new { x.MessageId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "IngestionBatches",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StagingReference = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalRecords = table.Column<int>(type: "int", nullable: false),
                    AcceptedRecords = table.Column<int>(type: "int", nullable: false),
                    RejectedRecords = table.Column<int>(type: "int", nullable: false),
                    DuplicateRecords = table.Column<int>(type: "int", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: false),
                    TraceParent = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockOwner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LockExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationRuns",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LeftSourceSystemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RightSourceSystemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComparisonProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RuleSetVersion = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Stats_LeftRecords = table.Column<long>(type: "bigint", nullable: false),
                    Stats_RightRecords = table.Column<long>(type: "bigint", nullable: false),
                    Stats_Matched = table.Column<long>(type: "bigint", nullable: false),
                    Stats_MissingInLeft = table.Column<long>(type: "bigint", nullable: false),
                    Stats_MissingInRight = table.Column<long>(type: "bigint", nullable: false),
                    Stats_FieldConflicts = table.Column<long>(type: "bigint", nullable: false),
                    Stats_Duplicates = table.Column<long>(type: "bigint", nullable: false),
                    Stats_AutoResolved = table.Column<long>(type: "bigint", nullable: false),
                    Stats_CasesOpened = table.Column<long>(type: "bigint", nullable: false),
                    Stats_Ignored = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Checkpoint = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_BIN2"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuleSets",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangeNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceRecords",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MatchKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_BIN2"),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IngestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceSystems",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TrustRank = table.Column<int>(type: "int", nullable: false),
                    ConfigJson = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: false),
                    ApiKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceSystems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MaxConcurrentReconciliations = table.Column<int>(type: "int", nullable: false),
                    MaxConcurrentIngestions = table.Column<int>(type: "int", nullable: false),
                    IngestionRequestsPerMinute = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowInstances",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StateJson = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    WakeUpAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LockOwner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LockExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CaseHistory",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FromState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ToState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseHistory_Cases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "ds",
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rules",
                schema: "ds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ActionKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActionStrategy = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ActionPriority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ActionAssignToRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rules_RuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalSchema: "ds",
                        principalTable: "RuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseHistory_CaseId_At",
                schema: "ds",
                table: "CaseHistory",
                columns: new[] { "CaseId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Cases_TenantId_AssigneeId",
                schema: "ds",
                table: "Cases",
                columns: new[] { "TenantId", "AssigneeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Cases_TenantId_DiscrepancyId",
                schema: "ds",
                table: "Cases",
                columns: new[] { "TenantId", "DiscrepancyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cases_TenantId_State_DueAt",
                schema: "ds",
                table: "Cases",
                columns: new[] { "TenantId", "State", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonProfiles_TenantId_EntityType_IsDefault",
                schema: "ds",
                table: "ComparisonProfiles",
                columns: new[] { "TenantId", "EntityType", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterMessages_TenantId_ReplayedAt_DeadLetteredAt",
                schema: "ds",
                table: "DeadLetterMessages",
                columns: new[] { "TenantId", "ReplayedAt", "DeadLetteredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Discrepancies_TenantId_DetectedAt",
                schema: "ds",
                table: "Discrepancies",
                columns: new[] { "TenantId", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Discrepancies_TenantId_RunId_Status",
                schema: "ds",
                table: "Discrepancies",
                columns: new[] { "TenantId", "RunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Discrepancies_TenantId_Status_Severity",
                schema: "ds",
                table: "Discrepancies",
                columns: new[] { "TenantId", "Status", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_FirstReceivedAt",
                schema: "ds",
                table: "InboxMessages",
                column: "FirstReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionBatches_TenantId_SourceSystemId_ContentHash",
                schema: "ds",
                table: "IngestionBatches",
                columns: new[] { "TenantId", "SourceSystemId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionBatches_TenantId_Status_ReceivedAt",
                schema: "ds",
                table: "IngestionBatches",
                columns: new[] { "TenantId", "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt",
                schema: "ds",
                table: "OutboxMessages",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Pending",
                schema: "ds",
                table: "OutboxMessages",
                column: "OccurredAt",
                filter: "[ProcessedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRuns_TenantId_Status_RequestedAt",
                schema: "ds",
                table: "ReconciliationRuns",
                columns: new[] { "TenantId", "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RuleSets_TenantId_EntityType_Status",
                schema: "ds",
                table: "RuleSets",
                columns: new[] { "TenantId", "EntityType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RuleSets_TenantId_FamilyId_Version",
                schema: "ds",
                table: "RuleSets",
                columns: new[] { "TenantId", "FamilyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rules_RuleSetId_Order",
                schema: "ds",
                table: "Rules",
                columns: new[] { "RuleSetId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceRecords_TenantId_SourceSystemId_EntityType_ExternalId",
                schema: "ds",
                table: "SourceRecords",
                columns: new[] { "TenantId", "SourceSystemId", "EntityType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceRecords_TenantId_SourceSystemId_EntityType_MatchKey_ExternalId",
                schema: "ds",
                table: "SourceRecords",
                columns: new[] { "TenantId", "SourceSystemId", "EntityType", "MatchKey", "ExternalId" },
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "SourceUpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceSystems_TenantId_Code",
                schema: "ds",
                table: "SourceSystems",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                schema: "ds",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TenantId",
                schema: "ds",
                table: "Tenants",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_Status_WakeUpAt",
                schema: "ds",
                table: "WorkflowInstances",
                columns: new[] { "Status", "WakeUpAt" },
                filter: "[Status] IN ('Scheduled','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_TenantId_WorkflowType_SubjectId",
                schema: "ds",
                table: "WorkflowInstances",
                columns: new[] { "TenantId", "WorkflowType", "SubjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseHistory",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "ComparisonProfiles",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "DeadLetterMessages",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "Discrepancies",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "IngestionBatches",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "ReconciliationRuns",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "Rules",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "SourceRecords",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "SourceSystems",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "WorkflowInstances",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "Cases",
                schema: "ds");

            migrationBuilder.DropTable(
                name: "RuleSets",
                schema: "ds");
        }
    }
}
