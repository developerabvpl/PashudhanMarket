using System.Security.Cryptography;

namespace UPBazaar.Modules.Orders.Domain;

/// <summary>
/// Order numbers such as <c>UPB-260921-7K3QX9</c>: the date it was placed, then six random
/// characters.
///
/// Random rather than sequential so a buyer cannot read the shop's order volume off their own
/// number, and from an alphabet without 0/O or 1/I/L, because these get read out over the phone to
/// support. Six characters give nearly 900 million numbers a day. Checkout draws again if the
/// number is already taken, and the unique index catches the two-at-once case that check cannot.
/// </summary>
public static class OrderNumber
{
    public const int MaxLength = 20;

    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public static string New(DateTime now) =>
        $"UPB-{now:yyMMdd}-{RandomNumberGenerator.GetString(Alphabet, 6)}";
}
