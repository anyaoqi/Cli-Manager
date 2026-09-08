using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using CliManager.App;
using Xunit;

namespace CliManager.Tests;

public class LayoutVerificationTest
{
    [Fact]
    public void VerifyMainWindowLayoutStructure()
    {
        var thread = new Thread(() =>
        {
            var app = new CliManager.App.App();
            app.InitializeComponent();
            var window = new MainWindow();
            window.Measure(new Size(1120, 740));
            window.Arrange(new Rect(0, 0, 1120, 740));

            var grid = (Grid)window.Content;
            var infoBar = grid.Children.OfType<Wpf.Ui.Controls.InfoBar>().FirstOrDefault();
            Assert.NotNull(infoBar);

            var mainWorkspace = grid.Children.OfType<Grid>().First(g => Grid.GetRow(g) == 2);
            var leftCard = (Wpf.Ui.Controls.Card)mainWorkspace.Children[0];
            var rightCard = (Wpf.Ui.Controls.Card)mainWorkspace.Children[2];

            Assert.Equal(4, grid.RowDefinitions.Count);
            Assert.True(grid.RowDefinitions[2].Height.IsStar);
            Assert.Equal(VerticalAlignment.Stretch, mainWorkspace.VerticalAlignment);
            Assert.Equal(VerticalAlignment.Stretch, leftCard.VerticalAlignment);
            Assert.Equal(VerticalAlignment.Stretch, rightCard.VerticalAlignment);

            // Verify VerticalContentAlignment is Stretch
            Assert.Equal(VerticalAlignment.Stretch, leftCard.VerticalContentAlignment);
            Assert.Equal(VerticalAlignment.Stretch, rightCard.VerticalContentAlignment);
            Assert.Equal(HorizontalAlignment.Stretch, leftCard.HorizontalContentAlignment);
            Assert.Equal(HorizontalAlignment.Stretch, rightCard.HorizontalContentAlignment);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void VerifyToolEditorLoadPreservesParentIdAndIcon()
    {
        var thread = new Thread(() =>
        {
            var folder = new CliManager.Core.Models.FolderItem
            {
                Id = "ai-folder-guid",
                Name = "AI 编程工具"
            };
            var tool = new CliManager.Core.Models.ToolItem
            {
                Id = "tool-guid",
                Name = "Claude Code",
                ParentId = "ai-folder-guid",
                Icon = @"C:\test\claude.exe",
                Executable = @"C:\test\claude.exe"
            };

            var editor = new CliManager.App.ViewModels.ToolEditorViewModel();
            bool callbackInvoked = false;

            editor.Load(tool, [folder], () =>
            {
                callbackInvoked = true;
            });

            // Must NOT have triggered callback or wiped out data during Load
            Assert.False(callbackInvoked);
            Assert.Equal("ai-folder-guid", tool.ParentId);
            Assert.Equal(@"C:\test\claude.exe", tool.Icon);
            Assert.Equal(@"C:\test\claude.exe", tool.Executable);
            Assert.NotNull(editor.SelectedFolder);
            Assert.Equal("ai-folder-guid", editor.SelectedFolder.Id);
            Assert.Equal(@"C:\test\claude.exe", editor.Icon);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
