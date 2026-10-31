using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MiniLyrics.App;

// One animated value keeps brightness, weight and enlargement in step.
internal static class LyricHighlight
{
    internal const int DurationMilliseconds = 280;
    private static readonly DependencyProperty OverlayProperty = DependencyProperty.RegisterAttached(
        "Overlay", typeof(bool), typeof(LyricHighlight), new PropertyMetadata(false));
    private static readonly DependencyProperty ProgressProperty = DependencyProperty.RegisterAttached(
        "Progress", typeof(double), typeof(LyricHighlight), new PropertyMetadata(0d,
            (owner, args) => Apply((TextBlock)owner, (double)args.NewValue)));

    internal static void Initialize(TextBlock block, bool overlay)
    {
        block.SetValue(OverlayProperty, overlay);
        block.RenderTransformOrigin = new Point(0.5, 0.5);
        block.RenderTransform = new ScaleTransform(1, 1);
        Apply(block, 0);
    }

    internal static void SetActive(TextBlock block, bool active, bool animate)
    {
        double target = active ? 1 : 0;
        if ((double)block.GetAnimationBaseValue(ProgressProperty) == target) return;
        double from = (double)block.GetValue(ProgressProperty);
        block.BeginAnimation(ProgressProperty, null);
        block.SetValue(ProgressProperty, target);
        if (animate)
            Animate(block, ProgressProperty, from, target);
    }

    internal static void SetFontSize(TextBlock block, double target, bool animate)
    {
        if ((double)block.GetAnimationBaseValue(TextBlock.FontSizeProperty) == target) return;
        double from = block.FontSize;
        block.BeginAnimation(TextBlock.FontSizeProperty, null);
        block.FontSize = target;
        if (animate)
            Animate(block, TextBlock.FontSizeProperty, from, target);
    }

    private static void Animate(TextBlock block, DependencyProperty property, double from, double target)
    {
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(DurationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };
        var clock = animation.CreateClock();
        block.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
        // Start at the current appearance immediately, including another seek before the next frame.
        clock.Controller!.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
    }

    private static void Apply(TextBlock block, double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        bool overlay = (bool)block.GetValue(OverlayProperty);
        block.Opacity = overlay ? 0.4 + 0.6 * progress : 1;
        block.FontWeight = FontWeight.FromOpenTypeWeight((int)Math.Round(400 + 300 * progress));
        // Render scaling enlarges the active line without moving neighboring layout slots.
        var scale = (ScaleTransform)block.RenderTransform;
        scale.ScaleX = scale.ScaleY = 1 + 0.06 * progress;
        if (!overlay && block.Foreground is SolidColorBrush brush)
        {
            byte Blend(byte from, byte to) => (byte)Math.Round(from + (to - from) * progress);
            brush.Color = Color.FromRgb(Blend(137, 103), Blend(157, 232), Blend(145, 160));
        }
    }
}
