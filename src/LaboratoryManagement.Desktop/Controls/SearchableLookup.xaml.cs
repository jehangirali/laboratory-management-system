using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LaboratoryManagement.Desktop.Controls;

/// <summary>Type-ahead lookup with an "Add new ..." action. The view model supplies ItemsSource in response to SearchText.
/// Selecting an item (or the VM setting SelectedItem after creating a new record) fills the box without losing the form.</summary>
public partial class SearchableLookup : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SearchableLookup),
        new PropertyMetadata(null, (d, e) => ((SearchableLookup)d).OnItemsChanged()));
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(SearchableLookup),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((SearchableLookup)d).OnSelectedChanged()));
    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(SearchableLookup),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(nameof(DisplayMemberPath), typeof(string), typeof(SearchableLookup), new PropertyMetadata("DisplayText"));
    public static readonly DependencyProperty AddNewCommandProperty = DependencyProperty.Register(nameof(AddNewCommand), typeof(ICommand), typeof(SearchableLookup),
        new PropertyMetadata(null, (d, _) => ((SearchableLookup)d).UpdateAddButton()));
    public static readonly DependencyProperty AddNewLabelProperty = DependencyProperty.Register(nameof(AddNewLabel), typeof(string), typeof(SearchableLookup), new PropertyMetadata("Add new...", (d, e) => ((SearchableLookup)d).AddBtn.Content = e.NewValue));
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SearchableLookup), new PropertyMetadata("", (d, e) => ((SearchableLookup)d).Watermark.Text = (string)e.NewValue));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public string SearchText { get => (string)GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    public string DisplayMemberPath { get => (string)GetValue(DisplayMemberPathProperty); set => SetValue(DisplayMemberPathProperty, value); }
    public ICommand? AddNewCommand { get => (ICommand?)GetValue(AddNewCommandProperty); set => SetValue(AddNewCommandProperty, value); }
    public string AddNewLabel { get => (string)GetValue(AddNewLabelProperty); set => SetValue(AddNewLabelProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    private bool _updating;

    public SearchableLookup()
    {
        InitializeComponent();
        AddBtn.Content = AddNewLabel;
        List.DisplayMemberPath = DisplayMemberPath;
        UpdateAddButton();
    }

    private void UpdateAddButton()
        => AddBtn.Visibility = AddNewCommand == null ? Visibility.Collapsed : Visibility.Visible;

    private string TextOf(object? item)
        => item == null ? "" : item.GetType().GetProperty(DisplayMemberPath)?.GetValue(item)?.ToString() ?? item.ToString() ?? "";

    private void OnItemsChanged()
    {
        List.DisplayMemberPath = DisplayMemberPath;
        List.ItemsSource = ItemsSource;
        var any = ItemsSource?.Cast<object>().Any() ?? false;
        Empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        if (Box.IsKeyboardFocused && _userTyped) Pop.IsOpen = true;
    }

    private bool _userTyped;

    private void OnSelectedChanged()
    {
        if (_updating) return; // change caused by the user typing; keep their text
        _updating = true;
        Box.Text = TextOf(SelectedItem);
        _updating = false;
        Watermark.Visibility = string.IsNullOrEmpty(Box.Text) ? Visibility.Visible : Visibility.Collapsed;
        _userTyped = false;
        Pop.IsOpen = false;
    }

    private void Box_TextChanged(object sender, TextChangedEventArgs e)
    {
        Watermark.Visibility = string.IsNullOrEmpty(Box.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_updating) return;
        _userTyped = true;
        // Editing the text invalidates the previous selection.
        if (SelectedItem != null && Box.Text != TextOf(SelectedItem)) { _updating = true; SelectedItem = null; _updating = false; }
        SearchText = Box.Text;
        Pop.IsOpen = true;
    }

    private void Box_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (SelectedItem == null) { _userTyped = true; SearchText = Box.Text; Pop.IsOpen = true; }
    }

    private void Box_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is DependencyObject d && Pop.Child != null && IsInside(Pop.Child, d)) return;
        Pop.IsOpen = false;
    }

    private static bool IsInside(DependencyObject root, DependencyObject node)
    {
        for (var cur = node; cur != null; cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur)) if (cur == root) return true;
        return false;
    }

    private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && List.Items.Count > 0) { Pop.IsOpen = true; List.SelectedIndex = Math.Min(List.SelectedIndex + 1, List.Items.Count - 1); List.ScrollIntoView(List.SelectedItem); e.Handled = true; }
        else if (e.Key == Key.Up && List.Items.Count > 0) { List.SelectedIndex = Math.Max(List.SelectedIndex - 1, 0); e.Handled = true; }
        else if (e.Key == Key.Enter && Pop.IsOpen)
        {
            var item = List.SelectedItem ?? (List.Items.Count == 1 ? List.Items[0] : null);
            if (item != null) { Choose(item); e.Handled = true; }
        }
        else if (e.Key == Key.Escape) Pop.IsOpen = false;
    }

    private void List_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var el = e.OriginalSource as DependencyObject;
        while (el != null && el is not ListBoxItem) el = VisualTreeHelper.GetParent(el);
        if (el is ListBoxItem li && li.DataContext != null) Choose(li.DataContext);
    }

    private void Choose(object item)
    {
        SelectedItem = item;
        Pop.IsOpen = false;
        Keyboard.Focus(Box);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        Pop.IsOpen = false;
        var text = Box.Text;
        if (AddNewCommand?.CanExecute(text) == true) AddNewCommand.Execute(text);
    }
}
