using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using AOSharp.Bootstrap;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;

// Aliases
using WF = System.Windows.Forms;
using DG = System.Drawing;

namespace NotumVeinOverlay
{
    public class Main : AOPluginEntry
    {
        private static Overlay _overlay;
        private static bool _active;

        [Obsolete]
        public override void Run(string pluginDir)
        {
            _overlay = Overlay.Bootstrap();

            Chat.RegisterCommand("scan", OnScan);
            Chat.WriteLine("[VeinScan] Overlay ready. Try: /scan | /scan test on | /scan full on | /scan grid on | /scan perf on | /scan zfix");
            Game.OnUpdate += OnUpdate;
        }

        private static void OnUpdate(object sender, float dt)
        {
            if (!_active || _overlay == null) return;
            var me = DynelManager.LocalPlayer;
            if (me != null) _overlay.SetWorldAnchor(me.Position.X, me.Position.Y);
        }

        private static void OnScan(string cmd, string[] args, ChatWindow _)
        {
            if (_overlay == null) return;

            if (args.Length == 0)
            {
                _active = !_active;
                if (_active) { _overlay.Activate(); Chat.WriteLine("[VeinScan] Overlay on."); }
                else { _overlay.Deactivate(); Chat.WriteLine("[VeinScan] Overlay off."); }
                return;
            }

            var a0 = args[0].ToLowerInvariant();
            try
            {
                switch (a0)
                {
                    case "test": _overlay.SetDebugSheet(ParseOnOff(args, 1)); break;
                    case "grid": _overlay.SetGrid(ParseOnOff(args, 1)); break;
                    case "center": _overlay.SetCenter(ParseOnOff(args, 1)); break;
                    case "perf": _overlay.SetPerf(ParseOnOff(args, 1)); break;
                    case "full": _overlay.SetFullField(ParseOnOff(args, 1)); break;
                    case "band": _overlay.SetEdgeBand(ParseInt(args, 1, 0, 2000)); break;
                    case "cells": _overlay.SetCells(ParseInt(args, 1, 16, 256), ParseInt(args, 2, 12, 160)); break;
                    case "fps": _overlay.SetFps(ParseInt(args, 1, 15, 120)); break;
                    case "iso": _overlay.SetIso(ParseFloat(args, 1, 0f, 1f)); break;
                    case "color": _overlay.SetColor(ParseByte(args, 1), ParseByte(args, 2), ParseByte(args, 3)); break;
                    case "opacity": _overlay.SetOpacity(ParseInt(args, 1, 0, 255)); break;
                    case "zfix": _overlay.ForceTopMost(); Chat.WriteLine("[VeinScan] TopMost forced."); break;
                    case "track": _overlay.LogRects(); break;
                    case "reset": _overlay.ResetDefaults(); Chat.WriteLine("[VeinScan] Reset to defaults."); break;
                    default:
                        Chat.WriteLine("[VeinScan] cmds: test/grid/center/perf on|off, full on|off, band <px>, cells <x> <y>, iso <0-1>, fps <15-120>, color <r g b>, opacity <0-255>, track, zfix, reset");
                        break;
                }
            }
            catch (Exception ex) { Chat.WriteLine($"[VeinScan] Error: {ex.Message}"); }
        }

        private static bool ParseOnOff(string[] a, int i)
        {
            if (a.Length <= i) throw new ArgumentException("Missing on/off");
            var s = a[i].ToLowerInvariant();
            return s == "on" || s == "1" || s == "true" || s == "yes";
        }
        private static int ParseInt(string[] a, int i, int lo, int hi)
        {
            if (a.Length <= i) throw new ArgumentException("Missing int");
            int v = int.Parse(a[i]); if (v < lo) v = lo; if (v > hi) v = hi; return v;
        }
        private static float ParseFloat(string[] a, int i, float lo, float hi)
        {
            if (a.Length <= i) throw new ArgumentException("Missing float");
            float v = float.Parse(a[i]); if (v < lo) v = lo; if (v > hi) v = hi; return v;
        }
        private static int ParseByte(string[] a, int i)
        {
            if (a.Length <= i) throw new ArgumentException("Missing byte");
            int v = int.Parse(a[i]); if (v < 0) v = 0; if (v > 255) v = 255; return v;
        }
    }

