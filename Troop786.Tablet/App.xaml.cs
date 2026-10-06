using Troop786.Tablet.Pages;

namespace Troop786.Tablet;

public partial class App : Application
{
    private readonly CodeEntryPage _codeEntry;

    public App(CodeEntryPage codeEntry)
    {
        InitializeComponent();
        _codeEntry = codeEntry;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new Window(_codeEntry);
}
