using System.Reflection;
using LoanApp.Application.Abstractions;
using LoanApp.Application.Applications;
using LoanApp.Application.Diagnostics;
using LoanApp.Domain.Decisions;
using Microsoft.Extensions.DependencyInjection;

namespace LoanApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Open/closed: every IDecisionRule in the domain and every IIntegrationEventHandler<> in this assembly
        // is discovered. Adding a rule or an event handler is just adding a class.
        foreach (var rule in ConcreteTypes(typeof(IDecisionRule).Assembly).Where(t => t.IsAssignableTo(typeof(IDecisionRule))))
            services.AddScoped(typeof(IDecisionRule), rule);
        services.AddScoped<DecisionEngine>();

        foreach (var handler in ConcreteTypes(Assembly.GetExecutingAssembly()))
        foreach (var contract in IntegrationEventHandlerContracts(handler))
            services.AddScoped(contract, handler);

        services.AddScoped<SubmitApplicationHandler>();
        services.AddSingleton<ApplicationMetrics>();
        return services;
    }

    /// <summary>The closed IIntegrationEventHandler&lt;TEvent&gt; interfaces a type implements.</summary>
    public static IEnumerable<Type> IntegrationEventHandlerContracts(Type type) =>
        type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>));

    public static IEnumerable<Type> ConcreteTypes(Assembly assembly) =>
        assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });
}
