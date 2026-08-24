namespace UPBazaar.Modules.Identity.Contracts.Dtos;

/// <summary>Kind of account. Determines which sign-in methods apply.</summary>
public enum UserTypeDto
{
    Buyer = 0,
    Seller = 1,
    Staff = 2,
}

/// <summary>A user as the API exposes it.</summary>
public sealed record UserDto(
    Guid Id,
    string UserType,
    string? Email,
    bool EmailVerified,
    string? Mobile,
    bool MobileVerified,
    string DisplayName,
    string PreferredLanguage,
    string Status,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    DateTime CreatedAtUtc);

/// <summary>A user in a list, without the permission expansion.</summary>
public sealed record UserSummaryDto(
    Guid Id,
    string UserType,
    string? Email,
    string? Mobile,
    string DisplayName,
    string Status,
    IReadOnlyList<string> Roles,
    DateTime CreatedAtUtc);

/// <summary>A role and what it grants.</summary>
public sealed record RoleDto(string Name, string? Description, IReadOnlyList<string> Permissions);
