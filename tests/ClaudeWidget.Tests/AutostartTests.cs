using ClaudeWidget.Core;
using Microsoft.Win32;

namespace ClaudeWidget.Tests;

public sealed class AutostartTests : IDisposable
{
    private const string TestRoot = @"Software\ClaudeWidgetTests";
    private const string TestKey = TestRoot + @"\Run";

    public void Dispose()
        => Registry.CurrentUser.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false);

    [Fact]
    public void EnableIsEnabledDisableRoundTrip()
    {
        Assert.False(Autostart.IsEnabled(TestKey));
        Autostart.Enable(@"C:\fake dir\ClaudeWidget.exe", TestKey);
        Assert.True(Autostart.IsEnabled(TestKey));
        using (var key = Registry.CurrentUser.OpenSubKey(TestKey))
        {
            Assert.Equal("\"C:\\fake dir\\ClaudeWidget.exe\"", key!.GetValue("ClaudeWidget"));
        }
        Autostart.Disable(TestKey);
        Assert.False(Autostart.IsEnabled(TestKey));
    }

    [Fact]
    public void DisableWhenNotEnabledDoesNotThrow()
        => Autostart.Disable(TestKey);
}