    internal class Overlay : WF.Form
    {
        // ---------- bootstrap ----------
        public static Overlay Bootstrap()
        {
            if (_uiThread != null && _instance != null) return _instance;

            _uiThread = new Thread(() =>
            {
                WF.Application.EnableVisualStyles();
                WF.Application.SetCompatibleTextRenderingDefault(false);
                _instance = new Overlay();
                WF.Application.Run(_instance);
            });
            _uiThread.IsBackground = true;
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();

            var t0 = DateTime.UtcNow;
            while (_instance == null || !_instance.IsHandleCreated)
            {
                if ((DateTime.UtcNow - t0).TotalSeconds > 3) break;
                Thread.Sleep(10);
            }
            return _instance;
        }

        // ---------- external API (thread-safe) ----------
        public void Activate()
        {
            if (InvokeRequired) { BeginInvoke(new Action(Activate)); return; }
            _fadeTarget = 1f; _active = true; _frameTimer.Start(); _trackTimer.Start(); Show(); ForceTopMost();
        }
        public void Deactivate()
        {
            if (InvokeRequired) { BeginInvoke(new Action(Deactivate)); return; }
            _active = false; _fadeTarget = 0f;
        }

        public void SetWorldAnchor(float wx, float wy) { _worldX = wx; _worldY = wy; }
        public void SetEdgeBand(int px) { if (InvokeRequired) { BeginInvoke(new Action<int>(SetEdgeBand), px); return; } _edgeBandPx = px; }
        public void SetFullField(bool on) { if (InvokeRequired) { BeginInvoke(new Action<bool>(SetFullField), on); return; } _fullField = on; }
        public void SetGrid(bool on) { if (InvokeRequired) { BeginInvoke(new Action<bool>(SetGrid), on); return; } _drawGrid = on; Invalidate(); }
        public void SetCenter(bool on) { if (InvokeRequired) { BeginInvoke(new Action<bool>(SetCenter), on); return; } _drawCenter = on; Invalidate(); }
        public void SetPerf(bool on) { if (InvokeRequired) { BeginInvoke(new Action<bool>(SetPerf), on); return; } _showPerf = on; _fpsFrames = 0; Invalidate(); }
        public void SetCells(int cx, int cy)
        {
            if (InvokeRequired) { BeginInvoke(new Action<int, int>(SetCells), cx, cy); return; }
            _cellsX = cx; _cellsY = cy; _prevRow = new float[_cellsX + 1]; _thisRow = new float[_cellsX + 1];
        }
        public void SetColor(int r, int g, int b) { if (InvokeRequired) { BeginInvoke(new Action<int, int, int>(SetColor), r, g, b); return; } _baseColor = DG.Color.FromArgb(r, g, b); }
        public void SetIso(float iso) { if (InvokeRequired) { BeginInvoke(new Action<float>(SetIso), iso); return; } _isoBase = iso; }
        public void SetFps(int fps) { if (InvokeRequired) { BeginInvoke(new Action<int>(SetFps), fps); return; } _frameTimer.Interval = Math.Max(5, 1000 / Math.Max(15, Math.Min(120, fps))); }
        public void SetOpacity(int a) { if (InvokeRequired) { BeginInvoke(new Action<int>(SetOpacity), a); return; } _opacityOverride = Math.Max(0, Math.Min(255, a)); }
        public void SetDebugSheet(bool on) { if (InvokeRequired) { BeginInvoke(new Action<bool>(SetDebugSheet), on); return; } _debugSheet = on; Invalidate(); }
        public void ResetDefaults()
        {
            if (InvokeRequired) { BeginInvoke(new Action(ResetDefaults)); return; }
            _cellsX = 128; _cellsY = 80; _prevRow = new float[_cellsX + 1]; _thisRow = new float[_cellsX + 1];
            _isoBase = 0.45f; _edgeBandPx = 160; _fullField = true; _drawGrid = false; _drawCenter = false; _showPerf = false;
            _frameTimer.Interval = 1000 / 60; _baseColor = DG.Color.FromArgb(120, 235, 255); _opacityOverride = -1;
        }

        public void LogRects()
        {
            if (InvokeRequired) { BeginInvoke(new Action(LogRects)); return; }
            var hWnd = Process.GetCurrentProcess().MainWindowHandle;
            if (hWnd == IntPtr.Zero) { Chat.WriteLine("[VeinScan] MainWindowHandle=0"); return; }
            if (!GetWindowRect(hWnd, out RECT r)) { Chat.WriteLine("[VeinScan] GetWindowRect failed"); return; }
            Chat.WriteLine($"[VeinScan] AO: ({r.Left},{r.Top}) {r.Right - r.Left}x{r.Bottom - r.Top} | Overlay: ({Left},{Top}) {Width}x{Height}");
        }

