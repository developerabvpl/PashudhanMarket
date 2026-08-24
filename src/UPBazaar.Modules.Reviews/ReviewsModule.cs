using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Reviews;

/// <summary>
/// Product and seller ratings, review moderation and abuse reports.
/// </summary>
public sealed class ReviewsModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "reviews";

    /// <inheritdoc />
    public string Name => "Reviews";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(ReviewsModule).Assembly;
}

/// <summary>Registration entry point for the Reviews module.</summary>
public static class ReviewsModuleExtensions
{
    /// <summary>
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddReviewsModule(this IServiceCollection services) =>
        services.AddModule<ReviewsModule>();
}
