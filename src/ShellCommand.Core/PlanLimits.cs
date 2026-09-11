namespace ShellCommand.Core;

public static class PlanLimits
{
    public const int MaxCharacters = 128 * 1024;
    public static int Characters(LaunchAction action) =>
        (action.Exe?.Length ?? 0) + (action.Cwd?.Length ?? 0) + (action.Text?.Length ?? 0) +
        (action.Args?.Sum(a => a.Length) ?? 0) + (action.Env?.Sum(p => p.Key.Length + p.Value.Length) ?? 0);
    public static int Characters(LaunchPlan plan) => plan.Title.Length + plan.SourcePath.Length + plan.Actions.Sum(Characters);
}
