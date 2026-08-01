using System.Windows;
using SiteManager.App.ViewModels;

namespace SiteManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(new ShellViewModel())
    {
    }

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
