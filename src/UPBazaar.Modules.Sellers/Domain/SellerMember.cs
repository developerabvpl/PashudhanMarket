using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Sellers.Domain;

/// <summary>What a member of a shop's team may do. Stored by name.</summary>
public enum SellerMemberRole
{
    /// <summary>Runs the shop for its owner: products, stock, orders, dispatch, coupons, reviews.</summary>
    Manager = 0,

    /// <summary>Packs and ships orders, and handles returns. Nothing else.</summary>
    Dispatch = 1,
}

/// <summary>
/// Someone the owner added to their shop's team. The owner is not a member: they own the shop,
/// and only they manage the team, see its earnings and change its details.
///
/// An account belongs to one shop at most, whether as owner or as a member, so every request in
/// the seller portal knows which shop it is for without being told.
/// </summary>
public sealed class SellerMember : Entity
{
    private SellerMember()
    {
    }

    public long SellerId { get; private set; }

    /// <summary>Identity's id for the member's account.</summary>
    public Guid UserId { get; private set; }

    public SellerMemberRole Role { get; private set; }

    public DateTime AddedAtUtc { get; private set; }

    /// <summary>Who added them: the owner's user id.</summary>
    public string? AddedBy { get; private set; }

    public static SellerMember Add(long sellerId, Guid userId, SellerMemberRole role, string? addedBy, DateTime now) => new()
    {
        SellerId = sellerId,
        UserId = userId,
        Role = role,
        AddedAtUtc = now,
        AddedBy = addedBy,
    };

    public void ChangeRole(SellerMemberRole role) => Role = role;

    /// <summary>The Identity role that carries this member's permissions.</summary>
    public static string IdentityRole(SellerMemberRole role) => role switch
    {
        SellerMemberRole.Manager => "SellerManager",
        _ => "SellerDispatch",
    };
}
