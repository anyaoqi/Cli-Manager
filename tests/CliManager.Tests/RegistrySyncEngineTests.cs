using CliManager.Core.Models;
using CliManager.Core.Registry;
using Microsoft.Win32;

namespace CliManager.Tests;

public class RegistrySyncEngineTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly RegistryKey _rootKey;
    private readonly RegistrySyncEngine _engine;

    public RegistrySyncEngineTests()
    {
        _rootKey = Microsoft.Win32.Registry.CurrentUser;
        _testBasePath = $@"Software\CliManager_UnitTests_{Guid.NewGuid():N}";
        _engine = new RegistrySyncEngine(_rootKey, _testBasePath);
    }

    public void Dispose()
    {
        try
        {
            _rootKey.DeleteSubKeyTree(_testBasePath, throwOnMissingSubKey: false);
        }
        catch
        {
            // 忽略测试清理异常
        }
    }

    [Fact]
    public void Sync_CreatesFolderAndDirectTools_WithCorrectHierarchy()
    {
        var folder = new FolderItem
        {
            Id = "folder-ai",
            Name = "AI 工具箱",
            Order = 10,
            Enabled = true
        };

        var childTool1 = new ToolItem
        {
            Id = "tool-claude",
            Name = "Claude Code",
            Executable = @"C:\tools\claude.exe",
            Host = TerminalHosts.WindowsTerminal,
            ParentId = folder.Id,
            Order = 10,
            Enabled = true
        };

        var childTool2 = new ToolItem
        {
            Id = "tool-opencode",
            Name = "OpenCode",
            Executable = @"C:\tools\opencode.exe",
            Host = TerminalHosts.Cmd,
            ParentId = folder.Id,
            Order = 20,
            Enabled = false // 测试软禁用
        };

        var directTool = new ToolItem
        {
            Id = "tool-kimi",
            Name = "Kimi CLI",
            Executable = @"C:\tools\kimi.exe",
            Host = TerminalHosts.WindowsPowerShell,
            ParentId = null,
            Order = 20,
            Enabled = true
        };

        var config = new CliConfig
        {
            Folders = [folder],
            Tools = [childTool1, childTool2, directTool]
        };

        // 执行同步
        var result = _engine.Sync(config);

        Assert.True(result.Success);
        Assert.Empty(result.Errors);

        using var baseKey = _rootKey.OpenSubKey(_testBasePath);
        Assert.NotNull(baseKey);

        string[] topKeys = baseKey.GetSubKeyNames();
        // 应有 2 个顶级项：AI 工具箱 (order 10) 和 Kimi CLI (order 20)
        Assert.Equal(2, topKeys.Length);

        string folderKeyName = Assert.Single(topKeys, k => k.Contains("folderai"));
        string directKeyName = Assert.Single(topKeys, k => k.Contains("toolkimi"));

        // 验证顶级文件夹属性
        using (var fKey = baseKey.OpenSubKey(folderKeyName))
        {
            Assert.NotNull(fKey);
            Assert.Equal("AI 工具箱", fKey.GetValue(RegistryConstants.MuiVerbValueName));
            // 回归锁定：级联父键写 Default 值会导致 Explorer 无法展开子菜单
            Assert.Null(fKey.GetValue(""));
            Assert.Null(fKey.GetValue(RegistryConstants.SubCommandsValueName));
            string? extKey = fKey.GetValue(RegistryConstants.ExtendedSubCommandsKeyValueName) as string;
            Assert.NotNull(extKey);
            Assert.EndsWith(folderKeyName, extKey);
            Assert.Equal(1, fKey.GetValue(RegistryConstants.ManagedValueName));

            using var shellKey = fKey.OpenSubKey("shell");
            Assert.NotNull(shellKey);

            string[] childKeys = shellKey.GetSubKeyNames();
            Assert.Equal(2, childKeys.Length);

            // 验证子项 1 (Claude Code)
            string claudeKeyName = Assert.Single(childKeys, k => k.Contains("Claude"));
            using var claudeKey = shellKey.OpenSubKey(claudeKeyName);
            Assert.NotNull(claudeKey);
            Assert.Equal("Claude Code", claudeKey.GetValue(RegistryConstants.MuiVerbValueName));
            Assert.Null(claudeKey.GetValue(RegistryConstants.LegacyDisableValueName));
            using var cmdKey = claudeKey.OpenSubKey("command");
            Assert.NotNull(cmdKey);
            string? cmd = cmdKey.GetValue("") as string;
            Assert.NotNull(cmd);
            Assert.Contains("claude.exe", cmd);

            // 验证子项 2 (OpenCode - 软禁用)
            string opencodeKeyName = Assert.Single(childKeys, k => k.Contains("OpenCode"));
            using var opencodeKey = shellKey.OpenSubKey(opencodeKeyName);
            Assert.NotNull(opencodeKey);
            Assert.Equal("", opencodeKey.GetValue(RegistryConstants.LegacyDisableValueName));
        }

        // 验证顶级直出项
        using (var dKey = baseKey.OpenSubKey(directKeyName))
        {
            Assert.NotNull(dKey);
            Assert.Equal("Kimi CLI", dKey.GetValue(RegistryConstants.MuiVerbValueName));
            using var cmdKey = dKey.OpenSubKey("command");
            Assert.NotNull(cmdKey);
            string? cmd = cmdKey.GetValue("") as string;
            Assert.NotNull(cmd);
            Assert.Contains("kimi.exe", cmd);
        }
    }

    [Fact]
    public void Sync_DeletesObsoleteKeys_WhenItemIsRemoved()
    {
        var tool1 = new ToolItem
        {
            Id = "tool-1",
            Name = "Tool 1",
            Executable = @"C:\tools\1.exe",
            Order = 10
        };
        var tool2 = new ToolItem
        {
            Id = "tool-2",
            Name = "Tool 2",
            Executable = @"C:\tools\2.exe",
            Order = 20
        };

        var config1 = new CliConfig { Tools = [tool1, tool2] };
        _engine.Sync(config1);

        using (var baseKey = _rootKey.OpenSubKey(_testBasePath))
        {
            Assert.NotNull(baseKey);
            Assert.Equal(2, baseKey.GetSubKeyNames().Length);
        }

        // 移除 tool2，只保留 tool1
        var config2 = new CliConfig { Tools = [tool1] };
        var result2 = _engine.Sync(config2);

        Assert.True(result2.Success);
        Assert.Equal(1, result2.DeletedCount);

        using (var baseKey = _rootKey.OpenSubKey(_testBasePath))
        {
            Assert.NotNull(baseKey);
            string[] remainingKeys = baseKey.GetSubKeyNames();
            Assert.Single(remainingKeys);
            Assert.Contains("Tool_1", remainingKeys[0]);
        }
    }

    [Fact]
    public void RemoveAllManaged_DeletesOnlyManagedKeys()
    {
        // 预先建立一个非受管项（模拟系统项或其他软件项）
        using (var baseKey = _rootKey.CreateSubKey(_testBasePath, writable: true))
        {
            using var sysKey = baseKey.CreateSubKey("SystemGit");
            sysKey.SetValue("MUIVerb", "Git GUI Here");
        }

        // 写入受管项
        var tool = new ToolItem
        {
            Id = "tool-managed",
            Name = "Managed Tool",
            Executable = @"C:\tools\app.exe"
        };
        _engine.Sync(new CliConfig { Tools = [tool] });

        using (var baseKey = _rootKey.OpenSubKey(_testBasePath))
        {
            Assert.NotNull(baseKey);
            Assert.Equal(2, baseKey.GetSubKeyNames().Length);
        }

        // 执行一键清空受管项
        int deleted = _engine.RemoveAllManaged();
        Assert.Equal(1, deleted);

        // 验证非受管项完好无损！
        using (var baseKey = _rootKey.OpenSubKey(_testBasePath))
        {
            Assert.NotNull(baseKey);
            string[] remaining = baseKey.GetSubKeyNames();
            Assert.Single(remaining);
            Assert.Equal("SystemGit", remaining[0]);
        }
    }

    [Fact]
    public void Sync_WithHiddenHklmKeys_WritesShadowOverrideWithLegacyDisable()
    {
        var config = new CliConfig
        {
            Settings = new AppSettings
            {
                HiddenHklmKeys = ["AnyCode", "OldTool"]
            }
        };

        var result = _engine.Sync(config);
        Assert.True(result.Success);

        using var baseKey = _rootKey.OpenSubKey(_testBasePath);
        Assert.NotNull(baseKey);

        using var anyCodeKey = baseKey.OpenSubKey("AnyCode");
        Assert.NotNull(anyCodeKey);
        Assert.Equal("", anyCodeKey.GetValue(RegistryConstants.LegacyDisableValueName));
        Assert.Equal(1, anyCodeKey.GetValue(RegistryConstants.ShadowOverrideValueName));

        using var oldToolKey = baseKey.OpenSubKey("OldTool");
        Assert.NotNull(oldToolKey);
        Assert.Equal("", oldToolKey.GetValue(RegistryConstants.LegacyDisableValueName));
        Assert.Equal(1, oldToolKey.GetValue(RegistryConstants.ShadowOverrideValueName));
    }

    [Fact]
    public void Sync_FolderWithoutEnabledChildren_OmitsCascadeValues()
    {
        var folder = new FolderItem
        {
            Id = "folder-e",
            Name = "Empty Box",
            Order = 10,
            Enabled = true
        };

        var disabledChild = new ToolItem
        {
            Id = "tool-disabled",
            Name = "Disabled Tool",
            Executable = @"C:\tools\dis.exe",
            Host = TerminalHosts.WindowsTerminal,
            ParentId = folder.Id,
            Order = 10,
            Enabled = false
        };

        var result = _engine.Sync(new CliConfig { Folders = [folder], Tools = [disabledChild] });
        Assert.True(result.Success);

        using var baseKey = _rootKey.OpenSubKey(_testBasePath);
        Assert.NotNull(baseKey);

        string folderKeyName = Assert.Single(baseKey.GetSubKeyNames(), k => k.Contains("foldere"));
        using var fKey = baseKey.OpenSubKey(folderKeyName);
        Assert.NotNull(fKey);
        Assert.Null(fKey.GetValue(RegistryConstants.ExtendedSubCommandsKeyValueName));
        Assert.Null(fKey.GetValue(RegistryConstants.SubCommandsValueName));

        // 子项仍保留（软禁用），重新启用后再次同步即可恢复级联
        using var shellKey = fKey.OpenSubKey("shell");
        Assert.NotNull(shellKey);
        string childKeyName = Assert.Single(shellKey.GetSubKeyNames());
        using var childKey = shellKey.OpenSubKey(childKeyName);
        Assert.NotNull(childKey);
        Assert.Equal("", childKey.GetValue(RegistryConstants.LegacyDisableValueName));
    }

    [Fact]
    public void Sync_CleansUpLegacyContextMenusTree_KeepsThirdPartyEntries()
    {
        string legacyRootPath = RegistryConstants.LegacyContextMenusPath;
        string staleByName = KeyNameHelper.GenerateKeyName(10, "folder-legacy", "Legacy Box", 3);
        string staleByChildFlag = $"090_StaleByChild_{Guid.NewGuid():N}".Substring(0, 20);
        string thirdParty = $"ThirdParty_{Guid.NewGuid():N}";

        // 模拟历史版本遗留：与受管文件夹同名的键（无标记）；异名键 shell 子项带受管标记；第三方键无任何标记
        try
        {
            using (var legacyRoot = _rootKey.CreateSubKey(legacyRootPath, writable: true))
            {
                using (var byName = legacyRoot.CreateSubKey(staleByName, writable: true))
                {
                    using var shell = byName.CreateSubKey("shell");
                }

                using (var byChild = legacyRoot.CreateSubKey(staleByChildFlag, writable: true))
                {
                    using var shell = byChild.CreateSubKey("shell");
                    using var child = shell.CreateSubKey("010_Old_Child");
                    child.SetValue(RegistryConstants.ManagedValueName, 1, RegistryValueKind.DWord);
                }

                using (var third = legacyRoot.CreateSubKey(thirdParty, writable: true))
                {
                    using var shell = third.CreateSubKey("shell");
                    using var child = shell.CreateSubKey("SomeCommand");
                }
            }

            var folder = new FolderItem { Id = "folder-legacy", Name = "Legacy Box", Order = 10 };
            var legacyTool = new ToolItem
            {
                Id = "tool-legacy",
                Name = "Legacy Tool",
                Executable = @"C:\tools\legacy.exe",
                ParentId = folder.Id,
                Order = 10
            };

            var result = _engine.Sync(new CliConfig { Folders = [folder], Tools = [legacyTool] });
            Assert.True(result.Success);

            using (var checkRoot = _rootKey.OpenSubKey(legacyRootPath))
            {
                Assert.NotNull(checkRoot);
                string[] remaining = checkRoot.GetSubKeyNames();

                Assert.DoesNotContain(staleByName, remaining);
                Assert.DoesNotContain(staleByChildFlag, remaining);
                Assert.Contains(thirdParty, remaining);
            }
        }
        finally
        {
            using var cleanupRoot = _rootKey.OpenSubKey(legacyRootPath, writable: true);
            cleanupRoot?.DeleteSubKeyTree(staleByName, throwOnMissingSubKey: false);
            cleanupRoot?.DeleteSubKeyTree(staleByChildFlag, throwOnMissingSubKey: false);
            cleanupRoot?.DeleteSubKeyTree(thirdParty, throwOnMissingSubKey: false);
        }
    }
}
