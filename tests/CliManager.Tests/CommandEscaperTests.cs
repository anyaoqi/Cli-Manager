using CliManager.Core.Launch;

namespace CliManager.Tests;

public class CommandEscaperTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("arg with spaces", "\"arg with spaces\"")]
    [InlineData("中文路径", "中文路径")]
    [InlineData("中文 路径 带空格", "\"中文 路径 带空格\"")]
    [InlineData("a&b", "\"a&b\"")]
    [InlineData("a|b", "\"a|b\"")]
    [InlineData("a<b", "\"a<b\"")]
    [InlineData("a>b", "\"a>b\"")]
    [InlineData("a^b", "\"a^b\"")]
    [InlineData("C:\\path\\", "C:\\path\\")]
    [InlineData("C:\\my path\\", "\"C:\\my path\\\\\"")]
    public void EscapeCmdArgument_EscapesProperly(string input, string expected)
    {
        string result = CommandEscaper.EscapeCmdArgument(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("simple", "'simple'")]
    [InlineData("arg with spaces", "'arg with spaces'")]
    [InlineData("O'Brien", "'O''Brien'")]
    [InlineData("Don't touch", "'Don''t touch'")]
    [InlineData("", "''")]
    public void EscapePowerShellArgument_DoublesSingleQuotes(string input, string expected)
    {
        string result = CommandEscaper.EscapePowerShellArgument(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("wt.exe new-tab; split-pane", "wt.exe new-tab\\; split-pane")]
    [InlineData("echo a;b;c", "echo a\\;b\\;c")]
    [InlineData("echo already\\;escaped", "echo already\\;escaped")]
    [InlineData("plain command", "plain command")]
    public void EscapeWtSemicolons_EscapesUnescapedSemicolons(string input, string expected)
    {
        string result = CommandEscaper.EscapeWtSemicolons(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ValidatePercentSafety_WarnsOnVerbParamConflict()
    {
        var result = new LaunchValidationResult();
        CommandEscaper.ValidatePercentSafety("测试参数", "%WINDIR%", result);

        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("动词参数关键字"));
    }

    [Fact]
    public void ValidatePercentSafety_WarnsOnEnvironmentVariableReference()
    {
        var result = new LaunchValidationResult();
        CommandEscaper.ValidatePercentSafety("测试参数", "%MY_TOOL_HOME%", result);

        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("环境变量引用形式"));
    }

    [Fact]
    public void ValidatePercentSafety_PassesSafePercentLiteral()
    {
        var result = new LaunchValidationResult();
        // 比如 D:\100%\tool.exe，% 后面跟的是 \，不是动词参数，也不是成对 %...%
        CommandEscaper.ValidatePercentSafety("路径", @"D:\100%\tool.exe", result);

        Assert.Empty(result.Warnings);
        Assert.Empty(result.Errors);
    }
}
