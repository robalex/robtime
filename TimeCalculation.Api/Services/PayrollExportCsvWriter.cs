using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Turns projected export rows into a CSV file. Pure — takes the projector's own output, returns
/// bytes, no DB, no formatting decision deferred to a caller. First CsvHelper writer in this
/// codebase (PunchImportCsvReader is read-only); CultureInfo.InvariantCulture mirrors that reader's
/// own convention, so numeric/date formatting never depends on the server's locale.
///
/// Columns: EmployeeId, ExternalEmployeeId, EarningCode, WorkDate, Hours, Amount. Hours is written
/// only when the row's ValueBasis is Hours; Amount only when it's Amount — the other column is left
/// blank on that row. This is deliberate, not an oversight: emitting both would let a provider whose
/// earning code is configured "hours × its own rate" re-price a row RobTime already priced in
/// dollars, silently double-paying it. WorkDate is blank under PayPeriod grouping (the row's
/// WorkDate is null) and populated under WorkDate grouping.
/// </summary>
public static class PayrollExportCsvWriter
{
    // Encoding.UTF8 emits a byte-order-mark preamble by default -- fine for Excel, but some payroll
    // bulk-import parsers on the receiving end choke on a leading BOM in a plain CSV. Explicitly
    // opted out rather than relying on the default, since the failure mode (a parser rejecting or
    // mis-reading the very first header cell) would be silent until someone's file got bounced.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static byte[] Write(IReadOnlyList<PayrollExportRow> rows)
    {
        using var memoryStream = new MemoryStream();

        // leaveOpen on the StreamWriter so disposing it (which flushes CsvWriter's buffered output)
        // doesn't also close the MemoryStream out from under the ToArray() call below.
        using (var writer = new StreamWriter(memoryStream, Utf8NoBom, leaveOpen: true))
        using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
        {
            csv.WriteField("EmployeeId");
            csv.WriteField("ExternalEmployeeId");
            csv.WriteField("EarningCode");
            csv.WriteField("WorkDate");
            csv.WriteField("Hours");
            csv.WriteField("Amount");
            csv.NextRecord();

            foreach (var row in rows)
            {
                csv.WriteField(row.EmployeeId);
                csv.WriteField(row.ExternalEmployeeId);
                csv.WriteField(row.EarningCode);
                csv.WriteField(row.WorkDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);
                csv.WriteField(row.ValueBasis == PayrollExportValueBasis.Hours
                    ? row.Hours.ToString(CultureInfo.InvariantCulture)
                    : string.Empty);
                csv.WriteField(row.ValueBasis == PayrollExportValueBasis.Amount
                    ? row.Amount.ToString(CultureInfo.InvariantCulture)
                    : string.Empty);
                csv.NextRecord();
            }
        }

        return memoryStream.ToArray();
    }
}
