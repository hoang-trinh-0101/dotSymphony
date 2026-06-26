using OpenSymphony.Linear;

namespace OpenSymphony.Linear.Tests;

public class SchemaDriftTests
{
    [Fact]
    public void RequiredFieldsContainsIssueCoreFields()
    {
        var fields = RequiredFields.List;
        var issueFields = fields
            .Where(f => f.TypeName == "Issue")
            .Select(f => f.FieldName)
            .ToList();
        Assert.Contains("id", issueFields);
        Assert.Contains("identifier", issueFields);
        Assert.Contains("state", issueFields);
        Assert.Contains("inverseRelations", issueFields);
    }

    [Fact]
    public void RequiredFieldsMarksIdAsCritical()
    {
        foreach (var field in RequiredFields.List)
        {
            if (field.FieldName == "id")
            {
                Assert.True(field.Critical, $"id field on {field.TypeName} must be critical");
            }
        }
    }

    [Fact]
    public void SchemaDriftReportCompatibleWhenNoViolations()
    {
        var report = new SchemaDriftReport
        {
            IsCompatible = true,
            MissingFields = new List<SchemaDriftViolation>(),
            CheckedAt = null,
        };
        Assert.True(report.IsCompatible);
        Assert.Empty(report.MissingFields);
    }

    [Fact]
    public void SchemaDriftReportIncompatibleWithViolations()
    {
        var report = new SchemaDriftReport
        {
            IsCompatible = false,
            MissingFields =
            [
                new SchemaDriftViolation
                {
                    TypeName = "Issue",
                    FieldName = "deletedField",
                    Critical = true,
                    Remediation = "Remove from query",
                },
            ],
            CheckedAt = null,
        };
        Assert.False(report.IsCompatible);
        Assert.Single(report.MissingFields);
        Assert.True(report.MissingFields[0].Critical);
    }
}
