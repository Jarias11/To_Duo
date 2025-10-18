using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TaskMate.Services {
	public interface IAnimationService {
		bool Enabled { get; set; }
		void Confetti(Window? window = null, int pieces = 80, TimeSpan? duration = null);
	}

	/// <summary>
	/// Lightweight, dependency-free WPF particle effects.
	/// Renders onto a top-level Canvas named "EffectsLayer" if present,
	/// otherwise creates a temporary overlay Canvas.
	/// </summary>
	// AnimationService.cs


public sealed class AnimationService : IAnimationService {
    private readonly ISettingsService _settings;
    private readonly Random _rng = new();

    public AnimationService(ISettingsService settings) { _settings = settings; }

    public bool Enabled {
        get => _settings.AnimationsEnabled;
        set { _settings.AnimationsEnabled = value; _settings.Save(); }
    }

    // ---- particle state + loop ----
    private sealed class Particle {
        public Shape Shape = null!;
        public RotateTransform Rot = null!;
        public double X, Y;
        public double VX, VY;
        public double W;        // angular velocity (deg/s)
        public double Age;      // seconds
        public double Life;     // seconds
    }

    private readonly List<Particle> _live = new();
    private Canvas? _layer;
    private bool _loopHooked;
    private TimeSpan _lastTick;

    public void Confetti(Window? window = null, int pieces = 80, TimeSpan? duration = null) {
        if (!Enabled) return;

        var hostWin = window ?? Application.Current?.MainWindow;
        if (hostWin == null || !hostWin.IsLoaded) return;

        _layer ??= FindOrCreateEffectsLayer(hostWin);
        if (_layer == null) return;

        var w = hostWin.ActualWidth;
        var h = hostWin.ActualHeight;
        var cx = w * 0.5;
        var cy = h * 0.45;

        var life = (duration ?? TimeSpan.FromSeconds(1.8)).TotalSeconds;

        for (int i = 0; i < pieces; i++) {
            var s = MakePiece();
            var rot = (RotateTransform)s.RenderTransform;

            // initial outward burst
            var angle = _rng.NextDouble() * Math.PI * 2.0;
            var speed = 400 + _rng.NextDouble() * 520; // px/s
            var vx = Math.Cos(angle) * speed;
            var vy = Math.Sin(angle) * speed;

            var p = new Particle {
                Shape = s,
                Rot = rot,
                X = cx, Y = cy,
                VX = vx, VY = vy - 50 * (_rng.NextDouble()), // tiny upward bias
                W = (_rng.Next(2) == 0 ? 1 : -1) * (180 + _rng.NextDouble() * 360), // deg/s
                Age = 0,
                Life = life * (0.85 + _rng.NextDouble() * 0.3) // vary a bit
            };
            Canvas.SetLeft(s, p.X);
            Canvas.SetTop(s, p.Y);
            _layer.Children.Add(s);
            _live.Add(p);
        }

        StartLoop();
    }

    private void StartLoop() {
        if (_loopHooked) return;
        _lastTick = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRender;
        _loopHooked = true;
    }

    private void StopLoopIfIdle() {
        if (_loopHooked && _live.Count == 0 && _layer != null) {
            CompositionTarget.Rendering -= OnRender;
            _loopHooked = false;
            // if layer was temporary and now empty, you could remove it here
        }
    }

    private void OnRender(object? sender, EventArgs e) {
        if (_layer == null) return;

        // compute dt (seconds)
        var args = (RenderingEventArgs)e;
        if (_lastTick == TimeSpan.Zero) { _lastTick = args.RenderingTime; return; }
        var dt = (args.RenderingTime - _lastTick).TotalSeconds;
        _lastTick = args.RenderingTime;
        if (dt <= 0 || dt > 0.1) dt = 1.0 / 60.0; // clamp

        // constants: constant gravity, velocity drag (decays the initial burst)
        const double G = 900.0;      // px/s^2 downward
        const double DRAG = 1.8;     // s^-1  (higher -> faster decay of burst)
        const double ROT_DRAG = 0.6; // s^-1

        var host = Window.GetWindow(_layer);
        var w = host?.ActualWidth ?? 1200;
        var h = host?.ActualHeight ?? 800;

        for (int i = _live.Count - 1; i >= 0; --i) {
            var p = _live[i];
            p.Age += dt;

            // gravity first
            p.VY += G * dt;

            // exponential drag on velocity (kills the radial burst over time)
            var dragFactor = Math.Exp(-DRAG * dt);
            p.VX *= dragFactor;
            p.VY *= dragFactor;

            // integrate position
            p.X += p.VX * dt;
            p.Y += p.VY * dt;

            // spin with mild damping
            p.W *= Math.Exp(-ROT_DRAG * dt);
            p.Rot.Angle += p.W * dt;

            // late fade
            double t = p.Age / p.Life;
            if (t > 0.65) {
                p.Shape.Opacity = 0.95 * (1.0 - (t - 0.65) / 0.35);
            }

            // apply to canvas
            Canvas.SetLeft(p.Shape, p.X);
            Canvas.SetTop(p.Shape, p.Y);

            // kill if done
            bool off = p.Y > h + 80 || p.X < -120 || p.X > w + 120 || p.Age >= p.Life || p.Shape.Opacity <= 0;
            if (off) {
                _layer.Children.Remove(p.Shape);
                _live.RemoveAt(i);
            }
        }

        StopLoopIfIdle();
    }

    // -------- helpers (unchanged-ish) --------
    private double Next01() => _rng.NextDouble();

    private Shape MakePiece() {
        var isRect = _rng.Next(2) == 0;
        var size = 6 + _rng.NextDouble() * 10;
        Shape s = isRect ? new Rectangle { Width = size, Height = size * (0.6 + Next01()) }
                         : new Ellipse   { Width = size, Height = size };

        s.Fill = new SolidColorBrush(RandomConfettiColor());
        s.RenderTransformOrigin = new Point(0.5, 0.5);
        s.RenderTransform = new RotateTransform(_rng.Next(360));
        s.Opacity = 0.95;
        return s;
    }

    private Color RandomConfettiColor() {
        Color[] palette = {
            (Color)ColorConverter.ConvertFromString("#F94144"),
            (Color)ColorConverter.ConvertFromString("#F3722C"),
            (Color)ColorConverter.ConvertFromString("#F9C74F"),
            (Color)ColorConverter.ConvertFromString("#90BE6D"),
            (Color)ColorConverter.ConvertFromString("#43AA8B"),
            (Color)ColorConverter.ConvertFromString("#577590"),
            (Color)ColorConverter.ConvertFromString("#B56576"),
        };
        return palette[_rng.Next(palette.Length)];
    }

    private Canvas? FindOrCreateEffectsLayer(Window hostWin) {
        if (hostWin.Content is Panel rootPanel) {
            var named = LogicalTreeHelper.FindLogicalNode(hostWin, "EffectsLayer") as Canvas;
            if (named != null) return named;

            var temp = new Canvas { IsHitTestVisible = false };
            Panel.SetZIndex(temp, 999);
            if (rootPanel is Grid grid) {
                Grid.SetRowSpan(temp, Math.Max(1, grid.RowDefinitions.Count == 0 ? 1 : grid.RowDefinitions.Count));
                Grid.SetColumnSpan(temp, Math.Max(1, grid.ColumnDefinitions.Count == 0 ? 1 : grid.ColumnDefinitions.Count));
            }
            rootPanel.Children.Add(temp);
            return temp;
        }
        return null;
    }
}

}
