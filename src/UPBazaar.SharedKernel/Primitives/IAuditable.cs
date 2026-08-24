namespace UPBazaar.SharedKernel.Primitives;

/// <summary>
/// Marks an entity whose changes are stamped and written to the audit log by the
/// persistence interceptor. Implement this on anything an admin or seller can change.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAtUtc { get; set; }

    string? CreatedBy { get; set; }

    DateTime? ModifiedAtUtc { get; set; }

    string? ModifiedBy { get; set; }
}
