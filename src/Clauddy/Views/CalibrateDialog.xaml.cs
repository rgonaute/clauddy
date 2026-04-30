using Clauddy.Services;
using Clauddy.ViewModels;

namespace Clauddy.Views;

/// <summary>
/// Calibrates Clauddy's display against Claude Code's /usage by back-solving
/// per-window token caps from current local token counts and a user-supplied %.
/// </summary>
public partial class CalibrateDialog : System.Windows.Window
{
    private readonly UsageStats _snapshot;

    public long Quota5hTokens { get; private set; }
    public long Quota7dTokens { get; private set; }

    public CalibrateDialog(UsageStats snapshot, long currentQuota5h, long currentQuota7d)
    {
        InitializeComponent();
        _snapshot = snapshot;

        // Pre-fill with what Clauddy currently displays so users can compare against
        // /usage and tweak only the numbers that drift.
        if (currentQuota5h > 0)
            Pct5h.Text = Math.Round((double)snapshot.Last5HoursTokens / currentQuota5h * 100).ToString();
        if (currentQuota7d > 0)
            Pct7d.Text = Math.Round((double)snapshot.Last7DaysTokens / currentQuota7d * 100).ToString();

        CurrentSnapshot.Text =
            $"Snapshot: 5h = {MainViewModel.FormatTokens(snapshot.Last5HoursTokens)} tokens, " +
            $"7d = {MainViewModel.FormatTokens(snapshot.Last7DaysTokens)} tokens.";
    }

    private void SaveBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Quota5hTokens = BackSolve(Pct5h.Text, _snapshot.Last5HoursTokens);
        Quota7dTokens = BackSolve(Pct7d.Text, _snapshot.Last7DaysTokens);
        DialogResult = true; Close();
    }

    private void CancelBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false; Close();
    }

    private void Pct_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    /// <summary>
    /// Back-solve a cap from a percentage. Empty/invalid/non-positive → 0 (clears calibration).
    /// </summary>
    public static long BackSolve(string? pctText, long currentTokens)
    {
        if (!double.TryParse((pctText ?? "").Trim(), out var pct)) return 0;
        if (pct <= 0 || currentTokens <= 0) return 0;
        // Clamp to a sane range so a stray "0.1%" doesn't produce an absurd cap.
        pct = Math.Clamp(pct, 1, 100);
        return (long)Math.Round(currentTokens / (pct / 100.0));
    }

}
