using CliManager.Core.Registry;

namespace CliManager.Tests;

public class KeyNameHelperTests
{
    [Fact]
    public void GenerateKeyName_PadsOrderCorrectly()
    {
        string key1 = KeyNameHelper.GenerateKeyName(10, "12345678-abcd", "AI Tools", width: 3);
        Assert.StartsWith("010_AI_Tools_", key1);

        string key2 = KeyNameHelper.GenerateKeyName(5, "12345678-abcd", "Claude", width: 4);
        Assert.StartsWith("0005_Claude_", key2);
    }

    [Fact]
    public void GenerateSafeSlug_SanitizesSpecialCharacters()
    {
        string slug = KeyNameHelper.GenerateSafeSlug("Claude & Kimi / 2.0!", "guid-12345678");
        Assert.StartsWith("Claude__Kimi__20_", slug);
        Assert.DoesNotContain("&", slug);
        Assert.DoesNotContain("/", slug);
        Assert.DoesNotContain("!", slug);
    }
}
