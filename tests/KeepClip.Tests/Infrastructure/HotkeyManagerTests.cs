namespace KeepClip.Tests.Infrastructure;

public class HotkeyManagerTests
{
    [Theory]
    [InlineData("Alt+F10", 1U, 121U)]
    [InlineData("CTRL + Shift + a", 6U, 65U)]
    [InlineData("Control+Win+F1", 10U, 112U)]
    [InlineData("Meta+Alt+Space", 9U, 32U)]
    [InlineData("Alt+1", 1U, 49U)]
    [InlineData("Ctrl+0", 2U, 48U)]
    public void Shortcuts_map_to_the_expected_windows_modifier_and_key_codes(string shortcut, uint modifiers, uint key)
    {
        Assert.True(HotkeyManager.TryParse(shortcut, out var actualModifiers, out var actualKey));
        Assert.Equal(modifiers, actualModifiers);
        Assert.Equal(key, actualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Alt+UnknownKey")]
    [InlineData("Alt+10000000")]
    [InlineData("Ctrl++A")]
    [InlineData("Alt+ControlKey")]
    [InlineData("Ctrl+ShiftKey")]
    public void Invalid_or_ambiguous_shortcuts_are_rejected(string shortcut)
        => Assert.False(HotkeyManager.TryParse(shortcut, out _, out _));
}