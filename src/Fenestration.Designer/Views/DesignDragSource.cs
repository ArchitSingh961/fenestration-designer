using System.Windows;
using System.Windows.Input;
using Fenestration.Designer.Interaction;

namespace Fenestration.Designer.Views;

/// <summary>
/// Attached behaviour that lets a library button be dragged onto the drawing:
/// <c>views:DesignDragSource.TemplateId="{Binding Template.Id}"</c>. A press without movement stays a normal click;
/// the drag starts once the mouse moves past the system drag distance.
/// </summary>
public static class DesignDragSource
{
    public static readonly DependencyProperty TemplateIdProperty = DependencyProperty.RegisterAttached(
        "TemplateId", typeof(string), typeof(DesignDragSource), new PropertyMetadata(null, OnTemplateIdChanged));

    private static readonly DependencyProperty PressPointProperty = DependencyProperty.RegisterAttached(
        "PressPoint", typeof(Point?), typeof(DesignDragSource), new PropertyMetadata(null));

    public static string? GetTemplateId(DependencyObject element) => (string?)element.GetValue(TemplateIdProperty);

    public static void SetTemplateId(DependencyObject element, string? value) => element.SetValue(TemplateIdProperty, value);

    private static void OnTemplateIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseLeftButtonDown -= OnPress;
        element.PreviewMouseMove -= OnMove;
        element.PreviewMouseLeftButtonUp -= OnRelease;
        if (e.NewValue is string)
        {
            element.PreviewMouseLeftButtonDown += OnPress;
            element.PreviewMouseMove += OnMove;
            element.PreviewMouseLeftButtonUp += OnRelease;
        }
    }

    private static void OnPress(object sender, MouseButtonEventArgs e)
        => ((UIElement)sender).SetValue(PressPointProperty, e.GetPosition((IInputElement)sender));

    private static void OnRelease(object sender, MouseButtonEventArgs e)
        => ((UIElement)sender).SetValue(PressPointProperty, null);

    private static void OnMove(object sender, MouseEventArgs e)
    {
        var element = (UIElement)sender;
        if (e.LeftButton != MouseButtonState.Pressed || element.GetValue(PressPointProperty) is not Point start)
            return;
        var delta = e.GetPosition(element) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        element.SetValue(PressPointProperty, null);
        if (GetTemplateId(element) is not { } id) return;
        var data = new DataObject(IViewportDropTarget.DesignFormat, id);
        DragDrop.DoDragDrop(element, data, DragDropEffects.Copy);
        // The button saw the press but not the release: make sure it doesn't stay pressed (or click later).
        element.ReleaseMouseCapture();
        e.Handled = true;
    }
}
