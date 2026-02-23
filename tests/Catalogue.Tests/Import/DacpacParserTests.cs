// tests/Catalogue.Tests/Import/DacpacParserTests.cs
using Catalogue.Infrastructure.Import;
using FluentAssertions;
using System.IO.Compression;
using System.Text;

namespace Catalogue.Tests.Import;

public class DacpacParserTests
{
    /// <summary>Build an in-memory DACPAC (a ZIP archive containing model.xml) using the
    /// actual serialization namespace that DacpacParserService expects.
    /// </summary>
    private static MemoryStream BuildMinimalDacpac(string modelXml)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("model.xml");
            using var entryStream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(modelXml);
            entryStream.Write(bytes, 0, bytes.Length);
        }
        // After disposing the ZipArchive the central directory is finalized.
        // Seek back to the beginning so the parser can read from position 0.
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    // Namespace expected by DacpacParserService
    private const string DsqlNs = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02";

    private static string SampleModelXml => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <DataSchemaModel xmlns="{DsqlNs}">
          <Model>
            <Element Type="SqlTable" Name="[dbo].[Orders]">
              <Element Type="SqlSimpleColumn" Name="[dbo].[Orders].[OrderId]" />
              <Element Type="SqlSimpleColumn" Name="[dbo].[Orders].[CustomerId]" />
            </Element>
            <Element Type="SqlTable" Name="[dbo].[Customers]">
              <Element Type="SqlSimpleColumn" Name="[dbo].[Customers].[CustomerId]" />
            </Element>
          </Model>
        </DataSchemaModel>
        """;

    [Fact]
    public void Parse_extracts_all_columns()
    {
        var service = new DacpacParserService();
        using var stream = BuildMinimalDacpac(SampleModelXml);

        var rows = service.Parse(stream, "PROD-SQL", "OrdersDb");

        rows.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_extracts_correct_table_names()
    {
        var service = new DacpacParserService();
        using var stream = BuildMinimalDacpac(SampleModelXml);

        var rows = service.Parse(stream, "PROD-SQL", "OrdersDb");

        rows.Select(r => r.TableName).Distinct()
            .Should().BeEquivalentTo("Orders", "Customers");
    }

    [Fact]
    public void Parse_extracts_correct_column_names()
    {
        var service = new DacpacParserService();
        using var stream = BuildMinimalDacpac(SampleModelXml);

        var rows = service.Parse(stream, "PROD-SQL", "OrdersDb");

        rows.Select(r => r.ColumnName)
            .Should().BeEquivalentTo("OrderId", "CustomerId", "CustomerId");
    }

    [Fact]
    public void Parse_sets_correct_server_and_database()
    {
        var service = new DacpacParserService();
        using var stream = BuildMinimalDacpac(SampleModelXml);

        var rows = service.Parse(stream, "MY-SERVER", "MY-DB");

        rows.Should().AllSatisfy(r =>
        {
            r.ServerName.Should().Be("MY-SERVER");
            r.DatabaseName.Should().Be("MY-DB");
        });
    }

    [Fact]
    public void Parse_sets_default_schema_to_dbo()
    {
        var service = new DacpacParserService();
        using var stream = BuildMinimalDacpac(SampleModelXml);

        var rows = service.Parse(stream, "S", "D");

        rows.Should().AllSatisfy(r => r.SchemaName.Should().Be("dbo"));
    }

    [Fact]
    public void Parse_empty_model_returns_empty_list()
    {
        var emptyXml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <DataSchemaModel xmlns="{DsqlNs}">
              <Model />
            </DataSchemaModel>
            """;

        var service = new DacpacParserService();
        using var stream = BuildMinimalDacpac(emptyXml);

        var rows = service.Parse(stream, "S", "D");

        rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_throws_when_model_xml_not_in_archive()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            zip.CreateEntry("other.xml");
        ms.Position = 0;

        var service = new DacpacParserService();
        var act = () => service.Parse(ms, "S", "D");

        act.Should().Throw<InvalidDataException>();
    }
}
