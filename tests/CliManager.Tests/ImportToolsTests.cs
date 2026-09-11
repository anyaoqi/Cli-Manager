using CliManager.Core.Detection;
using CliManager.Core.Models;

namespace CliManager.Tests;

public class ImportToolsTests
{
    private static DetectedTool MakeTool(string name, string executable, string? presetId = null) => new()
    {
        Name = name,
        ExecutablePath = executable,
        Source = "node",
        MatchedPresetId = presetId,
        RecommendedDisplayName = name,
        RecommendedHost = "wt"
    };

    // ---------- ImportToolsViewModel ----------

    [Fact]
    public void Tools_SelectedByDefault_And_FolderOptionsContainRootExistingAndNew()
    {
        var candidates = new[]
        {
            MakeTool("Claude Code", @"C:\fakenode\claude.cmd", "claude"),
            MakeTool("npm", @"C:\fakenode\npm.cmd")
        };
        var folders = new List<FolderItem> { new() { Id = "f1", Name = "AI 编程工具" } };

        var vm = new CliManager.App.ViewModels.ImportToolsViewModel(candidates, folders);

        Assert.Equal(2, vm.Tools.Count);
        Assert.All(vm.Tools, t => Assert.True(t.IsSelected));
        Assert.Equal(2, vm.SelectedCount);

        // 根层级 + 已有分组 + 新建分组
        Assert.Equal(3, vm.FolderOptions.Count);
        Assert.Null(vm.FolderOptions[0].Id);
        Assert.Equal("f1", vm.FolderOptions[1].Id);
        Assert.Equal(CliManager.App.ViewModels.ImportToolsViewModel.NewFolderOptionId, vm.FolderOptions[2].Id);

        // 默认选中根层级，不默认创建任何分组
        Assert.Same(vm.FolderOptions[0], vm.SelectedFolder);
        Assert.False(vm.IsNewFolderSelected);
    }

    [Fact]
    public void BuildResult_DefaultSelection_TargetsRootLevel()
    {
        var candidates = new[]
        {
            MakeTool("Claude Code", @"C:\fakenode\claude.cmd", "claude"),
            MakeTool("codex", @"C:\fakenode\codex.cmd", "codex")
        };

        var vm = new CliManager.App.ViewModels.ImportToolsViewModel(candidates, []);

        // 取消勾选第一项后构建结果
        vm.Tools[0].IsSelected = false;
        var result = vm.BuildResult();

        Assert.Null(result.TargetFolderId);
        Assert.Null(result.NewFolderName);
        Assert.Single(result.SelectedTools);
        Assert.Equal("codex", result.SelectedTools[0].MatchedPresetId);
    }

    [Fact]
    public void BuildResult_NewFolder_RequiresNonEmptyName_AndTrimsIt()
    {
        var vm = new CliManager.App.ViewModels.ImportToolsViewModel(
            [MakeTool("npm", @"C:\fakenode\npm.cmd")], []);

        vm.SelectedFolder = vm.FolderOptions.Single(o =>
            o.Id == CliManager.App.ViewModels.ImportToolsViewModel.NewFolderOptionId);

        Assert.True(vm.IsNewFolderSelected);
        // 新分组名为空时不可导入
        Assert.False(vm.ImportCommand.CanExecute(null));

        vm.NewFolderName = "  我的分组  ";
        Assert.True(vm.ImportCommand.CanExecute(null));

        var result = vm.BuildResult();
        Assert.Equal("我的分组", result.NewFolderName);
        Assert.Null(result.TargetFolderId);
    }

    [Fact]
    public void ImportCommand_CanExecute_FollowsToolSelection()
    {
        var vm = new CliManager.App.ViewModels.ImportToolsViewModel(
            [MakeTool("npm", @"C:\fakenode\npm.cmd"), MakeTool("npx", @"C:\fakenode\npx.cmd")], []);

        Assert.True(vm.ImportCommand.CanExecute(null));

        vm.UnselectAllCommand.Execute(null);
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.ImportCommand.CanExecute(null));

