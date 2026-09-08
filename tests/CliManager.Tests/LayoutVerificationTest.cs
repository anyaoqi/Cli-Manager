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
            var mainWorkspace = (Grid)grid.Children[2];
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
}
