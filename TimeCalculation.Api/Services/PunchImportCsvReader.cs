using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace TimeCalculation.Api.Services;

/// <summary>One data row of an uploaded punch-import CSV, still in raw string form — nothing here
/// has been validated or type-converted yet. RowNumber is 1-based counting only data rows (the
/// header doesn't count), matching what a person editing the file in a spreadsheet would call "row
/// 1" of their data.</summary>
public sealed record PunchImportRow
{
    public required int RowNumber { get; init; }
    public required IReadOnlyDictionary<string, string?> Fields { get; init; }

    public string? Get(string header) => Fields.GetValueOrDefault(header);
}

public sealed record PunchImportParseResult
{
    public IReadOnlyList<PunchImportRow> Rows { get; init; } = [];

    /// <summary>Set only when the file itself couldn't be read at all (empty file, missing required
    /// columns) — a problem with every row at once, not any single one, so it's kept separate from
    /// per-row validation errors rather than awkwardly attached to a fake "row 0".</summary>
    public IDictionary<string, string[]>? FileError { get; init; }
}

/// <summary>
/// Turns an uploaded CSV into rows of raw strings — mechanics only (quoting, embedded commas,
/// header matching). Deliberately does none of the semantic work: type conversion, required-field
/// checks, and business rules all belong to PunchImportRowValidator, which is what actually produces
/// per-field error messages a person can act on. Configured maximally leniently (MissingFieldFound/
/// BadDataFound both null, i.e. "don't throw, just hand back what you've got") so a malformed row
/// becomes a validation error on that row rather than an exception that aborts reading every row
/// after it — the whole point of importing being all-or-nothing is that a person sees every problem
/// in one pass, not one at a time across repeated re-uploads.
/// </summary>
public static class PunchImportCsvReader
{
    public static readonly string[] RequiredHeaders = ["EmployeeId", "PunchTime", "Kind"];

    public static PunchImportParseResult Parse(Stream csvStream)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            BadDataFound = null,
        };

        using var reader = new StreamReader(csvStream);
        using var csv = new CsvReader(reader, config);

        if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is null)
        {
            return new PunchImportParseResult
            {
                FileError = new Dictionary<string, string[]>
                {
                    ["file"] = ["The file is empty or has no header row."],
                },
            };
        }

        var headers = csv.HeaderRecord;
        var missing = RequiredHeaders
            .Where(required => !headers.Contains(required, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (missing.Count > 0)
        {
            return new PunchImportParseResult
            {
                FileError = new Dictionary<string, string[]>
                {
                    ["file"] = [$"Missing required column(s): {string.Join(", ", missing)}."],
                },
            };
        }

        var rows = new List<PunchImportRow>();
        var rowNumber = 0;
        while (csv.Read())
        {
            rowNumber++;
            var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                fields[header] = csv.GetField(header);
            }
            rows.Add(new PunchImportRow { RowNumber = rowNumber, Fields = fields });
        }

        return new PunchImportParseResult { Rows = rows };
    }
}
