using FluentValidation;
using UPBazaar.Modules.Sellers.Domain;

namespace UPBazaar.Modules.Sellers.Application;

/// <summary>
/// What a complete application looks like. Format checks only: a well-formed GSTIN can still be
/// unregistered, which is what the human review is for.
/// </summary>
internal sealed class SellerApplicationValidator : AbstractValidator<SellerApplication>
{
    public const string GstinPattern = "^[0-3][0-9][A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$";
    public const string PanPattern = "^[A-Z]{5}[0-9]{4}[A-Z]$";
    public const string IfscPattern = "^[A-Z]{4}0[A-Z0-9]{6}$";

    public SellerApplicationValidator()
    {
        RuleFor(x => x.ShopName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(2000);
        SellerProfileRules.Contact(this, x => x.ContactMobile, x => x.ContactEmail);

        RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AddressLine2).MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Pincode).NotEmpty().Matches("^[1-9][0-9]{5}$").WithMessage("Enter a 6-digit PIN code.");

        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Pan)
            .NotEmpty()
            .Must(p => System.Text.RegularExpressions.Regex.IsMatch(Upper(p), PanPattern))
            .WithMessage("Enter a valid PAN, for example ABCDE1234F.");

        RuleFor(x => x.Gstin)
            .Must(g => System.Text.RegularExpressions.Regex.IsMatch(Upper(g), GstinPattern))
            .When(x => !string.IsNullOrWhiteSpace(x.Gstin))
            .WithMessage("Enter a valid 15-character GSTIN.");

        // A GSTIN has the holder's PAN inside it, at characters 3 to 12; a mismatch is nearly
        // always a typo in one of the two, and a reviewer should not have to spot it by eye.
        RuleFor(x => x.Gstin)
            .Must((a, g) => Upper(g).Length != 15 || Upper(g)[2..12] == Upper(a.Pan))
            .When(x => !string.IsNullOrWhiteSpace(x.Gstin))
            .WithMessage("The PAN does not match the one inside the GSTIN.");

        RuleFor(x => x.BankAccountHolder).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BankAccountNumber)
            .NotEmpty()
            .Matches("^[0-9]{9,18}$")
            .WithMessage("Enter the account number: 9 to 18 digits.");
        RuleFor(x => x.Ifsc)
            .NotEmpty()
            .Must(i => System.Text.RegularExpressions.Regex.IsMatch(Upper(i), IfscPattern))
            .WithMessage("Enter a valid IFSC code, for example SBIN0001234.");
    }

    private static string Upper(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
}

/// <summary>Contact rules shared by the application and the profile edit.</summary>
internal static class SellerProfileRules
{
    public static void Contact<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, string>> mobile,
        System.Linq.Expressions.Expression<Func<T, string?>> email)
    {
        var readEmail = email.Compile();

        validator.RuleFor(mobile).NotEmpty().Matches("^[6-9][0-9]{9}$").WithMessage("Enter a 10-digit mobile number.");
        validator.RuleFor(email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(readEmail(x)));
    }
}
