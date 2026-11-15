using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Fluent11Helper
{
    internal static class SmoothScroll
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const int WheelDelta = 120;

        private const double FollowMs = 50;
        private const int ReleaseMs = 50;
        private const int VelocityWindowMs = 40;
        private const int TrackpadHoldMs = 100;
        private const double FrictionMs = 325;
        private const double MinCoastSpeed = 0.3;
        private const double StopSpeed = 0.02;
        private const double MaxCoastSpeed = 8;

        private const double OverscrollMax = 80;
        private const double OverscrollStiffness = 0.55;
        private const double SpringBackMs = 35;
        private const double BounceFactor = 1.5;
        private const double MaxBounce = 250;
        private const double FadeRatio = 0.35;
        private const double EdgeFrictionMs = 45;

        private static readonly IEasingFunction Easing = CreateEasing();

        private static readonly Type BehavioursType = Type.GetType("Playnite.Behaviors.ScrollViewerBehaviours, Playnite");
        private static readonly Type DataType = Type.GetType("Playnite.Behaviors.ScrollAnimationData, Playnite");

        internal static readonly DependencyProperty ScrollDataProperty = StaticProperty("ScrollDataProperty");
        internal static readonly DependencyProperty VerticalOffsetProperty = StaticProperty("VerticalOffsetProperty");
        internal static readonly DependencyProperty SensitivityProperty = StaticProperty("SensitivityProperty");
        internal static readonly DependencyProperty SmoothEnabledProperty = StaticProperty("SmoothScrollEnabledProperty");
        internal static readonly DependencyProperty SpeedProperty = StaticProperty("SpeedProperty");
        internal static readonly DependencyProperty CustomScrollProperty = StaticProperty("CustomScrollEnabledProperty");
        internal static readonly PropertyInfo AnimationProperty = DataType?.GetProperty("Animation");
        internal static readonly PropertyInfo OwnerProperty = DataType?.GetProperty("ScrollOwner");
        internal static readonly PropertyInfo TargetOffsetProperty = DataType?.GetProperty("TargetOffset");
        internal static readonly PropertyInfo IsAnimatingProperty = DataType?.GetProperty("IsAnimating");

        internal static bool TrackpadSupported => VerticalOffsetProperty != null && SensitivityProperty != null && SmoothEnabledProperty != null
            && OwnerProperty != null && TargetOffsetProperty != null && IsAnimatingProperty != null;

        internal static bool PagesSupported => SensitivityProperty != null && SmoothEnabledProperty != null && SpeedProperty != null
            && CustomScrollProperty != null;

        private class Motion
        {
            public ScrollViewer Scroller;
            public object Data;
            public double Target;
            public double Position;
            public double LastSet;
            public double PreviousSet;
            public double Velocity;
            public bool Coasting;
            public double LastInput;
            public int Shrinking;
            public double Peak;
            public bool Returning;
            public bool Bounced;
            public bool DriverInertia;
            public int CoastTouches;
            public double Over;
            public UIElement Content;
            public Transform OriginalTransform;
            public TranslateTransform Shift;
            public readonly List<KeyValuePair<double, double>> Steps = new List<KeyValuePair<double, double>>();
        }

        private static readonly List<Motion> active = new List<Motion>();
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static double lastRenderTime = -1;
        private static bool rendering;

        private static readonly List<WeakReference> pages = new List<WeakReference>();

        private static DependencyProperty StaticProperty(string name)
        {
            return BehavioursType?.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as DependencyProperty;
        }

        private static Func<bool> enabled = () => true;

        public static void Register(Func<bool> isEnabled)
        {
            enabled = isEnabled;
            if (ScrollDataProperty == null || AnimationProperty == null)
            {
                return;
            }

            EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));
        }

        private static IEasingFunction CreateEasing()
        {
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            easing.Freeze();
            return easing;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || !(sender is ScrollViewer scroller))
            {
                return;
            }

            if (ScrollPopupList(scroller, e))
            {
                e.Handled = true;
                return;
            }

            if (!enabled())
            {
                return;
            }

            try
            {
                var data = scroller.GetValue(ScrollDataProperty) ?? AttachToPage(scroller);
                if (data == null)
                {
                    return;
                }

                var motion = active.FirstOrDefault(a => a.Scroller == scroller);
                var trackpad = e.Delta % WheelDelta != 0 || (motion != null && clock.Elapsed.TotalMilliseconds - motion.LastInput < TrackpadHoldMs);
                if (trackpad && TrackpadSupported && ScrollTrackpad(scroller, data, e.Delta))
                {
                    e.Handled = true;
                    return;
                }

                Stop(scroller);
                if (AnimationProperty.GetValue(data) is DoubleAnimation animation && animation.EasingFunction == null && !animation.IsFrozen)
                {
                    animation.EasingFunction = Easing;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Fluent 11: smooth scrolling not adjusted.");
            }
        }

        private static bool ScrollPopupList(ScrollViewer scroller, MouseWheelEventArgs e)
        {
            ScrollViewer list = null;
            var element = e.OriginalSource as DependencyObject;
            while (element != null)
            {
                if (element == scroller)
                {
                    return false;
                }

                if (list == null && element is ScrollViewer viewer)
                {
                    list = viewer;
                }

                element = element is Visual || element is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }

            if (list == null)
            {
                return false;
            }

            var lines = SystemParameters.WheelScrollLines;
            var step = lines > 0 ? (list.CanContentScroll ? lines : lines * 16) : list.ViewportHeight;
            list.ScrollToVerticalOffset(list.VerticalOffset - e.Delta / (double)WheelDelta * step);
            return true;
        }

        private static bool ScrollTrackpad(ScrollViewer scroller, object data, int delta)
        {
            if (!(OwnerProperty.GetValue(data) is FrameworkElement owner) || !(owner.GetValue(SmoothEnabledProperty) is bool smooth) || !smooth
                || scroller.ComputedVerticalScrollBarVisibility != Visibility.Visible)
            {
                return false;
            }

            if (true.Equals(IsAnimatingProperty.GetValue(data)))
            {
                scroller.SetValue(VerticalOffsetProperty, scroller.VerticalOffset);
                scroller.BeginAnimation(VerticalOffsetProperty, null);
            }

            TouchpadContacts.EnsureRegistered();
            var now = clock.Elapsed.TotalMilliseconds;
            var motion = active.FirstOrDefault(a => a.Scroller == scroller);
            if (motion == null)
            {
                motion = new Motion { Scroller = scroller, Data = data, Position = scroller.VerticalOffset, LastSet = scroller.VerticalOffset,
                    PreviousSet = scroller.VerticalOffset };
                motion.Target = motion.Position;
                active.Add(motion);
            }
            else if (motion.Coasting)
            {
                StopCoasting(motion);
            }

            var sensitivity = owner.GetValue(SensitivityProperty) is double value ? value : 1;
            var step = -delta * sensitivity;
            var fingers = TouchpadContacts.Known;
            var fingerDown = fingers && TouchpadContacts.Down;
            if (fingerDown || (!fingers && now - motion.LastInput > ReleaseMs))
            {
                motion.Returning = false;
                motion.Bounced = false;
                motion.DriverInertia = false;
                motion.Shrinking = 0;
                motion.Peak = 0;
            }

            motion.Peak = Math.Max(motion.Peak, Math.Abs(step));
            var outward = (motion.Over < 0 && step < 0) || (motion.Over > 0 && step > 0);
            motion.Shrinking = outward && Math.Abs(step) < motion.Peak * FadeRatio ? motion.Shrinking + 1 : 0;
            if (fingers ? !fingerDown : motion.Shrinking >= 2)
            {
                motion.Returning = true;
            }

            if (fingers && !fingerDown)
            {
                motion.DriverInertia = true;
            }

            var pushingOut = (step < 0 && motion.Target <= 0) || (step > 0 && motion.Target >= scroller.ScrollableHeight);
            if (motion.Returning && pushingOut)
            {
                if (fingers && !motion.Bounced)
                {
                    motion.Over = Math.Sign(step) * Math.Min(MaxBounce, Math.Abs(motion.Over) + Math.Abs(step) * BounceFactor);
                    motion.Bounced = true;
                }
            }
            else
            {
                var wanted = motion.Target + motion.Over + step;
                motion.Target = Clamp(scroller, wanted);
                motion.Over = wanted - motion.Target;
                if (motion.Returning && motion.Over != 0)
                {
                    motion.Bounced = true;
                }
            }

            motion.LastInput = now;
            motion.Steps.Add(new KeyValuePair<double, double>(now, step));
            motion.Steps.RemoveAll(a => now - a.Key > VelocityWindowMs);
            TargetOffsetProperty.SetValue(data, motion.Target);
            StartRendering();
            return true;
        }

        private static double Clamp(ScrollViewer scroller, double offset)
        {
            return Math.Max(0, Math.Min(scroller.ScrollableHeight, offset));
        }

        private static void StopCoasting(Motion motion)
        {
            motion.Coasting = false;
            motion.Target = Clamp(motion.Scroller, motion.Position);
            motion.Over = motion.Position - motion.Target;
        }

        private static void Stop(ScrollViewer scroller)
        {
            foreach (var motion in active.Where(a => a.Scroller == scroller).ToList())
            {
                End(motion);
            }
        }

        private static void End(Motion motion)
        {
            if (motion.Shift != null && motion.Content != null)
            {
                motion.Content.RenderTransform = motion.OriginalTransform;
                motion.Shift = null;
            }

            active.Remove(motion);
        }

        private static void ApplyOverscroll(Motion motion, double over)
        {
            var shift = Math.Abs(over) < 0.5 ? 0 : Shift(over);
            if (shift == 0 && motion.Shift == null)
            {
                return;
            }

            if (motion.Shift == null)
            {
                motion.Content = motion.Content ?? FindContent(motion.Scroller);
                var current = motion.Content?.RenderTransform;
                if (motion.Content == null || (current != null && current != Transform.Identity && !current.Value.IsIdentity))
                {
                    motion.Over = 0;
                    motion.Position = motion.Target;
                    return;
                }

                motion.OriginalTransform = current;
                motion.Shift = new TranslateTransform();
                motion.Content.RenderTransform = motion.Shift;
            }

            motion.Shift.Y = over < 0 ? shift : -shift;
        }

        private static double Shift(double over)
        {
            return (1 - 1 / (Math.Abs(over) * OverscrollStiffness / OverscrollMax + 1)) * OverscrollMax;
        }

        private static UIElement FindContent(ScrollViewer scroller)
        {
            var presenter = scroller.Template?.FindName("PART_ScrollContentPresenter", scroller) as ScrollContentPresenter ?? FindPresenter(scroller, scroller);
            return presenter != null && VisualTreeHelper.GetChildrenCount(presenter) > 0 ? VisualTreeHelper.GetChild(presenter, 0) as UIElement : null;
        }

        private static ScrollContentPresenter FindPresenter(DependencyObject parent, ScrollViewer scroller)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollContentPresenter presenter && presenter.TemplatedParent == scroller)
                {
                    return presenter;
                }

                if (!(child is ScrollViewer) && !(child is ContentPresenter) && FindPresenter(child, scroller) is ScrollContentPresenter found)
                {
                    return found;
                }
            }

            return null;
        }

        internal static void AttachPage(FrameworkElement root)
        {
            if (root != null && PagesSupported)
            {
                pages.RemoveAll(a => !a.IsAlive);
                pages.Add(new WeakReference(root));
            }
        }

        private static object AttachToPage(ScrollViewer scroller)
        {
            if (pages.Count == 0 || scroller.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled || !IsInPage(scroller))
            {
                return null;
            }

            var list = scroller.TemplatedParent is ListBox;
            var options = Reflect.GetMember(PlayniteInternals.AppSettings, list ? "DetailsViewListScrollOptions" : "DetailsViewDetailsScrollOptions");
            if (options == null)
            {
                return null;
            }

            scroller.SetValue(SensitivityProperty, Reflect.GetMember(options, "Sensitivity") is double sensitivity ? sensitivity : 1.0);
            scroller.SetValue(SmoothEnabledProperty, Reflect.GetMember(options, "SmoothEnabled") is bool smooth && smooth);
            scroller.SetValue(SpeedProperty, TimeSpan.FromMilliseconds(Reflect.GetMember(options, "Speed") is long speed ? speed : 250));
            scroller.SetValue(CustomScrollProperty, true);
            return scroller.GetValue(ScrollDataProperty);
        }

        private static bool IsInPage(DependencyObject element)
        {
            for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (pages.Any(a => a.Target == current))
                {
                    return true;
                }
            }

            return false;
        }

        private static void StartRendering()
        {
            if (!rendering)
            {
                rendering = true;
                lastRenderTime = -1;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        private static void OnRendering(object sender, EventArgs e)
        {
            var frameTime = (e as RenderingEventArgs)?.RenderingTime.TotalMilliseconds ?? clock.Elapsed.TotalMilliseconds;
            if (frameTime == lastRenderTime)
            {
                return;
            }

            var dt = lastRenderTime < 0 ? 16.7 : Math.Max(1, Math.Min(50, frameTime - lastRenderTime));
            lastRenderTime = frameTime;
            var now = clock.Elapsed.TotalMilliseconds;

            foreach (var motion in active.ToList())
            {
                try
                {
                    Advance(motion, dt, now);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Fluent 11: smooth scrolling stopped.");
                    try
                    {
                        End(motion);
                    }
                    catch
                    {
                        active.Remove(motion);
                    }
                }
            }

            if (active.Count == 0)
            {
                rendering = false;
                CompositionTarget.Rendering -= OnRendering;
            }
        }

        private static void Advance(Motion motion, double dt, double now)
        {
            var scroller = motion.Scroller;
            var current = scroller.VerticalOffset;
            if (!scroller.IsLoaded || (Math.Abs(current - motion.LastSet) > 2 && Math.Abs(current - motion.PreviousSet) > 2))
            {
                End(motion);
                return;
            }

            var released = now - motion.LastInput > ReleaseMs;
            var fingers = TouchpadContacts.Known;
            var fingerDown = fingers && TouchpadContacts.Down;
            if (motion.Coasting && TouchpadContacts.Touches != motion.CoastTouches)
            {
                StopCoasting(motion);
            }

            if (!motion.Coasting && motion.Steps.Count > 0 && released && !fingerDown)
            {
                var span = Math.Max(16, motion.LastInput - motion.Steps[0].Key + 16);
                var speed = motion.Steps.Sum(a => a.Value) / span;
                motion.Steps.Clear();
                if (Math.Abs(speed) > MinCoastSpeed && Math.Abs(motion.Over) < 0.5 && !motion.DriverInertia && !motion.Bounced && !motion.Returning)
                {
                    motion.Coasting = true;
                    motion.CoastTouches = TouchpadContacts.Touches;
                    motion.Velocity = Math.Sign(speed) * Math.Min(MaxCoastSpeed, Math.Abs(speed));
                }
            }
            else if (fingerDown && released)
            {
                motion.Steps.Clear();
            }

            if (motion.Coasting)
            {
                var wanted = motion.Target + motion.Over + motion.Velocity * dt;
                motion.Target = Clamp(scroller, wanted);
                motion.Over = wanted - motion.Target;
                motion.Velocity *= Math.Exp(-dt / (Math.Abs(motion.Over) > 0 ? EdgeFrictionMs : FrictionMs));
                if (Math.Abs(motion.Velocity) < StopSpeed)
                {
                    motion.Coasting = false;
                }

                TargetOffsetProperty.SetValue(motion.Data, motion.Target);
            }
            else if (motion.Over != 0 && (fingers ? !fingerDown : released || motion.Returning))
            {
                var shift = Shift(motion.Over) * Math.Exp(-dt / SpringBackMs);
                motion.Over = shift < 0.5 ? 0 : Math.Sign(motion.Over) * OverscrollMax / OverscrollStiffness * shift / (OverscrollMax - shift);
            }

            var goal = motion.Target + motion.Over;
            motion.Position += (goal - motion.Position) * (1 - Math.Exp(-dt / FollowMs));
            var idle = !motion.Coasting && released && motion.Steps.Count == 0 && motion.Over == 0;
            if (idle && Math.Abs(goal - motion.Position) < 0.5)
            {
                motion.Position = goal;
            }

            var offset = Clamp(scroller, motion.Position);
            ApplyOverscroll(motion, motion.Position - offset);
            if (idle && motion.Position == goal)
            {
                scroller.ScrollToVerticalOffset(offset);
                End(motion);
                return;
            }

            motion.PreviousSet = motion.LastSet;
            motion.LastSet = offset;
            scroller.ScrollToVerticalOffset(offset);
        }
    }
}
