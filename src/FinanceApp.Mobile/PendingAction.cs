namespace FinanceApp.Mobile;

/// <summary>
/// What a shortcut or the widget asked for («новый расход»), kept until the app can do it: the tabs are shown
/// and the lock is open. The app takes it then; a newer request replaces an older one.
/// </summary>
public static class PendingAction
{
    public const string Expense = "expense";
    public const string Income = "income";
    public const string Transfer = "transfer";

    private static string? _kind;

    /// <summary>A request arrived (possibly while the app was already open).</summary>
    public static event EventHandler? Requested;

    public static void Request(string? kind)
    {
        if (kind is Expense or Income or Transfer)
        {
            _kind = kind;
            Requested?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>The waiting request, removed.</summary>
    public static string? Take()
    {
        var kind = _kind;
        _kind = null;
        return kind;
    }
}
