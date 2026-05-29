using System.Windows;
using System.Windows.Controls;
using ImageTool.Core;

namespace ImageTool.Host.Workspace;

public partial class CollectionsPanel : UserControl
{
    private ICatalogService? _catalog;
    private IWorkspaceService? _workspace;

    public CollectionsPanel()
    {
        InitializeComponent();
    }

    public void Bind(ICatalogService catalog, IWorkspaceService workspace)
    {
        _catalog = catalog;
        _workspace = workspace;
        _catalog.CollectionsChanged += (s, e) => Dispatcher.BeginInvoke(Refresh);
        _catalog.ImportCompleted += (s, e) => Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    public void Refresh()
    {
        if (_catalog == null) return;
        var collections = _catalog.GetCollections();
        lstCollections.ItemsSource = collections;
    }

    private void BtnAddCollection_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog == null) return;

        var name = PromptInput("New Collection", "Collection name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        _catalog.CreateCollection(name);
    }

    private void LstCollections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_catalog == null || _workspace == null) return;
        if (lstCollections.SelectedItem is not ImageCollection col) return;

        var images = _catalog.GetCollectionImages(col.Id);
        _workspace.OpenCatalogView(images.Select(i => i.FilePath).ToList(), col.Name);
    }

    private void MenuRename_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog == null) return;
        if (lstCollections.SelectedItem is not ImageCollection col) return;

        var newName = PromptInput("Rename Collection", "New name:", col.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;

        _catalog.RenameCollection(col.Id, newName);
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog == null) return;
        if (lstCollections.SelectedItem is not ImageCollection col) return;

        var result = MessageBox.Show($"Delete collection \"{col.Name}\"?\n(Photos will not be deleted from disk.)",
            "Delete Collection", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _catalog.DeleteCollection(col.Id);
    }

    private static string? PromptInput(string title, string prompt, string defaultValue = "")
    {
        var dlg = new Window
        {
            Title = title,
            Width = 340,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.Black,
            ResizeMode = ResizeMode.NoResize
        };

        var sp = new StackPanel { Margin = new Thickness(16) };
        var lbl = new TextBlock { Text = prompt, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 8) };
        var txt = new TextBox { Text = defaultValue, Background = System.Windows.Media.Brushes.DarkGray, Padding = new Thickness(4), FontSize = 13 };
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var btnOk = new Button { Content = "OK", Width = 70, Height = 28, IsDefault = true };
        var btnCancel = new Button { Content = "Cancel", Width = 70, Height = 28, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };

        string? result = null;
        btnOk.Click += (s, e) => { result = txt.Text; dlg.DialogResult = true; };
        btnCancel.Click += (s, e) => { dlg.DialogResult = false; };

        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        sp.Children.Add(lbl);
        sp.Children.Add(txt);
        sp.Children.Add(btnPanel);
        dlg.Content = sp;

        dlg.Owner = Window.GetWindow(Application.Current.MainWindow);
        dlg.ShowDialog();
        return result;
    }
}
