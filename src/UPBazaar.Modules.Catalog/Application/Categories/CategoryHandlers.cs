using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Application.Categories;

/// <summary>Loads categories with the parent the DTO needs.</summary>
internal static class CategoryLoader
{
    public static Task<Category?> FindAsync(
        UPBazaarDbContext dbContext,
        Guid categoryId,
        CancellationToken cancellationToken) =>
        dbContext.Set<Category>()
            .Include(c => c.Parent)
            .FirstOrDefaultAsync(c => c.PublicId == categoryId, cancellationToken);
}

/// <summary>Every category, for the storefront's rail and the admin's pickers.</summary>
public sealed record ListCategoriesQuery : IQuery<IReadOnlyList<CategoryDto>>;

internal sealed class ListCategoriesQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<Result<IReadOnlyList<CategoryDto>>> HandleAsync(
        ListCategoriesQuery query,
        CancellationToken cancellationToken)
    {
        var categories = await dbContext.Set<Category>()
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.PublicId, c.Name, c.Slug, c.Parent != null ? c.Parent.PublicId : null))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<CategoryDto>>(categories);
    }
}

/// <summary>Creates a category.</summary>
public sealed record CreateCategoryCommand(string Name, Guid? ParentId) : ICommand<CategoryDto>;

internal sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
}

internal sealed class CreateCategoryCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<CreateCategoryCommand, CategoryDto>
{
    public async Task<Result<CategoryDto>> HandleAsync(
        CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        Category? parent = null;

        if (command.ParentId is { } parentId)
        {
            parent = await CategoryLoader.FindAsync(dbContext, parentId, cancellationToken);

            if (parent is null)
            {
                return Result.Failure<CategoryDto>(CatalogErrors.CategoryNotFound);
            }
        }

        var category = Category.Create(command.Name.Trim(), parent);

        if (await dbContext.Set<Category>().AnyAsync(c => c.Slug == category.Slug, cancellationToken))
        {
            return Result.Failure<CategoryDto>(CatalogErrors.CategorySlugTaken);
        }

        dbContext.Set<Category>().Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return category.ToDto();
    }
}

/// <summary>Renames a category or moves it under another.</summary>
public sealed record UpdateCategoryCommand(Guid CategoryId, string Name, Guid? ParentId) : ICommand<CategoryDto>;

internal sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
    }
}

internal sealed class UpdateCategoryCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<UpdateCategoryCommand, CategoryDto>
{
    public async Task<Result<CategoryDto>> HandleAsync(
        UpdateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        var category = await CategoryLoader.FindAsync(dbContext, command.CategoryId, cancellationToken);

        if (category is null)
        {
            return Result.Failure<CategoryDto>(CatalogErrors.CategoryNotFound);
        }

        Category? parent = null;

        if (command.ParentId is { } parentId)
        {
            parent = await CategoryLoader.FindAsync(dbContext, parentId, cancellationToken);

            if (parent is null)
            {
                return Result.Failure<CategoryDto>(CatalogErrors.CategoryNotFound);
            }

            if (await IsSelfOrDescendantAsync(parent, category, cancellationToken))
            {
                return Result.Failure<CategoryDto>(CatalogErrors.CategoryCycle);
            }
        }

        category.Rename(command.Name.Trim());
        category.MoveUnder(parent);

        if (await dbContext.Set<Category>()
                .AnyAsync(c => c.Slug == category.Slug && c.Id != category.Id, cancellationToken))
        {
            return Result.Failure<CategoryDto>(CatalogErrors.CategorySlugTaken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return category.ToDto();
    }

    /// <summary>Walks up from the proposed parent; meeting the category itself means a loop.</summary>
    private async Task<bool> IsSelfOrDescendantAsync(
        Category proposedParent,
        Category category,
        CancellationToken cancellationToken)
    {
        long? currentId = proposedParent.Id;

        // Bounded so corrupt data cannot spin forever; no real tree is this deep.
        for (var depth = 0; currentId is not null && depth < 32; depth++)
        {
            if (currentId == category.Id)
            {
                return true;
            }

            var id = currentId.Value;

            currentId = await dbContext.Set<Category>()
                .Where(c => c.Id == id)
                .Select(c => c.ParentId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return false;
    }
}

/// <summary>Removes a category nothing is filed under.</summary>
public sealed record DeleteCategoryCommand(Guid CategoryId) : ICommand;

/// <summary>
/// Deletes only a category with no products and no sub-categories. Archived products count: they
/// are kept because orders point at them, and each still needs a shelf. A category in that state
/// can still be renamed or moved.
/// </summary>
internal sealed class DeleteCategoryCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<DeleteCategoryCommand>
{
    public async Task<Result> HandleAsync(DeleteCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await dbContext.Set<Category>()
            .FirstOrDefaultAsync(c => c.PublicId == command.CategoryId, cancellationToken);

        if (category is null)
        {
            return Result.Failure(CatalogErrors.CategoryNotFound);
        }

        var inUse = await dbContext.Set<Product>().AnyAsync(p => p.CategoryId == category.Id, cancellationToken)
            || await dbContext.Set<Category>().AnyAsync(c => c.ParentId == category.Id, cancellationToken);

        if (inUse)
        {
            return Result.Failure(CatalogErrors.CategoryInUse);
        }

        dbContext.Set<Category>().Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
