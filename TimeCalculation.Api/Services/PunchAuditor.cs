using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Builds <see cref="PunchAuditEntry"/> rows — the before/after JSON-snapshot shape every mutation of
/// a <see cref="Punch"/> shares, so the create path (<see cref="PunchService"/>) and the edit/delete/
/// approve paths that follow it (Phase 6.1/6.2) don't each reinvent how a punch gets snapshotted.
/// Pure construction, no DB access: the caller adds the returned entry to the context and calls
/// SaveChanges alongside whatever Punch write it documents — see <see cref="PunchService.CreateAsync"/>
/// for why Created specifically needs the punch's real (post-insert) Id passed in rather than deriving
/// it from the punch itself.
/// </summary>
public static class PunchAuditor
{
    // A bare JsonSerializer.Serialize(punch) can't handle Punch.PunchTime (a NodaTime Instant)
    // without this — the same NodaTime + enum-as-string configuration Program.cs applies to the
    // public API's own JSON pipeline, just a separate instance, since this serialization is an
    // internal audit snapshot and never touches an HTTP response.
    private static readonly JsonSerializerOptions SnapshotOptions = CreateSnapshotOptions();

    public static PunchAuditEntry Created(Punch punch, string actorUserId, Instant occurredAt) => new()
    {
        ClientId = punch.ClientId,
        PunchId = punch.Id,
        ActorUserId = actorUserId,
        OccurredAt = occurredAt,
        Action = "Created",
        NewValues = Serialize(punch),
    };

    public static PunchAuditEntry Edited(
        Punch previous, Punch updated, string actorUserId, Instant occurredAt, string? reason) => new()
    {
        ClientId = updated.ClientId,
        PunchId = updated.Id,
        ActorUserId = actorUserId,
        OccurredAt = occurredAt,
        Action = "Edited",
        PreviousValues = Serialize(previous),
        NewValues = Serialize(updated),
        Reason = reason,
    };

    public static PunchAuditEntry Deleted(
        Punch punch, string actorUserId, Instant occurredAt, string? reason) => new()
    {
        ClientId = punch.ClientId,
        PunchId = punch.Id,
        ActorUserId = actorUserId,
        OccurredAt = occurredAt,
        Action = "Deleted",
        PreviousValues = Serialize(punch),
        Reason = reason,
    };

    // Employee/Position are nullable navigation properties that may or may not be populated
    // depending on what the caller happened to load — nulled out first so the snapshot's shape
    // stays consistent (and compact) regardless of query context, rather than sometimes embedding a
    // full Employee/Position graph and sometimes not.
    private static string Serialize(Punch punch) =>
        JsonSerializer.Serialize(punch with { Employee = null, Position = null }, SnapshotOptions);

    private static JsonSerializerOptions CreateSnapshotOptions()
    {
        var options = new JsonSerializerOptions();
        options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
