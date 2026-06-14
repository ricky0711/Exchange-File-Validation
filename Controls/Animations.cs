using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ExchangeFileValidator.Controls;

/// <summary>Single switch for all chrome animations (page transitions, frame blocks, etc.).</summary>
public static class AppAnimations
{
    public static bool Enabled { get; set; } = true;
    public static readonly Duration Fast = new(TimeSpan.FromMilliseconds(180));
}

/// <summary>
/// A <see cref="ContentControl"/> that cross-fades when its content changes — used for page transitions
/// (Feature 3). Chrome only: it never wraps the virtualized data grids' rows.
/// </summary>
public sealed class FadeContentControl : ContentControl
{
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (!AppAnimations.Enabled || newContent is null) return;

        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        var fade = new DoubleAnimation(0, 1, AppAnimations.Fast)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
