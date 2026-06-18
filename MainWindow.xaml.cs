using System.Windows;
using PrReviewHelper.ViewModels;

namespace PrReviewHelper;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}