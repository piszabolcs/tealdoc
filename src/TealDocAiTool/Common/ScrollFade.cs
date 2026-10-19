using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TealDocAiTool.Common;

/// <summary>
/// Lágy görgetési szélek: a <c>ScrollFade.IsEnabled="True"</c> egy ScrollViewer-en, vagy olyan vezérlőn, amelynek
/// sablonjában ScrollViewer van (TextBox, ListBox, DataGrid), a tartalmat a felső / alsó szélen
/// <see cref="FadeHeight"/> magasságban elhalványítja — de csak amíg arra még rejtett tartalom van. A maszk a
/// ScrollContentPresenter-en ül, így maga a görgetősáv sosem halványul.
/// </summary>
public static class ScrollFade
{
    /// <summary>Egy halványuló sáv magassága (DIP).</summary>
    public const double FadeHeight = 26;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ScrollFade), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(FadeState), typeof(ScrollFade));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        if ((bool)e.NewValue)
        {
            // A sablon (és vele a ScrollViewer) lehet, hogy még nem létezik. A SizeChanged képernyőn kívüli
            // elrendezésnél is megérkezik, a Loaded ott soha.
            element.Loaded += OnElementReady;
            element.SizeChanged += OnElementReady;
            Attach(element);
        }
        else
        {
            element.Loaded -= OnElementReady;
            element.SizeChanged -= OnElementReady;
            (element.GetValue(StateProperty) as FadeState)?.Detach();
            element.ClearValue(StateProperty);
        }
    }

    private static void OnElementReady(object sender, RoutedEventArgs e) => Attach((FrameworkElement)sender);

    private static void Attach(FrameworkElement element)
    {
        if (element.GetValue(StateProperty) is FadeState existing)
        {
            existing.Update();
            return;
        }
        var viewer = element as ScrollViewer ?? FindDescendant<ScrollViewer>(element);
        if (viewer is null)
            return;
        var state = new FadeState(viewer);
        element.SetValue(StateProperty, state);
        state.Update();
    }

    /// <summary>Szélességi bejárás: a vezérlő saját ScrollViewer-e előbb kerül elő, mint egy beágyazott.</summary>
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T match)
                    return match;
                queue.Enqueue(child);
            }
        }
        return null;
    }

    private sealed class FadeState
    {
        private readonly ScrollViewer _viewer;
        private readonly GradientStop _top = new(Colors.Black, 0);
        private readonly GradientStop _topInner = new(Colors.Black, 0);
        private readonly GradientStop _bottomInner = new(Colors.Black, 1);
        private readonly GradientStop _bottom = new(Colors.Black, 1);
        private readonly LinearGradientBrush _mask;
        private ScrollContentPresenter? _presenter;

        public FadeState(ScrollViewer viewer)
        {
            _viewer = viewer;
            _mask = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                GradientStops = { _top, _topInner, _bottomInner, _bottom },
            };
            viewer.ScrollChanged += OnScrollChanged;
        }

        public void Detach()
        {
            _viewer.ScrollChanged -= OnScrollChanged;
            if (_presenter is null)
                return;
            _presenter.SizeChanged -= OnPresenterSizeChanged;
            _presenter.ClearValue(UIElement.OpacityMaskProperty);
        }

        // A ScrollChanged felbuborékol: a beágyazott görgetők (pl. a lapon lévő napló) eseményeit kihagyjuk.
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, _viewer))
                Update();
        }

        private void OnPresenterSizeChanged(object sender, SizeChangedEventArgs e) => Update();

        public void Update()
        {
            if (_presenter is null)
            {
                _presenter = _viewer.Template?.FindName("PART_ScrollContentPresenter", _viewer) as ScrollContentPresenter
                             ?? FindDescendant<ScrollContentPresenter>(_viewer);
                if (_presenter is null)
                    return; // a sablon még nincs alkalmazva; a következő elrendezés újra próbálja
                _presenter.SizeChanged += OnPresenterSizeChanged;
            }

            double height = _presenter.ActualHeight;
            double top = Strength(_viewer.VerticalOffset);
            double bottom = Strength(_viewer.ExtentHeight - _viewer.ViewportHeight - _viewer.VerticalOffset);
            if (height <= 0 || (top == 0 && bottom == 0))
            {
                _presenter.ClearValue(UIElement.OpacityMaskProperty);
                return;
            }

            double band = Math.Min(FadeHeight, height * 0.25) / height;
            _mask.EndPoint = new Point(0, height);
            _top.Color = Black(1 - top);
            _topInner.Offset = band;
            _bottomInner.Offset = 1 - band;
            _bottom.Color = Black(1 - bottom);
            if (!ReferenceEquals(_presenter.OpacityMask, _mask))
                _presenter.OpacityMask = _mask;
        }

        /// <summary>0 = arra nincs rejtett tartalom, 1 = legalább egy teljes sávnyi rejtett tartalom van.</summary>
        private double Strength(double hidden)
        {
            if (hidden < 0.5)
                return 0;
            // Elemenkénti görgetésnél (virtualizált listák) az eltolás elemszám, nem DIP: rögtön teljes halványítás.
            bool inPixels = !_viewer.CanContentScroll || _viewer.TemplatedParent is TextBoxBase;
            return inPixels ? Math.Min(1, hidden / FadeHeight) : 1;
        }

        private static Color Black(double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), 0, 0, 0);
    }
}
