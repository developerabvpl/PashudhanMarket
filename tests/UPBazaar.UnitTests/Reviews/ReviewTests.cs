using UPBazaar.Modules.Reviews.Contracts.Events;
using UPBazaar.Modules.Reviews.Domain;

namespace UPBazaar.UnitTests.Reviews;

public sealed class ReviewTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Stars_alone_leave_nothing_for_staff()
    {
        var review = Write(4, null, "   ");

        review.ContentStatus.ShouldBe(ContentStatus.None);
        review.Body.ShouldBeNull();
        review.ContentSubmittedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Words_wait_for_staff_and_changing_them_sends_them_back()
    {
        var review = Write(4, "Good", "Burns clean.");
        review.ContentStatus.ShouldBe(ContentStatus.Pending);
        review.ContentSubmittedAtUtc.ShouldBe(Now);

        review.Approve("staff", Now).IsSuccess.ShouldBeTrue();
        review.ContentStatus.ShouldBe(ContentStatus.Approved);

        // Same words, different stars: nothing new to read.
        review.Edit("Asha", 5, "Good", "Burns clean.", Now.AddHours(1));
        review.ContentStatus.ShouldBe(ContentStatus.Approved);
        review.Rating.ShouldBe(5);

        review.Edit("Asha", 5, "Good", "Burns clean, but cracked.", Now.AddHours(2));
        review.ContentStatus.ShouldBe(ContentStatus.Pending);
        review.ContentSubmittedAtUtc.ShouldBe(Now.AddHours(2));
        review.ModeratedBy.ShouldBeNull();
    }

    [Fact]
    public void Only_pending_words_can_be_decided()
    {
        var review = Write(3, null, "Fine.");

        review.Reject("No phone numbers.", "staff", Now).IsSuccess.ShouldBeTrue();
        review.ModerationNote.ShouldBe("No phone numbers.");

        review.Approve("staff", Now).Error.ShouldBe(ReviewErrors.NothingToModerate);

        // Clearing the words leaves stars only, and the note goes with them.
        review.Edit("Asha", 3, null, null, Now);
        review.ContentStatus.ShouldBe(ContentStatus.None);
        review.ModerationNote.ShouldBeNull();
    }

    [Fact]
    public void A_review_holds_at_most_three_photos_and_removing_one_keeps_its_approval()
    {
        var review = Write(5, null, null);

        for (var i = 0; i < Review.MaxPhotos; i++)
        {
            review.AddPhoto(Photo(), Now).IsSuccess.ShouldBeTrue();
        }

        review.ContentStatus.ShouldBe(ContentStatus.Pending);
        review.AddPhoto(Photo(), Now).Error.ShouldBe(ReviewErrors.TooManyPhotos);

        review.Approve("staff", Now);

        var first = review.Photos.First();
        review.RemovePhoto(first.PublicId).Value.ShouldBeSameAs(first);
        review.ContentStatus.ShouldBe(ContentStatus.Approved);

        review.RemovePhoto(Guid.NewGuid()).Error.ShouldBe(ReviewErrors.PhotoNotFound);
    }

    [Fact]
    public void Removing_the_last_photo_of_a_wordless_review_leaves_stars_only()
    {
        var review = Write(5, null, null);
        review.AddPhoto(Photo(), Now);

        review.RemovePhoto(review.Photos.Single().PublicId);

        review.ContentStatus.ShouldBe(ContentStatus.None);
        review.ContentSubmittedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void A_hidden_reply_stays_hidden_when_the_seller_rewrites_it()
    {
        var review = Write(2, null, null);

        review.HideReply().Error.ShouldBe(ReviewErrors.NoReply);

        review.Reply("  Sorry to hear that.  ", Now);
        review.ReplyText.ShouldBe("Sorry to hear that.");

        review.HideReply().IsSuccess.ShouldBeTrue();
        review.Reply("Please call us.", Now.AddHours(1));

        review.ReplyHidden.ShouldBeTrue();
        review.RepliedAtUtc.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void Writing_a_review_tells_the_seller_once_and_rejecting_it_tells_the_buyer()
    {
        var review = Write(2, null, "Smaller than shown.");

        var written = review.DomainEvents.OfType<ReviewWrittenDomainEvent>().ShouldHaveSingleItem();
        written.Rating.ShouldBe(2);
        written.HasContent.ShouldBeTrue();

        review.ClearDomainEvents();
        review.Edit("Asha", 1, null, "Much smaller than shown.", Now);
        review.DomainEvents.ShouldBeEmpty();

        review.Reject("Please describe the product, not the seller.", "staff", Now);
        var rejected = review.DomainEvents.OfType<ReviewRejectedDomainEvent>().ShouldHaveSingleItem();
        rejected.OrderId.ShouldBe(review.OrderId);
        rejected.Note.ShouldBe("Please describe the product, not the seller.");

        // A second decision finds nothing pending and raises nothing.
        review.ClearDomainEvents();
        review.Reject("Again.", "staff", Now);
        review.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Ratings_run_from_one_to_five(int rating) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Write(rating, null, null));

    private static Review Write(int rating, string? title, string? body) =>
        Review.Create(
            ReviewableLine.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Gobar Diya", Guid.NewGuid(), Guid.NewGuid(), Now),
            "Asha",
            rating,
            title,
            body,
            Now);

    private static ReviewPhoto Photo() => ReviewPhoto.Create($"{Guid.NewGuid():N}.png", "image/png", 68, Now);
}
