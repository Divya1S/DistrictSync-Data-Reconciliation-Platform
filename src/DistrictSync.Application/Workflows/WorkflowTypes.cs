namespace DistrictSync.Application.Workflows;

public static class WorkflowTypes
{
    /// <summary>Watches a case's SLA; escalates when overdue.</summary>
    public const string CaseSla = "case-sla";

    /// <summary>Applies an approved or auto-resolved outcome to the losing source system, with retries.</summary>
    public const string ApplyResolution = "apply-resolution";
}