        public void ForceTopMost()
        {
            if (InvokeRequired) { BeginInvoke(new Action(ForceTopMost)); return; }
            const int SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
            SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        // ---------- state ----------
        private static Overlay _instance;
        private static Thread _uiThread;

        private readonly WF.Timer _frameTimer;
        private readonly WF.Timer _trackTimer;

        private float _worldX, _worldY;
        private float _fade, _fadeTarget;
        private bool _active;
        private float _pulse;

        private int _cellsX = 128, _cellsY = 80;
        private float[] _prevRow, _thisRow;

        private int _edgeBandPx = 160;
        private bool _fullField = true;     // <— default ON for visibility
        private bool _drawGrid = false;
        private bool _drawCenter = false;
        private bool _showPerf = false;
        private bool _debugSheet = false;

        private float _isoBase = 0.45f;     // slightly lower threshold
        private const float IsoWobble = 0.07f;
        private const float Scale = 0.0075f;
        private const float DriftX = 0.035f, DriftY = -0.022f;

        private DG.Color _baseColor = DG.Color.FromArgb(120, 235, 255);
        private int _opacityOverride = -1; // -1 => auto pulse

        private readonly int _seed = (int)(DateTime.UtcNow.Ticks ^ 0x5F3759DF) & 0x7FFFFFFF;
        private static readonly DG.Color CHROMA = DG.Color.FromArgb(1, 0, 1);

        // perf meter
        private int _fpsFrames = 0;
        private DateTime _fpsT0 = DateTime.UtcNow;
        private double _lastFps = 0;

        private Overlay()
        {
            SetStyle(WF.ControlStyles.AllPaintingInWmPaint | WF.ControlStyles.UserPaint | WF.ControlStyles.OptimizedDoubleBuffer, true);
            FormBorderStyle = WF.FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = CHROMA;
            TransparencyKey = CHROMA;

            _prevRow = new float[_cellsX + 1];
            _thisRow = new float[_cellsX + 1];

            _frameTimer = new WF.Timer { Interval = 1000 / 60 }; // 60 FPS cap
            _frameTimer.Tick += (s, e) => TickFrame();

            _trackTimer = new WF.Timer { Interval = 250 };
            _trackTimer.Tick += (s, e) => TrackAOWindow();

            TrackAOWindow();
            Hide();
        }

        protected override WF.CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TRANSPARENT = 0x20;
                const int WS_EX_TOOLWINDOW = 0x80;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_LAYERED = 0x00080000;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
                return cp;
            }
        }
        protected override bool ShowWithoutActivation => true;

        private void TickFrame()
        {
            _fade += (_fadeTarget - _fade) * 0.15f;
            if (_fade < 0.01f && !_active) { _frameTimer.Stop(); Hide(); return; }

            _pulse += 0.016f; // ~60fps timebase
            Invalidate();
        }

