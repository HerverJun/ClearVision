using System.Reflection;
using Acme.Product.Desktop.Hubs;
using Acme.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;

namespace Acme.Product.Desktop.Tests;

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
