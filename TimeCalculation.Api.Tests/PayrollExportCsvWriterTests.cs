using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using NodaTime;
using TimeCalculation.Api.Services;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>Pure — no DB, no [Collection("Api")], matching DateRangeTests' precedent.</summary>
public class PayrollExportCsvWriterTests
{
    private static PayrollExportRow Row(
        string earningCode, PayrollExportValueBasis basis, decimal hours, decimal amount, LocalDate? workDate = null) => new()
    {
        EmployeeId = 1,
        ExternalEmployeeId = "EXT-1",
        PeriodStart = new LocalDate(2026, 1, 1),
        PeriodEnd = new LocalDate(2026, 1, 14),
        WorkDate = workDate,
        EarningCode = earningCode,
        ValueBasis = basis,
        Hours = hours,
        Amount = amount,
        ExactHours = hours,
        ExactAmount = amount,
        Rate = null,
        LineItemCount = 1,
        IsRoundingAdjusted = false,
    };

    /// <summary>Parses the writer's own output back with CsvHelper's reader rather than asserting
    /// exact raw text — robust to quoting/newline conventions that aren't the point of these tests.</summary>
    private static List<Dictionary<string, string>> ParseBack(byte[] csvBytes)
    {
        using var stream = new MemoryStream(csvBytes);
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord!;
        var records = new List<Dictionary<string, string>>();

        while (csv.Read())
        {
            records.Add(headers.ToDictionary(h => h, h => csv.GetField(h) ?? string.Empty));
        }

        return records;
    }

    [Fact]
    public void Write_ProducesTheDocumentedHeaderRow_EvenForZeroRows()
    {
        var bytes = PayrollExportCsvWriter.Write([]);
        var text = Encoding.UTF8.GetString(bytes).TrimEnd();

        Assert.Equal("EmployeeId,ExternalEmployeeId,EarningCode,WorkDate,Hours,Amount", text);
    }

    [Fact]
    public void HoursBasisRow_LeavesAmountBlank()
    {
        var row = Row("REG", PayrollExportValueBasis.Hours, hours: 8m, amount: 160m);

        var record = Assert.Single(ParseBack(PayrollExportCsvWriter.Write([row])));

        Assert.Equal("8", record["Hours"]);
        Assert.Equal(string.Empty, record["Amount"]);
    }

    [Fact]
    public void AmountBasisRow_LeavesHoursBlank()
    {
        // The row this exists to protect: an OvertimePremium line priced off the weighted regular
        // rate. Writing Hours here alongside an Amount-basis EarningCode would let a provider whose
        // code is configured "hours x its own rate" re-price it -- double-paying the premium.
        var row = Row("OT", PayrollExportValueBasis.Amount, hours: 2m, amount: 30m);

        var record = Assert.Single(ParseBack(PayrollExportCsvWriter.Write([row])));

        Assert.Equal(string.Empty, record["Hours"]);
        Assert.Equal("30", record["Amount"]);
    }

    [Fact]
    public void PayPeriodGrouping_LeavesWorkDateBlank()
    {
        var row = Row("REG", PayrollExportValueBasis.Hours, 8m, 160m, workDate: null);

        var record = Assert.Single(ParseBack(PayrollExportCsvWriter.Write([row])));

        Assert.Equal(string.Empty, record["WorkDate"]);
    }

    [Fact]
    public void WorkDateGrouping_PopulatesWorkDate_InIsoFormat()
    {
        var row = Row("REG", PayrollExportValueBasis.Hours, 8m, 160m, workDate: new LocalDate(2026, 1, 5));

        var record = Assert.Single(ParseBack(PayrollExportCsvWriter.Write([row])));

        Assert.Equal("2026-01-05", record["WorkDate"]);
    }

    [Fact]
    public void FractionalValues_RoundTrip_ThroughInvariantCultureFormatting()
    {
        var row = Row("REG", PayrollExportValueBasis.Hours, hours: 8.25m, amount: 165.5025m);

        var record = Assert.Single(ParseBack(PayrollExportCsvWriter.Write([row])));

        Assert.Equal("8.25", record["Hours"]);
    }

    [Fact]
    public void EmployeeIdAndExternalEmployeeId_AreAlwaysWritten_RegardlessOfValueBasis()
    {
        var row = Row("REG", PayrollExportValueBasis.Hours, 8m, 160m) with
        {
            EmployeeId = 42,
            ExternalEmployeeId = "ADP-042",
        };

        var record = Assert.Single(ParseBack(PayrollExportCsvWriter.Write([row])));

        Assert.Equal("42", record["EmployeeId"]);
        Assert.Equal("ADP-042", record["ExternalEmployeeId"]);
    }

    [Fact]
    public void MultipleRows_EachWriteTheirOwnRecord_InOrder()
    {
        var rows = new[]
        {
            Row("REG", PayrollExportValueBasis.Hours, 8m, 160m),
            Row("OT", PayrollExportValueBasis.Amount, 2m, 30m),
        };

        var records = ParseBack(PayrollExportCsvWriter.Write(rows));

        Assert.Equal(2, records.Count);
        Assert.Equal("REG", records[0]["EarningCode"]);
        Assert.Equal("OT", records[1]["EarningCode"]);
    }
}