        protected override void OnPaint(WF.PaintEventArgs e)
        {
            try
            {
                if (_fade <= 0.01f) return;

                var g = e.Graphics;
                g.SmoothingMode = DG.Drawing2D.SmoothingMode.AntiAlias;

                int W = ClientSize.Width, H = ClientSize.Height;
                if (W <= 0 || H <= 0) return;

                // Debug sheet
                if (_debugSheet)
                {
                    using (var sm = new DG.SolidBrush(DG.Color.FromArgb((int)(80 * _fade), 0, 0, 0)))
                        g.FillRectangle(sm, 0, 0, W, H);
                    using (var pn = new DG.Pen(DG.Color.FromArgb((int)(230 * _fade), 0, 255, 180), 6f))
                        g.DrawRectangle(pn, 3, 3, W - 6, H - 6);
                }

                // Optional grid
                if (_drawGrid)
                {
                    using (var gp = new DG.Pen(DG.Color.FromArgb((int)(40 * _fade), 255, 255, 255), 1f))
                    {
                        for (int x = 0; x <= W; x += 80) g.DrawLine(gp, x, 0, x, H);
                        for (int y = 0; y <= H; y += 80) g.DrawLine(gp, 0, y, W, y);
                    }
                }
                if (_drawCenter)
                {
                    int cx = W / 2, cy = H / 2;
                    using (var cp = new DG.Pen(DG.Color.FromArgb((int)(180 * _fade), 255, 255, 255), 1f))
                    {
                        g.DrawEllipse(cp, cx - 6, cy - 6, 12, 12);
                        g.DrawLine(cp, cx - 10, cy, cx + 10, cy);
                        g.DrawLine(cp, cx, cy - 10, cx, cy + 10);
                    }
                }

                float cellW = (float)W / _cellsX, cellH = (float)H / _cellsY;

                // Pulse/opacity/color
                float pulse = 0.85f + 0.15f * (float)(0.5 * (1 + Math.Sin(_pulse * 2 * Math.PI * 0.7)));
                int aGlow = _opacityOverride >= 0 ? (int)(_opacityOverride * 0.35) : (int)(70 * _fade * pulse);
                int aCore = _opacityOverride >= 0 ? _opacityOverride : (int)(190 * _fade * pulse);

                // Color cycle subtly over time
                var c1 = HueShift(_baseColor, 8f * (float)Math.Sin(_pulse * 0.2f));
                var c2 = HueShift(_baseColor, -8f * (float)Math.Cos(_pulse * 0.17f));

                using (var penGlow = new DG.Pen(DG.Color.FromArgb(aGlow, c2), Math.Max(4f, Math.Min(10f, Math.Min(W, H) * 0.006f))))
                using (var penCore = new DG.Pen(DG.Color.FromArgb(aCore, c1), 2f))
                {
                    float iso = _isoBase + IsoWobble * (float)Math.Sin(_pulse * 2 * Math.PI * 0.35f);

                    for (int x = 0; x <= _cellsX; x++) _prevRow[x] = Sample(x, 0);

                    for (int y = 0; y < _cellsY; y++)
                    {
                        var tmp = _prevRow; _prevRow = _thisRow; _thisRow = tmp;
                        for (int x = 0; x <= _cellsX; x++) _thisRow[x] = Sample(x, y + 1);

                        for (int x = 0; x < _cellsX; x++)
                        {
                            if (!_fullField && !InEdgeBand(W, H, x, y, cellW, cellH)) continue;

                            float v00 = _prevRow[x], v10 = _prevRow[x + 1], v01 = _thisRow[x], v11 = _thisRow[x + 1];
                            int idx = 0;
                            if (v00 > iso) idx |= 1; if (v10 > iso) idx |= 2; if (v11 > iso) idx |= 4; if (v01 > iso) idx |= 8;
                            if (idx == 0 || idx == 15) continue;

                            float x0 = x * cellW, y0 = y * cellH;
                            DG.PointF pL = new DG.PointF(x0, y0 + cellH * T(iso, v00, v01));
                            DG.PointF pR = new DG.PointF(x0 + cellW, y0 + cellH * T(iso, v10, v11));
                            DG.PointF pT = new DG.PointF(x0 + cellW * T(iso, v00, v10), y0);
                            DG.PointF pB = new DG.PointF(x0 + cellW * T(iso, v01, v11), y0 + cellH);

                            // draw seg(s)
                            switch (idx)
                            {
                                case 1: case 14: Draw(g, penGlow, penCore, pL, pT); break;
                                case 2: case 13: Draw(g, penGlow, penCore, pT, pR); break;
                                case 3: case 12: Draw(g, penGlow, penCore, pL, pR); break;
                                case 4: case 11: Draw(g, penGlow, penCore, pR, pB); break;
                                case 5: Draw(g, penGlow, penCore, pL, pT); Draw(g, penGlow, penCore, pR, pB); break;
                                case 6: case 9: Draw(g, penGlow, penCore, pT, pB); break;
                                case 7: case 8: Draw(g, penGlow, penCore, pL, pB); break;
                                case 10: Draw(g, penGlow, penCore, pT, pR); Draw(g, penGlow, penCore, pL, pB); break;
                            }
                        }
                    }
                }

                if (_showPerf)
                {
                    _fpsFrames++;
                    var now = DateTime.UtcNow;
                    double dt = (now - _fpsT0).TotalSeconds;
                    if (dt >= 0.5) { _lastFps = _fpsFrames / dt; _fpsFrames = 0; _fpsT0 = now; }

                    using (var bg = new DG.SolidBrush(DG.Color.FromArgb(120, 0, 0, 0)))
                        g.FillRectangle(bg, 10, 10, 180, 44);
                    using (var br = new DG.SolidBrush(DG.Color.FromArgb(230, 255, 255, 255)))
                    using (var font = new DG.Font("Consolas", 11f))
                    {
                        g.DrawString($"FPS: {_lastFps:0.0}\nCells: {_cellsX}x{_cellsY}\nIso: {_isoBase:0.00} Full:{_fullField}", font, br, 16, 14);
                    }
                }
            }
            catch (Exception ex)
            {
                // fail safe: stop rendering to keep client stable
                try { _frameTimer.Stop(); } catch { }
                try { Hide(); } catch { }
                Chat.WriteLine("[VeinScan] Overlay render error, stopping: " + ex.Message);
            }

            base.OnPaint(e);
        }

