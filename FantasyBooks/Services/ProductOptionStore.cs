using FantasyBooks.Data;
using FantasyBooks.Models;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Services;

public static class ProductOptionStore
{
    public sealed class GroupInput
    {
        public string? Name { get; set; }

        public List<string>? Choices { get; set; }
    }

    /// <summary>Parse posted admin groups; returns null and adds model errors via <paramref name="addError"/> on failure.</summary>
    public static List<(string Name, List<string> Choices)>? ParsePosted(
        IEnumerable<GroupInput>? posted,
        Action<string, string> addError)
    {
        var result = new List<(string Name, List<string> Choices)>();
        if (posted is null)
            return result;

        var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in posted)
        {
            var name = group.Name?.Trim() ?? "";
            var choices = (group.Choices ?? [])
                .Select(c => c?.Trim() ?? "")
                .Where(c => c.Length > 0)
                .ToList();

            // Skip completely empty rows from the dynamic form.
            if (name.Length == 0 && choices.Count == 0)
                continue;

            if (name.Length == 0)
            {
                addError(nameof(GroupInput.Name), "Each option group needs a name (e.g. Colour).");
                return null;
            }

            if (choices.Count == 0)
            {
                addError(nameof(GroupInput.Choices), $"Add at least one choice for “{name}”, or remove the group.");
                return null;
            }

            if (!groupNames.Add(name))
            {
                addError(nameof(GroupInput.Name), $"Duplicate option group “{name}”.");
                return null;
            }

            if (result.Count >= ProductOptionsFormat.MaxGroups)
            {
                addError(nameof(GroupInput.Name), $"At most {ProductOptionsFormat.MaxGroups} option groups are allowed.");
                return null;
            }

            var uniqueChoices = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var choice in choices)
            {
                if (!seen.Add(choice))
                    continue;
                uniqueChoices.Add(choice);
                if (uniqueChoices.Count > ProductOptionsFormat.MaxChoicesPerGroup)
                {
                    addError(nameof(GroupInput.Choices),
                        $"At most {ProductOptionsFormat.MaxChoicesPerGroup} choices per group (“{name}”).");
                    return null;
                }
            }

            if (name.Length > 80)
            {
                addError(nameof(GroupInput.Name), "Option group names must be 80 characters or fewer.");
                return null;
            }

            if (uniqueChoices.Any(c => c.Length > 80))
            {
                addError(nameof(GroupInput.Choices), "Choice labels must be 80 characters or fewer.");
                return null;
            }

            result.Add((name, uniqueChoices));
        }

        return result;
    }

    public static async Task ReplaceAsync(
        LibraryContext db,
        int productId,
        IReadOnlyList<(string Name, List<string> Choices)> groups,
        CancellationToken cancellationToken = default)
    {
        var existingGroups = await db.ProductOptionGroups
            .Where(g => g.ProductId == productId)
            .Include(g => g.Choices)
            .ToListAsync(cancellationToken);

        if (existingGroups.Count > 0)
        {
            db.ProductOptionChoices.RemoveRange(existingGroups.SelectMany(g => g.Choices));
            db.ProductOptionGroups.RemoveRange(existingGroups);
            await db.SaveChangesAsync(cancellationToken);
        }

        for (var gi = 0; gi < groups.Count; gi++)
        {
            var (name, choices) = groups[gi];
            var group = new ProductOptionGroup
            {
                ProductId = productId,
                Name = name,
                SortOrder = gi,
            };
            db.ProductOptionGroups.Add(group);
            await db.SaveChangesAsync(cancellationToken);

            for (var ci = 0; ci < choices.Count; ci++)
            {
                db.ProductOptionChoices.Add(new ProductOptionChoice
                {
                    GroupId = group.Id,
                    Label = choices[ci],
                    SortOrder = ci,
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public static async Task<List<ProductOptionGroup>> LoadForProductAsync(
        LibraryContext db,
        int productId,
        CancellationToken cancellationToken = default)
    {
        var groups = await db.ProductOptionGroups.AsNoTracking()
            .Where(g => g.ProductId == productId)
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Id)
            .Include(g => g.Choices)
            .ToListAsync(cancellationToken);

        foreach (var group in groups)
        {
            group.Choices = group.Choices
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Id)
                .ToList();
        }

        return groups;
    }

    /// <summary>
    /// Validates posted form values (groupId → choiceId) against the product's current options.
    /// Returns normalized selections, or null if invalid.
    /// </summary>
    public static List<CartOptionSelection>? ResolveSelections(
        IReadOnlyList<ProductOptionGroup> groups,
        IReadOnlyDictionary<int, int> choiceByGroupId,
        out string? error)
    {
        error = null;
        if (groups.Count == 0)
            return [];

        var result = new List<CartOptionSelection>();
        foreach (var group in groups)
        {
            if (!choiceByGroupId.TryGetValue(group.Id, out var choiceId))
            {
                error = $"Please choose a {group.Name}.";
                return null;
            }

            var choice = group.Choices.FirstOrDefault(c => c.Id == choiceId);
            if (choice is null)
            {
                error = $"Please choose a valid {group.Name}.";
                return null;
            }

            result.Add(new CartOptionSelection
            {
                GroupName = group.Name,
                ChoiceLabel = choice.Label,
            });
        }

        return ProductOptionsFormat.Normalize(result);
    }

    /// <summary>True when cart selections still match the product's current option catalogue.</summary>
    public static bool SelectionsStillValid(
        IReadOnlyList<ProductOptionGroup> groups,
        IReadOnlyList<CartOptionSelection> selections)
    {
        if (groups.Count == 0)
            return selections.Count == 0;

        if (selections.Count != groups.Count)
            return false;

        foreach (var group in groups)
        {
            var pick = selections.FirstOrDefault(s =>
                string.Equals(s.GroupName, group.Name, StringComparison.OrdinalIgnoreCase));
            if (pick is null)
                return false;

            var ok = group.Choices.Any(c =>
                string.Equals(c.Label, pick.ChoiceLabel, StringComparison.OrdinalIgnoreCase));
            if (!ok)
                return false;
        }

        return true;
    }
}
