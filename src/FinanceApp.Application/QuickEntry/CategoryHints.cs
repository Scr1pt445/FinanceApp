using FinanceApp.Domain;

namespace FinanceApp.Application.QuickEntry;

/// <summary>
/// Remembers which category each note word was used with («кофе» → «Еда»), per transaction type.
/// The most frequent category wins; on a tie, the most recent one.
/// </summary>
public sealed class CategoryHints
{
    public const int MinWordLength = 3;

    public static readonly CategoryHints Empty = new(new Dictionary<(TransactionType, string), Guid>());

    private readonly IReadOnlyDictionary<(TransactionType Type, string Word), Guid> _byWord;

    private CategoryHints(IReadOnlyDictionary<(TransactionType, string), Guid> byWord) => _byWord = byWord;

    public static CategoryHints Build(IEnumerable<Transaction> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var stats = new Dictionary<(TransactionType, string), Dictionary<Guid, (int Count, DateTime Last)>>();
        foreach (var transaction in history)
        {
            foreach (var word in Words(transaction.Note))
            {
                var key = (transaction.Type, word);
                if (!stats.TryGetValue(key, out var perCategory))
                {
                    stats[key] = perCategory = [];
                }

                var (count, last) = perCategory.GetValueOrDefault(transaction.CategoryId);
                perCategory[transaction.CategoryId] = (count + 1, transaction.CreatedAt > last ? transaction.CreatedAt : last);
            }
        }

        var best = stats.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderByDescending(c => c.Value.Count).ThenByDescending(c => c.Value.Last).First().Key);
        return new CategoryHints(best);
    }

    /// <summary>The category suggested by the first note word that has a hint.</summary>
    public Guid? Suggest(TransactionType type, IEnumerable<string> noteWords)
    {
        foreach (var word in noteWords.SelectMany(Words))
        {
            if (_byWord.TryGetValue((type, word), out var categoryId))
            {
                return categoryId;
            }
        }

        return null;
    }

    internal static IEnumerable<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
                .Where(w => w.Length >= MinWordLength);
}