        vm.SelectAllCommand.Execute(null);
        Assert.Equal(2, vm.SelectedCount);
        Assert.True(vm.ImportCommand.CanExecute(null));
    }

    [Fact]
    public void SelectAiOnly_SelectsOnlyToolsWithPresets()
    {
        var vm = new CliManager.App.ViewModels.ImportToolsViewModel(
            [
                MakeTool("Claude Code", @"C:\fakenode\claude.cmd", "claude"),
                MakeTool("npm", @"C:\fakenode\npm.cmd"),
                MakeTool("Codex CLI", @"C:\fakenode\codex.cmd", "codex")
            ], []);

        Assert.Equal(3, vm.SelectedCount);

        vm.SelectAiOnlyCommand.Execute(null);

        Assert.Equal(2, vm.SelectedCount);
        Assert.True(vm.Tools[0].IsSelected); // claude
        Assert.False(vm.Tools[1].IsSelected); // npm
        Assert.True(vm.Tools[2].IsSelected); // codex
    }

    [Fact]
    public void BuildResult_NewFolderWithEmptyName_DoesNotLeakSentinelAsTargetFolderId()
    {
        var vm = new CliManager.App.ViewModels.ImportToolsViewModel(
            [MakeTool("npm", @"C:\fakenode\npm.cmd")], []);

        vm.SelectedFolder = vm.FolderOptions.Single(o =>
            o.Id == CliManager.App.ViewModels.ImportToolsViewModel.NewFolderOptionId);

        vm.NewFolderName = "   "; // 留空

        var result = vm.BuildResult();
        Assert.Null(result.TargetFolderId);
        Assert.Null(result.NewFolderName);
        Assert.NotEqual(CliManager.App.ViewModels.ImportToolsViewModel.NewFolderOptionId, result.TargetFolderId);
    }

    // ---------- MainViewModel 导入主流程（STA：涉及 WPF 树构建） ----------

    [Fact]
    public void ImportDetectedTools_RootTarget_DoesNotCreateAnyFolder()
    {
        var thread = new Thread(() =>
        {
            var vm = new CliManager.App.ViewModels.MainViewModel();
            var fakeTools = new[]
            {
                MakeTool("Zz Test A", @"C:\fakenode\zz-test-a.cmd"),
                MakeTool("Zz Test B", @"C:\fakenode\zz-test-b.cmd")
            };
            vm._unconfiguredDetectedTools.Clear();
            vm._unconfiguredDetectedTools.AddRange(fakeTools);

            int foldersBefore = vm.Config.Folders.Count;
            int toolsBefore = vm.Config.Tools.Count;

            // 模拟用户：只勾选第一个工具，目标为根层级
            vm.ImportDialogHandler = (tools, folders) => new CliManager.App.ViewModels.ImportSelectionResult
            {
                SelectedTools = tools.Take(1).ToList(),
                TargetFolderId = null,
                NewFolderName = null
            };

            vm.ImportDetectedToolsCommand.Execute(null);

            // 不主动创建 AI 编程工具等任何分组
            Assert.Equal(foldersBefore, vm.Config.Folders.Count);

            var added = vm.Config.Tools.Single(t => t.Executable == @"C:\fakenode\zz-test-a.cmd");
            Assert.Null(added.ParentId); // 根层级一级直出
            Assert.Equal(toolsBefore + 1, vm.Config.Tools.Count);

            // 未导入的候选仍在横幅中
            Assert.Single(vm._unconfiguredDetectedTools);
            Assert.True(vm.HasDetectedTools);
            Assert.Contains(vm.RootNodes, n => !n.IsFolder && n.Id == added.Id);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void ImportDetectedTools_NewFolder_CreatesFolderAndImportsIntoIt()
    {
        var thread = new Thread(() =>
        {
            var vm = new CliManager.App.ViewModels.MainViewModel();
            string newFolderName = $"测试新分组 {Guid.NewGuid():N}";
            vm._unconfiguredDetectedTools.Clear();
            vm._unconfiguredDetectedTools.AddRange(new[]
            {
                MakeTool("Zz Test A", @"C:\fakenode\zz-test-a.cmd"),
                MakeTool("Zz Test B", @"C:\fakenode\zz-test-b.cmd")
            });

            vm.ImportDialogHandler = (tools, folders) => new CliManager.App.ViewModels.ImportSelectionResult
            {
                SelectedTools = tools.ToList(),
                NewFolderName = $"  {newFolderName}  "
            };

            vm.ImportDetectedToolsCommand.Execute(null);

            var folder = vm.Config.Folders.Single(f => f.Name == newFolderName);
            Assert.All(
                vm.Config.Tools.Where(t => t.Executable.StartsWith(@"C:\fakenode\zz-test")),
                t => Assert.Equal(folder.Id, t.ParentId));

            // 横幅清空
            Assert.Empty(vm._unconfiguredDetectedTools);
            Assert.False(vm.HasDetectedTools);

            // 树结构中工具挂在新分组下
            var folderNode = vm.RootNodes.Single(n => n.IsFolder && n.Id == folder.Id);
            Assert.Equal(2, folderNode.Children.Count);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void ImportDetectedTools_ExistingFolderTarget_PutsToolsInside()
    {
        var thread = new Thread(() =>
        {
            var vm = new CliManager.App.ViewModels.MainViewModel();
            var targetFolder = new FolderItem { Name = $"目标分组 {Guid.NewGuid():N}" };
            vm.Config.Folders.Add(targetFolder);
            vm.BuildTree();

            vm._unconfiguredDetectedTools.Clear();
            vm._unconfiguredDetectedTools.AddRange(new[]
            {
                MakeTool("Zz Test A", @"C:\fakenode\zz-test-a.cmd")
            });

            vm.ImportDialogHandler = (tools, folders) => new CliManager.App.ViewModels.ImportSelectionResult
            {
                SelectedTools = tools.ToList(),
                TargetFolderId = folders.First(f => f.Id == targetFolder.Id).Id
            };

            vm.ImportDetectedToolsCommand.Execute(null);

            var added = vm.Config.Tools.Single(t => t.Executable == @"C:\fakenode\zz-test-a.cmd");
            Assert.Equal(targetFolder.Id, added.ParentId);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void ImportDetectedTools_Cancelled_ChangesNothing()
    {
        var thread = new Thread(() =>
        {
            var vm = new CliManager.App.ViewModels.MainViewModel();
            vm._unconfiguredDetectedTools.Clear();
            vm._unconfiguredDetectedTools.AddRange(new[]
            {
                MakeTool("Zz Test A", @"C:\fakenode\zz-test-a.cmd")
            });

            int foldersBefore = vm.Config.Folders.Count;
            int toolsBefore = vm.Config.Tools.Count;

            vm.ImportDialogHandler = (tools, folders) => null; // 用户关闭弹框
            vm.ImportDetectedToolsCommand.Execute(null);

            Assert.Equal(foldersBefore, vm.Config.Folders.Count);
            Assert.Equal(toolsBefore, vm.Config.Tools.Count);
            Assert.Single(vm._unconfiguredDetectedTools);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
