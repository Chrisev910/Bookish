using FantasyBooks.Models;

namespace FantasyBooks.Services;

public static class ProductOptionsFormat
{
    public const int MaxGroups = 5;
    public const int MaxChoicesPerGroup = 20;

    public static string Signature(IEnumerable<CartOptionSelection>? selections)
    {
        if (selections is null)
            return "";

        var parts = selections
            .Where(s => !string.IsNullOrWhiteSpace(s.GroupName) && !string.IsNullOrWhiteSpace(s.ChoiceLabel))
            .Select(s => $"{s.GroupName.Trim()}={s.ChoiceLabel.Trim()}")
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join('|', parts);
    }

    public static string Summary(IEnumerable<CartOptionSelection>? selections)
    {
        if (selections is null)
            return "";

        var parts = selections
            .Where(s => !string.IsNullOrWhiteSpace(s.GroupName) && !string.IsNullOrWhiteSpace(s.ChoiceLabel))
            .Select(s => $"{s.GroupName.Trim()}: {s.ChoiceLabel.Trim()}")
            .ToList();

        return string.Join(", ", parts);
    }

    public static List<CartOptionSelection> Normalize(IEnumerable<CartOptionSelection>? selections)
    {
        if (selections is null)
            return [];

        return selections
            .Where(s => !string.IsNullOrWhiteSpace(s.GroupName) && !string.IsNullOrWhiteSpace(s.ChoiceLabel))
            .Select(s => new CartOptionSelection
            {
                GroupName = s.GroupName.Trim(),
                ChoiceLabel = s.ChoiceLabel.Trim(),
            })
            .OrderBy(s => s.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
