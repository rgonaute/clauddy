using System.Windows.Controls;
using Clauddy.Models;
using Clauddy.Services;
using Clauddy.ViewModels;

namespace Clauddy.Views;

/// <summary>
/// Calibrates Clauddy's display against Claude Code's /usage. Two paths feed the
/// resulting cap pair: pick a Plan (uses PlanDefaults), or paste /usage percentages
/// (back-solves caps from current local token counts). Pct entry overrides the plan
/// default per-window, so users can mix-and-match (e.g. trust the plan's 5h default
/// but pin the weekly to whatever /usage shows).
/// </summary>
public partial class CalibrateDialog : System.Windows.Window
{
    private readonly UsageStats _snapshot;

    public Plan SelectedPlan { get; private set; }
    public long Quota5hTokens { get; private set; }
    public long Quota7dTokens { get; private set; }

    public CalibrateDialog(UsageStats snapshot, Plan currentPlan, long currentQuota5h, long currentQuota7d)
    {
        InitializeComponent();
        _snapshot = snapshot;

        PlanCombo.ItemsSource = Enum.GetValues<Plan>()
            .Select(p => new { Display = PlanDefaults.DisplayName(p), Value = p })
            .ToList();
        PlanCombo.SelectedValue = currentPlan;
        UpdatePlanCapsLabel(currentPlan);

        // Pre-fill pct fields with what Clauddy currently displays so users can compare
        // against /usage at a glance and only edit the box that drifted.
        if (currentQuota5h > 0)
            Pct5h.Text = Math.Round((double)snapshot.CurrentBucketTokens / currentQuota5h * 100).ToString();
        if (currentQuota7d > 0)
            Pct7d.Text = Math.Round((double)snapshot.Last7DaysTokens / currentQuota7d * 100).ToString();

        CurrentSnapshot.Text =
            $"Snapshot: 5h = {MainViewModel.FormatTokens(snapshot.CurrentBucketTokens)} tokens, " +
            $"7d = {MainViewModel.FormatTokens(snapshot.Last7DaysTokens)} tokens.";
    }

    private void PlanCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlanCombo.SelectedValue is Plan p) UpdatePlanCapsLabel(p);
    }

    private void UpdatePlanCapsLabel(Plan plan)
    {
        if (PlanCapsLabel == null) return;
        var caps = PlanDefaults.For(plan);
        if (caps.Tokens5h == 0 && caps.Tokens7d == 0)
        {
            PlanCapsLabel.Text = "No plan-derived defaults — enter percentages from /usage below.";
        }
        else
        {
            PlanCapsLabel.Text =
                $"Plan defaults — 5h: {MainViewModel.FormatTokens(caps.Tokens5h)} tokens, " +
                $"7d: {MainViewModel.FormatTokens(caps.Tokens7d)} tokens.";
        }
    }

    private void SaveBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SelectedPlan = (Plan)(PlanCombo.SelectedValue ?? Plan.Custom);
        var planCaps = PlanDefaults.For(SelectedPlan);
        // Per-window: pct entry wins when present, plan default otherwise. 0 means
        // "no calibration"; the widget falls back to raw token counts for that window.
        Quota5hTokens = ResolveCap(Pct5h.Text, _snapshot.CurrentBucketTokens, planCaps.Tokens5h);
        Quota7dTokens = ResolveCap(Pct7d.Text, _snapshot.Last7DaysTokens, planCaps.Tokens7d);
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
    /// Cap resolution: pct entry takes priority (back-solved from current tokens);
    /// otherwise fall back to the plan default. Returns 0 only when both inputs are absent.
    /// </summary>
    public static long ResolveCap(string? pctText, long currentTokens, long planDefault)
    {
        var backSolved = BackSolve(pctText, currentTokens);
        return backSolved > 0 ? backSolved : planDefault;
    }

    /// <summary>
    /// Back-solve a cap from a percentage. Empty/invalid/non-positive → 0 (no override).
    /// </summary>
    public static long BackSolve(string? pctText, long currentTokens)
    {
        if (!double.TryParse((pctText ?? "").Trim(), out var pct)) return 0;
        if (pct <= 0 || currentTokens <= 0) return 0;
        pct = Math.Clamp(pct, 1, 100);
        return (long)Math.Round(currentTokens / (pct / 100.0));
    }
}
