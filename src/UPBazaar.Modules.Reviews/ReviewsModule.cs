using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Reviews.Photos;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Reviews;

/// <summary>
/// Buyers' ratings and reviews of what was delivered to them, staff moderation, and sellers' replies.
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
    /// <summary>Registers the module's schema, handlers, photo links and photo store.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration, for <see cref="ReviewsModuleOptions"/>.</param>
    /// <param name="environment">Host environment: Development and Testing keep photos on local disk.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddReviewsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddModule<ReviewsModule>();

        services.AddOptions<ReviewsModuleOptions>()
            .Bind(configuration.GetSection(ReviewsModuleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<PhotoLinks>();

        // Resolved lazily so that registering the module - for migrations, say - creates no folder.
        services.AddSingleton<IPhotoStore>(provider =>
        {
            var folder = provider.GetRequiredService<IOptions<ReviewsModuleOptions>>().Value.PhotoFolder;

            if (!string.IsNullOrWhiteSpace(folder))
            {
                return new LocalDiskPhotoStore(folder);
            }

            return environment.IsDevelopment() || environment.IsEnvironment("Testing")
                ? new LocalDiskPhotoStore(Path.Combine(environment.ContentRootPath, "App_Data", "review-photos"))
                : new UnconfiguredPhotoStore();
        });

        return services;
    }
}
