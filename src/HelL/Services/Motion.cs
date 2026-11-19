using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace HelL.Services;

public static class Motion
{
	private static bool _enabled = true;

	private static readonly ConditionalWeakTable<object, Dictionary<DependencyProperty, AnimationTimeline>> _latest = new ConditionalWeakTable<object, Dictionary<DependencyProperty, AnimationTimeline>>();

	private static readonly CubicEase EaseOut = new CubicEase
	{
		EasingMode = EasingMode.EaseOut
	};

	private static readonly CubicEase EaseIn = new CubicEase
	{
		EasingMode = EasingMode.EaseIn
	};

	private static readonly QuadraticEase EaseInOut = new QuadraticEase
	{
		EasingMode = EasingMode.EaseInOut
	};

	private static readonly BackEase EaseBack = new BackEase
	{
		EasingMode = EasingMode.EaseOut,
		Amplitude = 0.4
	};

	private static int _swapGen;

	public static double Scale { get; set; } = 1.0;

	public static bool Enabled
	{
		get
		{
			if (_enabled && SystemParameters.ClientAreaAnimation)
			{
				return !SystemParameters.IsRemoteSession;
			}
			return false;
		}
		set
		{
			if (_enabled != value)
			{
				_enabled = value;
				Motion.EnabledChanged?.Invoke();
			}
		}
	}

	public static bool UserEnabled => _enabled;

	public static event Action? EnabledChanged;

	private static void Play(IAnimatable target, DependencyProperty prop, AnimationTimeline an, double final, Action? done = null)
	{
		Dictionary<DependencyProperty, AnimationTimeline> map = _latest.GetOrCreateValue(target);
		map[prop] = an;
		an.Completed += delegate
		{
			if (map.TryGetValue(prop, out AnimationTimeline value) && value == an)
			{
				map.Remove(prop);
				target.BeginAnimation(prop, null);
				if (target is DependencyObject dependencyObject)
				{
					dependencyObject.SetValue(prop, final);
				}
				done?.Invoke();
			}
		};
		target.BeginAnimation(prop, an);
	}

	private static void Take(IAnimatable target, DependencyProperty prop, AnimationTimeline? an)
	{
		Dictionary<DependencyProperty, AnimationTimeline> orCreateValue = _latest.GetOrCreateValue(target);
		if (an == null)
		{
			orCreateValue.Remove(prop);
		}
		else
		{
			orCreateValue[prop] = an;
		}
		target.BeginAnimation(prop, an);
	}

	public static void Loop(FrameworkElement owner, Action start, Action stop)
	{
		bool running = false;
		owner.Loaded += delegate
		{
			Sync();
		};
		owner.Unloaded += delegate
		{
			Sync();
		};
		owner.IsVisibleChanged += delegate
		{
			Sync();
		};
		Action onToggle = delegate
		{
			owner.Dispatcher.BeginInvoke(new Action(Sync));
		};
		owner.Loaded += delegate
		{
			EnabledChanged += onToggle;
		};
		owner.Unloaded += delegate
		{
			EnabledChanged -= onToggle;
		};
		if (owner.IsLoaded)
		{
			EnabledChanged += onToggle;
		}
		Sync();
		void Sync()
		{
			bool flag = Enabled && owner.IsLoaded && owner.IsVisible;
			if (flag != running)
			{
				running = flag;
				if (flag)
				{
					start();
				}
				else
				{
					stop();
				}
			}
		}
	}

	private static Duration D(double ms)
	{
		return TimeSpan.FromMilliseconds(Math.Max(1.0, ms * Scale));
	}

	private static TranslateTransform EnsureTranslate(UIElement el)
	{
		if (el.RenderTransform is TransformGroup transformGroup)
		{
			return transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault() ?? Add(transformGroup, new TranslateTransform());
		}
		if (el.RenderTransform is TranslateTransform result)
		{
			return result;
		}
		TransformGroup transformGroup2 = new TransformGroup();
		Transform renderTransform = el.RenderTransform;
		if (renderTransform != null && !(renderTransform is MatrixTransform { Matrix: { IsIdentity: not false } }))
		{
			transformGroup2.Children.Add(renderTransform);
		}
		TranslateTransform translateTransform = new TranslateTransform();
		transformGroup2.Children.Add(translateTransform);
		el.RenderTransform = transformGroup2;
		return translateTransform;
	}

