namespace UPBazaar.SharedKernel.Primitives;

/// <summary>
/// Marks an entity whose changes are stamped and written to the audit log. Implementing this
/// is the entire opt-in: the audit interceptor does the rest.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAtUtc { get; set; }

    string? CreatedBy { get; set; }

    DateTime? ModifiedAtUtc { get; set; }

    string? ModifiedBy { get; set; }
}
