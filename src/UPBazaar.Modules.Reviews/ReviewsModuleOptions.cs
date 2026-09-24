namespace UPBazaar.Modules.Reviews;

/// <summary>Where review photos are kept, bound from <c>Reviews</c>.</summary>
public sealed class ReviewsModuleOptions
{
    public const string SectionName = "Reviews";

    /// <summary>
    /// A folder to keep photos in. Setting it switches photos on in any environment - right for a
    /// single server with a lasting disk. Unset, Development and Testing use a folder under the
    /// app and everywhere else has photos switched off until a cloud store is added.
    /// </summary>
    public string? PhotoFolder { get; set; }
}
