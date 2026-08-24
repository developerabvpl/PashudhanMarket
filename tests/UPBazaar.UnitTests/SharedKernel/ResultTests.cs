using UPBazaar.SharedKernel.Results;

namespace UPBazaar.UnitTests.SharedKernel;

public sealed class ResultTests
{
    [Fact]
    public void A_failed_result_cannot_have_its_value_read()
    {
        var result = Result.Failure<string>(Error.NotFound("x.not_found", "Missing."));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void A_successful_result_carries_no_error()
    {
        var result = Result.Success("ok");

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
        result.Value.ShouldBe("ok");
    }

    [Fact]
    public void Validation_failures_travel_with_the_result()
    {
        var errors = new Dictionary<string, string[]> { ["Price"] = ["Price must be greater than zero."] };

        var result = Result.ValidationFailure<string>(errors);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.ValidationErrors["Price"].ShouldHaveSingleItem();
    }

    [Fact]
    public void Paging_counters_are_derived_from_the_total()
    {
        var page = new PagedList<int>([1, 2, 3], Page: 1, PageSize: 3, TotalCount: 7);

        page.TotalPages.ShouldBe(3);
        page.HasNextPage.ShouldBeTrue();
    }
}
