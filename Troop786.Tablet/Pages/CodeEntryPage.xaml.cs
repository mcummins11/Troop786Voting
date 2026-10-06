using Troop786.Tablet.ViewModels;

namespace Troop786.Tablet.Pages;

public partial class CodeEntryPage : ContentPage
{
    public CodeEntryPage(CodeEntryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
