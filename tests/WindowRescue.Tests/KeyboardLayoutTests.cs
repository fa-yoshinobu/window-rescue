using WindowRescue.Core;

namespace WindowRescue.Tests;

public class KeyboardLayoutTests
{
    [Fact]
    public void DetectsAltGrAndShiftAltGrButIgnoresUnmappedCharacters()
    {
        Assert.False(KeyboardLayout.DetectAltGr(_ => -1));
        Assert.False(KeyboardLayout.DetectAltGr(c => c == '@' ? (short)0x0132 : (short)-1));
        Assert.True(KeyboardLayout.DetectAltGr(c => c == '@' ? (short)0x0632 : (short)-1));
        Assert.True(KeyboardLayout.DetectAltGr(c => c == '€' ? (short)0x0735 : (short)-1));
    }

    [Fact]
    public void AltGrDefaultsAvoidCharacterKeysButKeepExplicitChoices()
    {
        foreach (var action in HotkeyActions.All([1, 2, 3]))
        {
            if (action == HotkeyActions.RescueHere) continue;
            Assert.Equal("", HotkeyActions.Default(action, true));
        }
        Assert.Equal("Ctrl+Alt+Home", HotkeyActions.Default(HotkeyActions.RescueHere, true));
        Assert.Equal("Ctrl+Alt+1", HotkeyActions.Default("RescueForeground:1", false));
        var configured = new Dictionary<string, string> { ["RescueForeground:1"] = "Ctrl+Alt+1", ["RescueAll:1"] = "" };
        Assert.Equal("Ctrl+Alt+1", HotkeyActions.Resolve("RescueForeground:1", configured, true));
        Assert.Equal("", HotkeyActions.Resolve("RescueAll:1", configured, false));
    }

    [Theory]
    [InlineData("Ctrl+Alt+A", true)]
    [InlineData("Ctrl+Alt+Shift+2", true)]
    [InlineData("Ctrl+Alt+OemPlus", true)]
    [InlineData("Ctrl+Alt+Home", false)]
    [InlineData("Ctrl+Alt+F1", false)]
    [InlineData("Ctrl+Shift+A", false)]
    public void WarnsForCtrlAltCharacterKeysOnly(string gesture, bool conflict)
    {
        Assert.Equal(conflict, KeyboardLayout.ConflictsWithAltGr(gesture, true));
        Assert.False(KeyboardLayout.ConflictsWithAltGr(gesture, false));
    }
}
