using UPBazaar.SharedKernel.Results;

namespace UPBazaar.UnitTests.SharedKernel;

public sealed class ResultTests
{
    [Fact]
    public void A_successful_result_carries_no_error()
    {
        var result = Result.Success("ok");

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
        result.Value.ShouldBe("ok");
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_is_a_programming_error()
    {
        var result = Result.Failure<string>(Error.NotFound("x.not_found", "Missing."));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void A_failure_keeps_the_code_and_type_it_was_given()
    {
        var result = Result.Failure(Error.Conflict("orders.already_paid", "Already paid."));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("orders.already_paid");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Validation_failures_travel_with_the_result()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Price"] = ["Price must be greater than zero."],
        };

        var result = Result.ValidationFailure<string>(errors);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.ValidationErrors["Price"].ShouldHaveSingleItem();
    }

    [Fact]
    public void A_value_converts_implicitly_to_a_successful_result()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Paging_counters_are_derived_from_the_total()
    {
        var page = new PagedList<int>([1, 2, 3], Page: 1, PageSize: 3, TotalCount: 7);

        page.TotalPages.ShouldBe(3);
        page.HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public void The_last_page_reports_no_next_page()
    {
        var page = new PagedList<int>([7], Page: 3, PageSize: 3, TotalCount: 7);

        page.HasNextPage.ShouldBeFalse();
    }
}
