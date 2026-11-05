using DistrictSync.Domain.Common;
using NetArchTest.Rules;

namespace DistrictSync.ArchitectureTests;

/// <summary>
/// Executable architecture: the dependency direction Domain ← Application ← Infrastructure ← Hosts is
/// enforced by tests, not by convention.
/// </summary>
public class LayerDependencyTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(AggregateRoot).Assembly;
    private static readonly System.Reflection.Assembly Application = typeof(DistrictSync.Application.DependencyInjection).Assembly;
    private static readonly System.Reflection.Assembly Infrastructure = typeof(DistrictSync.Infrastructure.DependencyInjection).Assembly;
    private static readonly System.Reflection.Assembly Contracts = typeof(DistrictSync.Contracts.Events.EventCatalog).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_but_the_BCL()
    {
        var result = Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny("DistrictSync.Application", "DistrictSync.Infrastructure", "DistrictSync.Contracts", "DistrictSync.Api", "DistrictSync.Worker", "Microsoft.EntityFrameworkCore", "RabbitMQ", "Microsoft.Extensions")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Contracts_are_free_of_domain_and_framework_dependencies()
    {
        var result = Types.InAssembly(Contracts).ShouldNot()
            .HaveDependencyOnAny("DistrictSync.Domain", "DistrictSync.Application", "DistrictSync.Infrastructure", "Microsoft.EntityFrameworkCore", "RabbitMQ")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_hosts()
    {
        var result = Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("DistrictSync.Infrastructure", "DistrictSync.Api", "DistrictSync.Worker", "Microsoft.EntityFrameworkCore.SqlServer", "RabbitMQ", "StackExchange.Redis", "CsvHelper", "Microsoft.AspNetCore")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_hosts()
    {
        var result = Types.InAssembly(Infrastructure).ShouldNot()
            .HaveDependencyOnAny("DistrictSync.Api", "DistrictSync.Worker", "Microsoft.AspNetCore.Mvc", "Microsoft.AspNetCore.SignalR")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Domain_events_are_immutable_records()
    {
        var result = Types.InAssembly(Domain).That().ImplementInterface(typeof(IDomainEvent)).Should().BeSealed().GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Integration_events_are_named_consistently_and_registered()
    {
        var eventTypes = Contracts.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(DistrictSync.Contracts.Events.IIntegrationEvent).IsAssignableFrom(t))
            .ToList();

        eventTypes.ShouldNotBeEmpty();
        foreach (var type in eventTypes)
        {
            type.Name.ShouldEndWith("Event", customMessage: type.FullName);
            Should.NotThrow(() => DistrictSync.Contracts.Events.EventCatalog.NameOf(type), $"{type.Name} is missing from EventCatalog");
        }
    }

    [Fact]
    public void Application_services_live_in_feature_namespaces_not_in_the_root()
    {
        var offenders = Application.GetTypes()
            .Where(t => t.IsPublic && t.Namespace == "DistrictSync.Application" && t.Name != "DependencyInjection")
            .Select(t => t.FullName)
            .ToList();
        offenders.ShouldBeEmpty();
    }

    private static string Describe(NetArchTest.Rules.TestResult result) => result.IsSuccessful ? string.Empty : "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
