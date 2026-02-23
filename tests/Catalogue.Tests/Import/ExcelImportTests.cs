// tests/Catalogue.Tests/Import/ExcelImportTests.cs
using Catalogue.Infrastructure.Import;
using ClosedXML.Excel;
using FluentAssertions;

namespace Catalogue.Tests.Import;

public class ExcelImportTests
{
    /// <summary>Build an in-memory .xlsx workbook with the given headers and rows.</summary>
    private static Stream BuildExcel(string sheetName, string[] headers, string[][] dataRows)
    {
        var ms = new MemoryStream();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);

        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];

        for (int r = 0; r < dataRows.Length; r++)
        for (int c = 0; c < dataRows[r].Length; c++)
            ws.Cell(r + 2, c + 1).Value = dataRows[r][c];

        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Parse_reads_all_rows_from_All_sheet()
    {
        var headers = new[] { "Server", "Database", "Schema", "Table", "Column" };
        var data = new[]
        {
            new[] { "SRV1", "DB1", "dbo", "Orders", "OrderId" },
            new[] { "SRV1", "DB1", "dbo", "Orders", "CustomerId" },
        };
        using var stream = BuildExcel("All", headers, data);
        var svc = new ExcelImportService();

        var (rows, _) = svc.Parse(stream);

        rows.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_maps_known_headers_correctly()
    {
        var headers = new[] { "Server Name", "Database Name", "Schema", "Table Name", "Column Name" };
        var data = new[] { new[] { "SRV1", "DB1", "dbo", "Orders", "OrderId" } };
        using var stream = BuildExcel("All", headers, data);
        var svc = new ExcelImportService();

        var (rows, _) = svc.Parse(stream);

        var row = rows.Single();
        row.ServerName.Should().Be("SRV1");
        row.DatabaseName.Should().Be("DB1");
        row.SchemaName.Should().Be("dbo");
        row.TableName.Should().Be("Orders");
        row.ColumnName.Should().Be("OrderId");
    }

    [Fact]
    public void Parse_maps_legacy_Generate_SQL_INSERTS_to_IsSelectedForLoad()
    {
        var headers = new[] { "Server", "Database", "Schema", "Table", "Column", "Generate SQL INSERTS" };
        var data = new[] { new[] { "S", "D", "dbo", "T", "Col", "TRUE" } };
        using var stream = BuildExcel("All", headers, data);
        var svc = new ExcelImportService();

        var (rows, _) = svc.Parse(stream);

        rows.Single().IsSelectedForLoad.Should().BeTrue();
    }

    [Theory]
    [InlineData("TRUE",  true)]
    [InlineData("true",  true)]
    [InlineData("1",     true)]
    [InlineData("Yes",   true)]
    [InlineData("Y",     true)]
    [InlineData("FALSE", false)]
    [InlineData("0",     false)]
    [InlineData("No",    false)]
    [InlineData("",      false)]
    public void Parse_bool_values_handled_correctly(string cellValue, bool expected)
    {
        var headers = new[] { "Server", "Database", "Schema", "Table", "Column", "DAO" };
        var data = new[] { new[] { "S", "D", "dbo", "T", "Col", cellValue } };
        using var stream = BuildExcel("All", headers, data);
        var svc = new ExcelImportService();

        var (rows, _) = svc.Parse(stream);

        rows.Single().IsInDaoAnalysis.Should().Be(expected);
    }

    [Fact]
    public void Parse_flags_unrecognised_headers()
    {
        var headers = new[] { "Server", "Database", "Table", "Column", "UnknownField", "AnotherUnknown" };
        var data = new[] { new[] { "S", "D", "T", "Col", "x", "y" } };
        using var stream = BuildExcel("All", headers, data);
        var svc = new ExcelImportService();

        var (_, unrecognised) = svc.Parse(stream);

        unrecognised.Should().BeEquivalentTo("UnknownField", "AnotherUnknown");
    }

    [Fact]
    public void Parse_defaults_persistence_type_to_R_when_empty()
    {
        var headers = new[] { "Server", "Database", "Schema", "Table", "Column", "PersistenceType" };
        var data = new[] { new[] { "S", "D", "dbo", "T", "Col", "" } };
        using var stream = BuildExcel("All", headers, data);
        var svc = new ExcelImportService();

        var (rows, _) = svc.Parse(stream);

        rows.Single().PersistenceType.Should().Be('R');
    }

    [Fact]
    public void Parse_warns_for_empty_column_name()
    {
        var headers = new[] { "Server", "Database", "Schema", "Table", "Column" };
        var data = new[] { new[] { "S", "D", "dbo", "T", "" } };
        using var stream = BuildExcel("All", headers, data);
        var svc = new ExcelImportService();

        var (rows, _) = svc.Parse(stream);

        rows.Single().Warning.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Parse_falls_back_to_first_sheet_when_no_All_sheet()
    {
        var headers = new[] { "Server", "Database", "Schema", "Table", "Column" };
        var data = new[] { new[] { "SRV1", "DB1", "dbo", "Orders", "OrderId" } };
        using var stream = BuildExcel("Data", headers, data);   // sheet named "Data" not "All"
        var svc = new ExcelImportService();

        var (rows, _) = svc.Parse(stream);

        rows.Should().HaveCount(1);
        rows.Single().ServerName.Should().Be("SRV1");
    }
}
