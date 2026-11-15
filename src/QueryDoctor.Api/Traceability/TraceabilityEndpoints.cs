using System.Text.Json;
using System.Text.Json.Serialization;
using QueryDoctor.Api.Auth;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Audit;

namespace QueryDoctor.Api.Traceability;

/// <summary>Entrada de traza para la Api (RF-TRZ-01, CU-06).</summary>
internal sealed record TraceEntryView(
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("recorded_at")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("explanation_id")] Guid ExplanationId,
    [property: JsonPropertyName("query_id")] Guid QueryId,
    [property: JsonPropertyName("connection_id")] Guid ConnectionId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("cause")] string? Cause,
    [property: JsonPropertyName("validation_status")] string? ValidationStatus,
    [property: JsonPropertyName("finding_codes")] IReadOnlyList<string> FindingCodes,
    [property: JsonPropertyName("catalog_version")] string? CatalogVersion,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("prompt_version")] string? PromptVersion,
    [property: JsonPropertyName("llm_profile")] string? LlmProfile,
    [property: JsonPropertyName("served_by")] string? ServedBy,
    [property: JsonPropertyName("data_policy")] string? DataPolicy,
    [property: JsonPropertyName("context_hash")] string? ContextHash,
    [property: JsonPropertyName("reused_from_id")] Guid? ReusedFromId,
    [property: JsonPropertyName("entry_hash")] string EntryHash)
{
    public static TraceEntryView From(TraceEntry e) => new(
        e.Seq,
        e.RecordedAt,
        e.ExplanationId,
        e.QueryId,
        e.ConnectionId,
        e.State,
        e.Cause,
        e.ValidationStatus,
        JsonSerializer.Deserialize<string[]>(e.FindingCodesJson) ?? [],
        e.CatalogVersion,
        e.Model,
        e.PromptVersion,
        e.LlmProfile,
        e.ServedBy,
        e.DataPolicy,
        e.ContextHash,
        e.ReusedFromId,
        e.EntryHash);
}

/// <summary>Evento de auditoría para la Api (RF-TRZ-05).</summary>
internal sealed record AuditEventView(
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("recorded_at")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("user_id")] Guid? UserId,
    [property: JsonPropertyName("user_name")] string? UserName,
    [property: JsonPropertyName("subject_type")] string? SubjectType,
    [property: JsonPropertyName("subject_id")] Guid? SubjectId,
    [property: JsonPropertyName("outcome")] string Outcome)
{
    public static AuditEventView From(AuditEvent e) =>
        new(e.Seq, e.RecordedAt, e.EventType.ToString(), e.UserId, e.UserName, e.SubjectType, e.SubjectId, e.Outcome);
}

/// <summary>Resultado de verificar una cadena.</summary>
internal sealed record ChainVerificationView(
    [property: JsonPropertyName("intact")] bool Intact,
    [property: JsonPropertyName("entries_checked")] long EntriesChecked,
    [property: JsonPropertyName("first_broken_seq")] long? FirstBrokenSeq,
    [property: JsonPropertyName("reason")] string? Reason)
{
    public static ChainVerificationView From(ChainVerification v) => new(v.IsIntact, v.EntriesChecked, v.FirstBrokenSeq, v.Reason);
}

/// <summary>
/// Registro de trazabilidad y log de auditoría (S2-06, CU-06). Solo lectura: no existe ninguna ruta para modificar o
/// borrar entradas (RF-TRZ-02, CA-HU14.2), y la base además lo rechaza. Solo DBA: un rol Auditor depende de DA-14.
/// </summary>
internal static class TraceabilityEndpoints
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;

    public static IEndpointRouteBuilder MapTraceabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var trace = app.MapGroup("/trace").RequireAuthorization(AuthConstants.DbaOnlyPolicy);
        trace.MapGet("/", SearchTraceAsync);
        trace.MapGet("/verify", VerifyTraceAsync);

        var audit = app.MapGroup("/audit").RequireAuthorization(AuthConstants.DbaOnlyPolicy);
        audit.MapGet("/", SearchAuditAsync);
        audit.MapGet("/verify", VerifyAuditAsync);

        return app;
    }

    private static async Task<IResult> SearchTraceAsync(
        ITraceabilityQueries queries,
        Guid? queryId,
        Guid? explanationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? take,
        CancellationToken cancellationToken)
    {
        if (InvalidRange(from, to, take) is { } problem)
        {
            return problem;
        }

        var entries = await queries.SearchTraceAsync(
            new TraceFilter(queryId, explanationId, from, to, take ?? DefaultTake),
            cancellationToken);
        return Results.Ok(entries.Select(TraceEntryView.From));
    }

    private static async Task<IResult> SearchAuditAsync(
        ITraceabilityQueries queries,
        string? type,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? take,
        CancellationToken cancellationToken)
    {
        if (InvalidRange(from, to, take) is { } problem)
        {
            return problem;
        }

        AuditEventType? eventType = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse<AuditEventType>(type, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Tipo de evento desconocido.");
            }

            eventType = parsed;
        }

        var events = await queries.SearchAuditAsync(new AuditFilter(eventType, from, to, take ?? DefaultTake), cancellationToken);
        return Results.Ok(events.Select(AuditEventView.From));
    }

    private static async Task<IResult> VerifyTraceAsync(ITraceabilityQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(ChainVerificationView.From(await queries.VerifyTraceAsync(cancellationToken)));

    private static async Task<IResult> VerifyAuditAsync(ITraceabilityQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(ChainVerificationView.From(await queries.VerifyAuditAsync(cancellationToken)));

    private static IResult? InvalidRange(DateTimeOffset? from, DateTimeOffset? to, int? take)
    {
        if (from is not null && to is not null && from > to)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "La fecha inicial es posterior a la final.");
        }

        if (take is < 1 or > MaxTake)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: $"take debe estar entre 1 y {MaxTake}.");
        }

        return null;
    }
}