        private static DG.Color HueShift(DG.Color c, float degrees)
        {
            // tiny HSV twiddle
            float h, s, v; RgbToHsv(c, out h, out s, out v);
            h = (h + degrees) % 360f; if (h < 0) h += 360f;
            return HsvToRgb(h, s, v);
        }
        private static void RgbToHsv(DG.Color c, out float h, out float s, out float v)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            v = max; float d = max - min; s = max == 0 ? 0 : d / max;
            if (d == 0) { h = 0; return; }
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60f;
        }
        private static DG.Color HsvToRgb(float h, float s, float v)
        {
            int i = (int)Math.Floor(h / 60f) % 6; float f = h / 60f - (float)Math.Floor(h / 60f);
            float p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            float r = 0, g = 0, b = 0;
            switch (i) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; case 5: r = v; g = p; b = q; break; }
            return DG.Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        private static void Draw(DG.Graphics g, DG.Pen glow, DG.Pen core, DG.PointF a, DG.PointF b)
        {
            g.DrawLine(glow, a, b);
            g.DrawLine(core, a, b);
        }

        private bool InEdgeBand(int W, int H, int x, int y, float cw, float ch)
        {
            float left = x * cw, top = y * ch, right = left + cw, bottom = top + ch;
            return (left < _edgeBandPx) || (top < _edgeBandPx) || (right > W - _edgeBandPx) || (bottom > H - _edgeBandPx);
        }

        private float Sample(int gx, int gy)
        {
            // world-anchored value noise with drift
            float sx = (_worldX * Scale) + gx * 0.012f + _pulse * DriftX;
            float sy = (_worldY * Scale) + gy * 0.012f + _pulse * DriftY;
            float v = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (int o = 0; o < 3; o++) { v += amp * ValueNoise(sx * freq, sy * freq); norm += amp; amp *= 0.5f; freq *= 2f; }
            return (norm > 0f) ? v / norm : v;
        }

        private static float T(float iso, float a, float b)
        {
            float d = b - a; if (Math.Abs(d) < 1e-5f) return 0.5f;
            float t = (iso - a) / d; if (t < 0f) t = 0f; else if (t > 1f) t = 1f; return t;
        }

        // ---- noise ----
        private int FastFloor(float f) => (f >= 0f) ? (int)f : (int)f - 1;
        private float Hash01(int x, int y)
        {
            unchecked
            {
                int h = _seed; h ^= x * 374761393; h = (h << 5) | (h >> 27); h ^= y * 668265263; h *= 1274126177;
                return (h & 0x7FFFFFFF) / 2147483647.0f;
            }
        }
        private float Smooth(float t) => t * t * (3f - 2f * t);
        private float Lerp(float a, float b, float t) => a + (b - a) * t;
        private float ValueNoise(float x, float y)
        {
            int xi = FastFloor(x), yi = FastFloor(y);
            float xf = x - xi, yf = y - yi;
            float v00 = Hash01(xi, yi), v10 = Hash01(xi + 1, yi), v01 = Hash01(xi, yi + 1), v11 = Hash01(xi + 1, yi + 1);
            float u = Smooth(xf), v = Smooth(yf);
            float i1 = Lerp(v00, v10, u), i2 = Lerp(v01, v11, u);
            return Lerp(i1, i2, v);
        }

        // ---- window tracking / z ----
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private struct RECT { public int Left, Top, Right, Bottom; }

        private void TrackAOWindow()
        {
            try
            {
                var hWnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hWnd == IntPtr.Zero) return;
                if (!GetWindowRect(hWnd, out RECT r)) return;

                int w = Math.Max(200, r.Right - r.Left), h = Math.Max(200, r.Bottom - r.Top);
                Location = new DG.Point(r.Left, r.Top);
                Size = new DG.Size(w, h);
            }
            catch { /* ignore */ }
        }
    }
}
