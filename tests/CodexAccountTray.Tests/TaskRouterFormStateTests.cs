using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class TaskRouterFormStateTests
{
    [Fact]
    public void MissingProjectKeepsStartActionsDisabled()
    {
        TaskRouterControlState state = TaskRouterFormState.Calculate(
            projectReady: false, taskReady: true, policyReady: true, imagesReady: true,
            busy: false, cancellationRequested: false);

        Assert.False(state.CanStart);
        Assert.True(state.InputsEnabled);
        Assert.Contains("Projektordner", state.StatusText);
    }

    [Fact]
    public void CompleteInputEnablesBothStartPaths()
    {
        TaskRouterControlState state = TaskRouterFormState.Calculate(
            projectReady: true, taskReady: true, policyReady: true, imagesReady: true,
            busy: false, cancellationRequested: false);

        Assert.True(state.CanStart);
        Assert.False(state.CancelEnabled);
        Assert.True(state.CloseEnabled);
    }

    [Fact]
    public void RunningTaskLocksInputsAndEnablesCancel()
    {
        TaskRouterControlState state = TaskRouterFormState.Calculate(
            projectReady: true, taskReady: true, policyReady: true, imagesReady: true,
            busy: true, cancellationRequested: false);

        Assert.False(state.CanStart);
        Assert.False(state.InputsEnabled);
        Assert.True(state.CancelEnabled);
        Assert.False(state.CloseEnabled);
    }

    [Fact]
    public void RepeatedCancellationIsPrevented()
    {
        TaskRouterControlState state = TaskRouterFormState.Calculate(
            projectReady: true, taskReady: true, policyReady: true, imagesReady: true,
            busy: true, cancellationRequested: true);

        Assert.False(state.CancelEnabled);
        Assert.Contains("Abbruch", state.CancelText);
    }

    [Theory]
    [InlineData(false, true, "Regeldatei")]
    [InlineData(true, false, "Referenzbild")]
    public void InvalidOptionalInputKeepsStartActionsDisabled(
        bool policyReady, bool imagesReady, string expectedStatus)
    {
        TaskRouterControlState state = TaskRouterFormState.Calculate(
            projectReady: true, taskReady: true, policyReady: policyReady, imagesReady: imagesReady,
            busy: false, cancellationRequested: false);

        Assert.False(state.CanStart);
        Assert.Contains(expectedStatus, state.StatusText);
    }
}
