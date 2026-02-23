// tests/Catalogue.Tests/Validators/ValidatorTests.cs
using Catalogue.Core.Models;
using Catalogue.Core.Validation;
using FluentAssertions;
using FluentValidation;
using FluentValidation.TestHelper;

namespace Catalogue.Tests.Validators;

public class SourceValidatorTests
{
    private readonly SourceValidator _validator = new();

    [Fact]
    public void Valid_source_passes()
    {
        var source = new Source { ServerName = "PROD-SQL-01", Description = "Production server" };
        var result = _validator.TestValidate(source);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_server_name_fails()
    {
        var source = new Source { ServerName = "" };
        var result = _validator.TestValidate(source);
        result.ShouldHaveValidationErrorFor(s => s.ServerName);
    }

    [Fact]
    public void Server_name_over_255_chars_fails()
    {
        var source = new Source { ServerName = new string('A', 256) };
        var result = _validator.TestValidate(source);
        result.ShouldHaveValidationErrorFor(s => s.ServerName);
    }
}

public class SourceDatabaseValidatorTests
{
    private readonly SourceDatabaseValidator _validator = new();

    [Fact]
    public void Valid_database_passes()
    {
        var db = new SourceDatabase { SourceId = 1, DatabaseName = "Orders" };
        var result = _validator.TestValidate(db);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Missing_source_id_fails()
    {
        var db = new SourceDatabase { SourceId = 0, DatabaseName = "Orders" };
        var result = _validator.TestValidate(db);
        result.ShouldHaveValidationErrorFor(d => d.SourceId);
    }

    [Fact]
    public void Empty_database_name_fails()
    {
        var db = new SourceDatabase { SourceId = 1, DatabaseName = "" };
        var result = _validator.TestValidate(db);
        result.ShouldHaveValidationErrorFor(d => d.DatabaseName);
    }
}

public class SourceColumnValidatorTests
{
    private readonly SourceColumnValidator _validator = new();

    [Fact]
    public void Valid_column_with_R_persistence_passes()
    {
        var col = new SourceColumn { TableId = 1, ColumnName = "CustomerId", PersistenceType = 'R' };
        var result = _validator.TestValidate(col);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Valid_column_with_D_persistence_passes()
    {
        var col = new SourceColumn { TableId = 1, ColumnName = "Payload", PersistenceType = 'D' };
        var result = _validator.TestValidate(col);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Invalid_persistence_type_fails()
    {
        var col = new SourceColumn { TableId = 1, ColumnName = "Col", PersistenceType = 'X' };
        var result = _validator.TestValidate(col);
        result.ShouldHaveValidationErrorFor(c => c.PersistenceType);
    }

    [Fact]
    public void Empty_column_name_fails()
    {
        var col = new SourceColumn { TableId = 1, ColumnName = "", PersistenceType = 'R' };
        var result = _validator.TestValidate(col);
        result.ShouldHaveValidationErrorFor(c => c.ColumnName);
    }
}
