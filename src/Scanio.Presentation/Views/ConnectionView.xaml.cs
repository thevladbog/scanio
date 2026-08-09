using System.Windows;
using System.Windows.Input;
using Scanio.Presentation.ViewModels;

namespace Scanio.Presentation.Views;

public partial class ConnectionView : System.Windows.Controls.UserControl
{
    private ConnectionViewModel? _subscribedViewModel;

    public ConnectionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeToViewModel(DataContext as ConnectionViewModel);
        ReportKeyboardFocus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => SubscribeToViewModel(null);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded)
        {
            SubscribeToViewModel(e.NewValue as ConnectionViewModel);
            ReportKeyboardFocus();
        }
    }

    private void SubscribeToViewModel(ConnectionViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.KeyboardFocusRequested -= OnKeyboardFocusRequested;
            _subscribedViewModel.SetKeyboardSurfaceFocused(false);
        }

        _subscribedViewModel = viewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.KeyboardFocusRequested += OnKeyboardFocusRequested;
        }
    }

    private void OnKeyboardFocusRequested(object? sender, EventArgs e)
    {
        if (IsLoaded && KeyboardCaptureInput.IsVisible && KeyboardCaptureInput.IsEnabled)
        {
            Keyboard.Focus(KeyboardCaptureInput);
        }
    }

    private void OnKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        ReportKeyboardFocus();

    private void ReportKeyboardFocus() =>
        (DataContext as ConnectionViewModel)?.SetKeyboardSurfaceFocused(
            KeyboardCaptureInput.IsKeyboardFocusWithin);

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (DataContext is ConnectionViewModel viewModel && !string.IsNullOrEmpty(e.Text))
        {
            viewModel.AcceptKeyboardText(e.Text);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Tab && DataContext is ConnectionViewModel viewModel)
        {
            viewModel.CompleteKeyboardInput();
            e.Handled = true;
        }
    }
}
