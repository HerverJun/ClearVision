using System.Reflection;
using ClearVision.Product.Desktop.Hubs;
using ClearVision.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class StationHubContractTests
{
    [Fact]
    public void StationHubMethods_ShouldStayInSyncWithStationHub()
    {
        var hubMethodNames = typeof(StationHub)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.Name != nameof(Hub.OnDisconnectedAsync))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        var contractMethodNames = typeof(StationHubMethods)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => field.GetRawConstantValue())
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        contractMethodNames.Should().BeEquivalentTo(hubMethodNames);
    }
}
