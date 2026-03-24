using FlatPlanet.Platform.Application.Common.Helpers;

namespace FlatPlanet.Platform.Tests.UnitTests.Helpers;

public sealed class SqlValidationHelperTests
{
    #region Schema name validation

    [Theory]
    [InlineData("project_abc")]
    [InlineData("project_abc123")]
    [InlineData("project_my_project")]
    [InlineData("project_abc_def_ghi")]
    public void IsValidSchemaName_ShouldReturnTrue_WhenSchemaIsValid(string schema)
    {
        var result = SqlValidationHelper.IsValidSchemaName(schema);
        Assert.True(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("public")]
    [InlineData("PROJECT_ABC")]
    [InlineData("project_AB")]
    [InlineData("project_")]
    [InlineData("project_1abc")]
    [InlineData(null!)]
    public void IsValidSchemaName_ShouldReturnFalse_WhenSchemaIsInvalid(string? schema)
    {
        var result = SqlValidationHelper.IsValidSchemaName(schema!);
        Assert.False(result);
    }

    #endregion

    #region Identifier validation

    [Theory]
    [InlineData("customers")]
    [InlineData("order_items")]
    [InlineData("MyTable")]
    [InlineData("_private")]
    [InlineData("col123")]
    public void IsValidIdentifier_ShouldReturnTrue_WhenIdentifierIsValid(string identifier)
    {
        var result = SqlValidationHelper.IsValidIdentifier(identifier);
        Assert.True(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123table")]
    [InlineData("my-table")]
    [InlineData("my table")]
    [InlineData(null!)]
    public void IsValidIdentifier_ShouldReturnFalse_WhenIdentifierIsInvalid(string? identifier)
    {
        var result = SqlValidationHelper.IsValidIdentifier(identifier!);
        Assert.False(result);
    }

    #endregion

    #region Read query validation

    [Theory]
    [InlineData("SELECT * FROM customers")]
    [InlineData("SELECT id, name FROM orders WHERE id = @id")]
    [InlineData("SELECT count(*) FROM products")]
    public void ValidateReadQuery_ShouldReturnValid_WhenQueryIsSafeSelect(string sql)
    {
        var (isValid, error) = SqlValidationHelper.ValidateReadQuery(sql);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("DROP TABLE customers", "DROP")]
    [InlineData("DELETE FROM customers WHERE id = 1", "DELETE")]
    [InlineData("UPDATE customers SET name = 'x'", "UPDATE")]
    [InlineData("INSERT INTO customers VALUES (1)", "INSERT")]
    [InlineData("ALTER TABLE customers ADD COLUMN x text", "ALTER")]
    [InlineData("CREATE TABLE foo (id int)", "CREATE")]
    [InlineData("TRUNCATE customers", "TRUNCATE")]
    [InlineData("GRANT SELECT ON customers TO user1", "GRANT")]
    [InlineData("REVOKE SELECT ON customers FROM user1", "REVOKE")]
    public void ValidateReadQuery_ShouldReturnInvalid_WhenQueryContainsDmlOrDdl(string sql, string keyword)
    {
        var (isValid, error) = SqlValidationHelper.ValidateReadQuery(sql);
        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains(keyword, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateReadQuery_ShouldReturnInvalid_WhenSqlIsEmpty()
    {
        var (isValid, error) = SqlValidationHelper.ValidateReadQuery(string.Empty);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    #endregion

    #region Write query validation

    [Theory]
    [InlineData("INSERT INTO customers (name) VALUES (@name)")]
    [InlineData("UPDATE customers SET name = @name WHERE id = @id")]
    [InlineData("DELETE FROM customers WHERE id = @id")]
    public void ValidateWriteQuery_ShouldReturnValid_WhenQueryIsSafeDml(string sql)
    {
        var (isValid, error) = SqlValidationHelper.ValidateWriteQuery(sql);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("DROP TABLE customers", "DROP")]
    [InlineData("ALTER TABLE customers ADD COLUMN x text", "ALTER")]
    [InlineData("CREATE TABLE foo (id int)", "CREATE")]
    [InlineData("TRUNCATE customers", "TRUNCATE")]
    [InlineData("GRANT SELECT ON customers TO user1", "GRANT")]
    [InlineData("REVOKE SELECT ON customers FROM user1", "REVOKE")]
    public void ValidateWriteQuery_ShouldReturnInvalid_WhenQueryContainsDdl(string sql, string keyword)
    {
        var (isValid, error) = SqlValidationHelper.ValidateWriteQuery(sql);
        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains(keyword, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateWriteQuery_ShouldReturnInvalid_WhenSqlIsEmpty()
    {
        var (isValid, error) = SqlValidationHelper.ValidateWriteQuery(string.Empty);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    #endregion
}