	private static ScaleTransform EnsureScale(UIElement el)
	{
		el.RenderTransformOrigin = new Point(0.5, 0.5);
		if (el.RenderTransform is TransformGroup transformGroup)
		{
			return transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault() ?? Add(transformGroup, new ScaleTransform(1.0, 1.0));
		}
		if (el.RenderTransform is ScaleTransform result)
		{
			return result;
		}
		TransformGroup transformGroup2 = new TransformGroup();
		Transform renderTransform = el.RenderTransform;
		if (renderTransform != null && !(renderTransform is MatrixTransform { Matrix: { IsIdentity: not false } }))
		{
			transformGroup2.Children.Add(renderTransform);
		}
		ScaleTransform scaleTransform = new ScaleTransform(1.0, 1.0);
		transformGroup2.Children.Add(scaleTransform);
		el.RenderTransform = transformGroup2;
		return scaleTransform;
	}

	private static T Add<T>(TransformGroup g, T t) where T : Transform
	{
		g.Children.Add(t);
		return t;
	}

	public static void FadeIn(UIElement el, double ms = 220.0, double fromY = 10.0, double delayMs = 0.0)
	{
		if (!Enabled)
		{
			el.Opacity = 1.0;
			return;
		}
		TranslateTransform translateTransform = EnsureTranslate(el);
		el.Opacity = 0.0;
		translateTransform.Y = fromY;
		TimeSpan value = TimeSpan.FromMilliseconds(delayMs * Scale);
		Play(el, UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, D(ms))
		{
			BeginTime = value,
			EasingFunction = EaseOut
		}, 1.0);
		Play(translateTransform, TranslateTransform.YProperty, new DoubleAnimation(fromY, 0.0, D(ms + 60.0))
		{
			BeginTime = value,
			EasingFunction = EaseOut
		}, 0.0);
	}

	public static void FadeOut(UIElement el, double ms = 140.0, Action? then = null)
	{
		if (!Enabled)
		{
			el.Opacity = 0.0;
			then?.Invoke();
		}
		else
		{
			Play(el, UIElement.OpacityProperty, new DoubleAnimation(el.Opacity, 0.0, D(ms))
			{
				EasingFunction = EaseIn
			}, 0.0, then);
		}
	}

	public static void Stagger(Panel host, double step = 26.0, double ms = 210.0, double fromY = 12.0)
	{
		if (!Enabled)
		{
			return;
		}
		int num = 0;
		foreach (UIElement child in host.Children)
		{
			if (num >= 16)
			{
				Take(child, UIElement.OpacityProperty, null);
				child.Opacity = 1.0;
				num++;
			}
			else
			{
				FadeIn(child, ms, fromY, Math.Min((double)num * step, 320.0));
				num++;
			}
		}
	}

	public static void StaggerItems(IEnumerable<UIElement> items, double step = 24.0, double ms = 200.0, double fromY = 10.0)
	{
		if (!Enabled)
		{
			return;
		}
		int num = 0;
		foreach (UIElement item in items)
		{
			if (num >= 16)
			{
				Take(item, UIElement.OpacityProperty, null);
				item.Opacity = 1.0;
				num++;
			}
			else
			{
				FadeIn(item, ms, fromY, Math.Min((double)num * step, 300.0));
				num++;
			}
		}
	}

	public static void HoverLift(FrameworkElement el, double scale = 1.012, double lift = 2.0, double blur = 22.0)
	{
		bool flag = scale > 1.0;
		ScaleTransform sc = (flag ? EnsureScale(el) : null);
		TranslateTransform tt = EnsureTranslate(el);
		DropShadowEffect shadow = null;
		bool hovering = false;
		el.MouseEnter += delegate
		{
			if (Enabled)
			{
				hovering = true;
				if (el.Effect == null || el.Effect == shadow)
				{
					if (shadow == null)
					{
						shadow = new DropShadowEffect
						{
							BlurRadius = blur,
							ShadowDepth = 3.0,
							Direction = 270.0,
							Opacity = 0.0,
							Color = Color.FromRgb(24, 24, 24),
							RenderingBias = RenderingBias.Performance
						};
					}
					el.Effect = shadow;
					Take(shadow, DropShadowEffect.OpacityProperty, new DoubleAnimation(0.16, D(170.0)));
				}
				if (sc != null)
				{
					Take(sc, ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, D(170.0))
					{
						EasingFunction = EaseOut
					});
					Take(sc, ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, D(170.0))
					{
						EasingFunction = EaseOut
					});
				}
				Take(tt, TranslateTransform.YProperty, new DoubleAnimation(0.0 - lift, D(170.0))
				{
					EasingFunction = EaseOut
				});
			}
		};
		el.MouseLeave += delegate
		{
			hovering = false;
			if (sc != null)
			{
				Play(sc, ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, D(190.0))
				{
					EasingFunction = EaseOut
				}, 1.0);
				Play(sc, ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, D(190.0))
				{
					EasingFunction = EaseOut
				}, 1.0);
			}
			Play(tt, TranslateTransform.YProperty, new DoubleAnimation(0.0, D(190.0))
			{
				EasingFunction = EaseOut
			}, 0.0);
			if (shadow != null && el.Effect == shadow)
			{
				Play(shadow, DropShadowEffect.OpacityProperty, new DoubleAnimation(0.0, D(190.0)), 0.0, delegate
				{
					if (!hovering && el.Effect == shadow)
					{
						el.Effect = null;
					}
				});
			}
		};
	}

	public static T Shadow<T>(T el, double blur = 18.0, double opacity = 0.08, double depth = 2.0) where T : UIElement
	{
		el.Effect = new DropShadowEffect
		{
			BlurRadius = blur,
			ShadowDepth = depth,
			Direction = 270.0,
			Opacity = opacity,
			Color = Color.FromRgb(20, 20, 20),
			RenderingBias = RenderingBias.Performance
		};
		return el;
	}

	public static void Pulse(UIElement el, double peak = 1.06)
	{
		if (Enabled)
		{
			ScaleTransform target = EnsureScale(el);
			DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
			{
				Duration = D(280.0)
			};
			doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromPercent(0.35), EaseOut));
			doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), EaseBack));
			Play(target, ScaleTransform.ScaleXProperty, doubleAnimationUsingKeyFrames, 1.0);
			Play(target, ScaleTransform.ScaleYProperty, doubleAnimationUsingKeyFrames.Clone(), 1.0);
		}
	}

	public static void Shake(UIElement el)
	{
		if (Enabled)
		{
			TranslateTransform target = EnsureTranslate(el);
			DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
			{
				Duration = D(330.0)
			};
			double[] array = new double[6] { -7.0, 6.0, -4.0, 3.0, -1.5, 0.0 };
			for (int i = 0; i < array.Length; i++)
			{
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(array[i], KeyTime.FromPercent(((double)i + 1.0) / (double)array.Length), EaseOut));
			}
			Play(target, TranslateTransform.XProperty, doubleAnimationUsingKeyFrames, 0.0);
		}
	}

	public static void Shine(FrameworkElement layer, TranslateTransform tx, double width = 1200.0)
	{
		layer.RenderTransform = tx;
		layer.Opacity = 0.0;
		DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromSeconds(7.0)
		};
		timer.Tick += delegate
		{
			Sweep();
		};
		Loop(layer, delegate
		{
			timer.Start();
			layer.Dispatcher.BeginInvoke(new Action(Sweep), DispatcherPriority.Background);
		}, delegate
		{
			timer.Stop();
			Take(tx, TranslateTransform.XProperty, null);
			Take(layer, UIElement.OpacityProperty, null);
			layer.Opacity = 0.0;
		});
		void Sweep()
		{
			Window window = Window.GetWindow(layer);
			if (window != null && window.WindowState != WindowState.Minimized && window.IsActive && !Dlg.AnyOpen)
			{
				double toValue = ((layer.Parent is FrameworkElement { ActualWidth: >0.0 } frameworkElement) ? (frameworkElement.ActualWidth + 40.0) : width);
				Play(tx, TranslateTransform.XProperty, new DoubleAnimation(0.0 - layer.ActualWidth - 40.0, toValue, D(1600.0))
				{
					EasingFunction = EaseInOut
				}, 0.0 - layer.ActualWidth - 40.0);
				DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
				{
					Duration = D(1600.0)
				};
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromPercent(0.25)));
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromPercent(0.65)));
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
				Play(layer, UIElement.OpacityProperty, doubleAnimationUsingKeyFrames, 0.0);
			}
		}
	}

	public static void Spin(FrameworkElement owner, RotateTransform rot, double seconds = 1.0)
	{
		Loop(owner, delegate
		{
			rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0.0, 360.0, TimeSpan.FromSeconds(seconds))
			{
				RepeatBehavior = RepeatBehavior.Forever
			});
		}, delegate
		{
			rot.BeginAnimation(RotateTransform.AngleProperty, null);
		});
	}

	public static void Progress(ProgressBar bar, double value)
	{
		value = Clamp.Of(value, 0.0, 100.0);
		if (!Enabled)
		{
			bar.BeginAnimation(RangeBase.ValueProperty, null);
			bar.Value = value;
		}
		else
		{
			bar.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(value, D((Math.Abs(value - bar.Value) > 40.0) ? 320 : 200))
			{
				EasingFunction = EaseOut
			});
		}
	}

	public static void CountUp(TextBlock tb, double from, double to, string fmt = "0", double ms = 620.0, Action? done = null)
	{
		if (!Enabled)
		{
			tb.Text = to.ToString(fmt);
			done?.Invoke();
			return;
		}
		Stopwatch sw = Stopwatch.StartNew();
		double total = Math.Max(1.0, ms * Scale);
		EventHandler tick = null;
		tick = delegate
		{
			double num = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / total);
			double num2 = 1.0 - Math.Pow(1.0 - num, 3.0);
			tb.Text = (from + (to - from) * num2).ToString(fmt);
			if (num >= 1.0 || (!tb.IsLoaded && tb.Parent == null))
			{
				CompositionTarget.Rendering -= tick;
				if (num >= 1.0)
				{
					done?.Invoke();
				}
			}
		};
		CompositionTarget.Rendering += tick;
	}

	public static void AnimateWidth(FrameworkElement el, double to, double ms = 220.0, Action? done = null)
	{
		AnimateLength(el, FrameworkElement.WidthProperty, double.IsNaN(el.Width) ? el.ActualWidth : el.Width, to, ms, done);
	}

	public static void AnimateHeight(FrameworkElement el, double to, double ms = 220.0, Action? done = null)
	{
		AnimateLength(el, FrameworkElement.HeightProperty, double.IsNaN(el.Height) ? el.ActualHeight : el.Height, to, ms, done);
	}

	private static void AnimateLength(FrameworkElement el, DependencyProperty prop, double from, double to, double ms, Action? done)
	{
		if (!Enabled)
		{
			el.BeginAnimation(prop, null);
			el.SetValue(prop, to);
			done?.Invoke();
			return;
		}
		DoubleAnimation doubleAnimation = new DoubleAnimation(from, to, D(ms))
		{
			EasingFunction = EaseOut
		};
		doubleAnimation.Completed += delegate
		{
			el.BeginAnimation(prop, null);
			el.SetValue(prop, to);
			done?.Invoke();
		};
		el.BeginAnimation(prop, doubleAnimation);
	}

	public static void Fade(UIElement el, double to, double ms = 160.0, Action? done = null)
	{
		if (!Enabled)
		{
			el.Opacity = to;
			done?.Invoke();
		}
		else
		{
			Play(el, UIElement.OpacityProperty, new DoubleAnimation(to, D(ms))
			{
				EasingFunction = EaseOut
			}, to, done);
		}
	}

	public static void PopIn(FrameworkElement el, double ms = 200.0)
	{
		if (!Enabled)
		{
			el.Opacity = 1.0;
			return;
		}
		ScaleTransform scaleTransform = EnsureScale(el);
		el.Opacity = 0.0;
		double num = (scaleTransform.ScaleY = 0.96);
		double scaleX = num;
		scaleTransform.ScaleX = scaleX;
		Play(el, UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, D(ms))
		{
			EasingFunction = EaseOut
		}, 1.0);
		Play(scaleTransform, ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1.0, D(ms + 60.0))
		{
			EasingFunction = EaseOut
		}, 1.0);
		Play(scaleTransform, ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1.0, D(ms + 60.0))
		{
			EasingFunction = EaseOut
		}, 1.0);
	}

	public static void PopOut(FrameworkElement el, Action done, double ms = 130.0)
	{
		if (!Enabled)
		{
			done();
			return;
		}
		ScaleTransform target = EnsureScale(el);
		Play(target, ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, D(ms))
		{
			EasingFunction = EaseIn
		}, 1.0);
		Play(target, ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, D(ms))
		{
			EasingFunction = EaseIn
		}, 1.0);
		Play(el, UIElement.OpacityProperty, new DoubleAnimation(0.0, D(ms))
		{
			EasingFunction = EaseIn
		}, 0.0, done);
	}

	public static void SlideIn(FrameworkElement el, double fromX = 40.0, double ms = 240.0)
	{
		if (!Enabled)
		{
			el.Opacity = 1.0;
			return;
		}
		TranslateTransform translateTransform = EnsureTranslate(el);
		el.Opacity = 0.0;
		translateTransform.X = fromX;
		Play(el, UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, D(ms))
		{
			EasingFunction = EaseOut
		}, 1.0);
		Play(translateTransform, TranslateTransform.XProperty, new DoubleAnimation(fromX, 0.0, D(ms + 60.0))
		{
			EasingFunction = EaseOut
		}, 0.0);
	}

	public static void SlideOut(FrameworkElement el, Action done, double toX = 40.0, double ms = 180.0)
	{
		if (!Enabled)
		{
			done();
			return;
		}
		Play(EnsureTranslate(el), TranslateTransform.XProperty, new DoubleAnimation(toX, D(ms))
		{
			EasingFunction = EaseIn
		}, 0.0);
		Play(el, UIElement.OpacityProperty, new DoubleAnimation(0.0, D(ms))
		{
			EasingFunction = EaseIn
		}, 0.0, done);
	}

	public static void PageSwap(ContentControl host, UIElement next, bool forward = true)
	{
		int gen = ++_swapGen;
		UIElement old = host.Content as UIElement;
		if (!Enabled || old == null || old == next)
		{
			if (old != null && old != next)
			{
				Reset(old);
			}
			host.Content = next;
			if (Enabled)
			{
				FadeIn(next, 230.0, 12.0);
			}
			else
			{
				Reset(next);
			}
			return;
		}
		Take(EnsureTranslate(old), TranslateTransform.YProperty, new DoubleAnimation(forward ? (-8) : 8, D(110.0))
		{
			EasingFunction = EaseIn
		});
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, D(110.0))
		{
			EasingFunction = EaseIn
		};
		doubleAnimation.Completed += delegate
		{
			Reset(old);
			if (gen == _swapGen)
			{
				host.Content = next;
				FadeIn(next, 250.0, forward ? 14 : (-14));
			}
		};
		Take(old, UIElement.OpacityProperty, doubleAnimation);
	}

	private static void Reset(UIElement el)
	{
		Take(el, UIElement.OpacityProperty, null);
		el.Opacity = 1.0;
		if (!(el.RenderTransform is TransformGroup transformGroup))
		{
			return;
		}
		foreach (TranslateTransform item in transformGroup.Children.OfType<TranslateTransform>())
		{
			Take(item, TranslateTransform.XProperty, null);
			Take(item, TranslateTransform.YProperty, null);
			double num = (item.Y = 0.0);
			double x = num;
			item.X = x;
		}
	}
}
