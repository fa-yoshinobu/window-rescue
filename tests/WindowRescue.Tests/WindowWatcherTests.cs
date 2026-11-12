using WindowRescue.Core;

namespace WindowRescue.Tests;

public class WindowWatcherTests
{
    [Fact]
    public void NewCloakedWindowWaitsUntilEligibleAndOnlyUncloaksOnce()
    {
        var appearances = new WindowAppearances([]);
        var hwnd = new IntPtr(1);
        Assert.False(appearances.Show(hwnd, true));
        Assert.False(appearances.Uncloak(hwnd, false));
        Assert.True(appearances.Uncloak(hwnd, true));
        Assert.False(appearances.Uncloak(hwnd, true));
    }

    [Fact]
    public void ExistingWindowsDoNotBecomeNewOnDesktopSwitch()
    {
        var hwnd = new IntPtr(1);
        var appearances = new WindowAppearances([hwnd]);
        Assert.False(appearances.Show(hwnd, true));
        Assert.False(appearances.Uncloak(hwnd, true));
        Assert.True(appearances.Show(hwnd, false)); // 通常の非表示→再表示は従来どおり。
        Assert.False(appearances.Uncloak(hwnd, true));
    }

    [Fact]
    public void VisibleShowConsumesPendingUncloakAndSubsequentCloakDoesNotRearmIt()
    {
        var appearances = new WindowAppearances([]);
        var hwnd = new IntPtr(1);
        Assert.False(appearances.Show(hwnd, true));
        Assert.True(appearances.Show(hwnd, false));
        Assert.False(appearances.Uncloak(hwnd, true));
        Assert.False(appearances.Show(hwnd, true));
        Assert.False(appearances.Uncloak(hwnd, true));
    }

    [Fact]
    public void DestroyedHandleCanBeReusedWithoutRetainingOldPendingState()
    {
        var hwnd = new IntPtr(1);
        var appearances = new WindowAppearances([hwnd]);
        appearances.Forget(hwnd);
        Assert.False(appearances.Show(hwnd, true));
        Assert.True(appearances.Uncloak(hwnd, true));
        appearances.Forget(hwnd);
        Assert.False(appearances.Uncloak(hwnd, true));
    }
}
