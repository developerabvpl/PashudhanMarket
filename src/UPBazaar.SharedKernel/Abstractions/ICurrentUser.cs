namespace UPBazaar.SharedKernel.Abstractions;

/// <summary>The authenticated principal for the current request, if any.</summary>
public interface ICurrentUser
{
    string? UserId { get; }

    string? UserName { get; }

    bool IsAuthenticated { get; }

    bool HasPermission(string permission);
}
