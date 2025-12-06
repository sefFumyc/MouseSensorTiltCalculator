using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading; // 需要用到 Thread.Sleep

namespace AngleCorrector
{
    // ==========================================
    // 1. 数据结构与算法 (无变化)
    // ==========================================
    public struct MousePoint { public double dx; public double dy; }

    public class StrokeResult
    {
        public List<PointF> Trajectory { get; set; }
        public double Angle { get; set; }
        public bool IsRight { get; set; }
    }

    public static class RegressionCalculator
    {
        public static double CalculateAngle(List<MousePoint> points)
        {
            if (points == null || points.Count < 2) return 0;
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            int n = points.Count;
            foreach (var p in points) { sumX += p.dx; sumY += p.dy; sumXY += (p.dx * p.dy); sumX2 += (p.dx * p.dx); }
            double denominator = (n * sumX2) - (sumX * sumX);
            if (Math.Abs(denominator) < 0.0001) return 0;
            double slope = ((n * sumXY) - (sumX * sumY)) / denominator;
            return Math.Atan(slope) * (180.0 / Math.PI);
        }
    }

    public class StrokeManager
    {
        private List<MousePoint> _buffer = new List<MousePoint>();
        private bool? _movingRight = null;
        private const int MinStrokeLength = 50;
        public event Action<StrokeResult> OnStrokeAnalyzed;

        public void Process(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;
            bool inputRight = dx > 0;
            if (_movingRight == null) _movingRight = inputRight;
            if (inputRight != _movingRight.Value && _buffer.Count > 0) { FinalizeStroke(); _movingRight = inputRight; }
            _buffer.Add(new MousePoint { dx = dx, dy = dy });
        }

        public void EndCurrentStroke() { if (_buffer.Count > 0) FinalizeStroke(); }

        private void FinalizeStroke()
        {
            double totalLen = _buffer.Sum(p => Math.Abs(p.dx));
            if (totalLen < MinStrokeLength) { _buffer.Clear(); return; }
            int count = _buffer.Count;
            int skip = (int)(count * 0.10);
            int take = count - (skip * 2);
            if (take > 5)
            {
                var validPoints = _buffer.Skip(skip).Take(take).ToList();
                double angle = RegressionCalculator.CalculateAngle(validPoints);
                var path = new List<PointF>();
                float curX = 0; float curY = 0; path.Add(new PointF(0, 0));
                foreach (var p in validPoints) { curX += (float)p.dx; curY += (float)p.dy; path.Add(new PointF(curX, curY)); }
                OnStrokeAnalyzed?.Invoke(new StrokeResult { Trajectory = path, Angle = angle, IsRight = _movingRight.Value });
            }
            _buffer.Clear();
        }
    }

    // ==========================================
    // 2. 底层 RawInput (无变化)
    // ==========================================
    public class RawInputReceiver : IMessageFilter
    {
        public event Action<int, int> OnMouseInput;
        private const int WM_INPUT = 0x00FF;
        private const int RID_INPUT = 0x10000003;
        private const int RIDEV_INPUTSINK = 0x00000100;
        private const int RIM_TYPEMOUSE = 0;

        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }
        [StructLayout(LayoutKind.Sequential)] struct RAWMOUSE { public ushort usFlags; public uint ulButtons; public uint ulRawButtons; public int lLastX; public int lLastY; public uint ulExtraInformation; }
        [StructLayout(LayoutKind.Explicit)] struct RAWINPUT { [FieldOffset(0)] public RAWINPUTHEADER header; [FieldOffset(24)] public RAWMOUSE mouse; }

        [DllImport("User32.dll")] extern static uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
        [DllImport("User32.dll")] extern static bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        public void Initialize(IntPtr hwnd)
        {
            RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
            rid[0].usUsagePage = 0x01; rid[0].usUsage = 0x02; rid[0].dwFlags = RIDEV_INPUTSINK; rid[0].hwndTarget = hwnd;
            RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            Application.AddMessageFilter(this);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                uint dwSize = 0;
                GetRawInputData(m.LParam, RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
                IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
                try
                {
                    if (GetRawInputData(m.LParam, RID_INPUT, buffer, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) == dwSize)
                    {
                        RAWINPUT raw = (RAWINPUT)Marshal.PtrToStructure(buffer, typeof(RAWINPUT));
                        if (raw.header.dwType == RIM_TYPEMOUSE && (raw.mouse.lLastX != 0 || raw.mouse.lLastY != 0))
                            OnMouseInput?.Invoke(raw.mouse.lLastX, raw.mouse.lLastY);
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            return false;
        }
    }

    // ==========================================
    // 3. 主窗口 (UI) - v9.0 240FPS 锁定版
    // ==========================================
    public partial class Form1 : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct NativeMessage { public IntPtr Handle; public uint Message; public IntPtr WParameter; public IntPtr LParameter; public uint Time; public Point Location; }
        [DllImport("user32.dll")] public static extern int PeekMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax, uint remove);

        private RawInputReceiver _rawInput;
        private StrokeManager _strokeManager;

        private enum AppState { Intro, Testing, Results }
        private AppState _currentState = AppState.Intro;

        // 核心变量
        private float _virtualCursorX;
        private float _targetX;
        private Point _screenCenter;
        private float _targetSize = 40f;

        // 运动变量
        private double _animTime = 0;
        private float _swingAmplitude;
        private Stopwatch _gameLoopStopwatch = new Stopwatch();

        // 用户可调参数
        private float _sensitivity = 0.5f;
        private float _targetSpeed = 0.02f;

        // FPS 统计与控制
        private int _frameCount = 0;
        private int _fps = 0;
        private long _lastFpsTick = 0;

        // --- 新增：帧率限制器 ---
        private const double TargetFPS = 240.0;
        private const double TargetFrameTime = 1000.0 / TargetFPS; // 约 4.16ms

        private List<StrokeResult> _history = new List<StrokeResult>();
        private int _scrollY = 0;

        // 绘图资源
        private Font _font = new Font("Microsoft YaHei UI", 12);
        private Font _smallFont = new Font("Consolas", 10);
        private Font _fpsFont = new Font("Consolas", 9);
        private Brush _textBrush = Brushes.LimeGreen;
        private Brush _targetBrushRed = Brushes.Red;
        private Brush _targetBrushHit = Brushes.Yellow;
        private Pen _crosshairPen = new Pen(Color.Lime, 2);
        private Pen _gridPen = new Pen(Color.FromArgb(50, 255, 255, 255), 1);

        public Form1()
        {
            this.Text = "Mouse Sensor Tilt Calculator";
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = Color.Black;
            this.DoubleBuffered = true;
            this.TopMost = true;

            _screenCenter = new Point(Screen.PrimaryScreen.Bounds.Width / 2, Screen.PrimaryScreen.Bounds.Height / 2);
            _swingAmplitude = (_screenCenter.X - 150);

            _strokeManager = new StrokeManager();
            _strokeManager.OnStrokeAnalyzed += (result) => _history.Add(result);

            _rawInput = new RawInputReceiver();

            _lastFpsTick = Stopwatch.GetTimestamp();

            this.KeyDown += OnKeyDown;
            this.MouseDown += OnMouseDown;
            this.MouseWheel += OnMouseWheel;

            this.Load += (s, e) => {
                _gameLoopStopwatch.Start();
                Application.Idle += OnApplicationIdle;
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try { _rawInput.Initialize(this.Handle); _rawInput.OnMouseInput += OnRawInput; }
            catch (Exception ex) { MessageBox.Show("RawInput Error: " + ex.Message); }
        }

        // --- 核心修改：带限频的游戏循环 ---
        private void OnApplicationIdle(object sender, EventArgs e)
        {
            NativeMessage msg;
            // 当消息队列为空时，我们接管 CPU 
            while (PeekMessage(out msg, IntPtr.Zero, 0, 0, 0) == 0)
            {
                long currentTicks = _gameLoopStopwatch.ElapsedTicks;
                double elapsedSinceLastUpdate = (currentTicks - _lastUpdateTick) * 1000.0 / Stopwatch.Frequency;

                // 如果距离上一帧还没到 4.16ms，就不画
                if (elapsedSinceLastUpdate < TargetFrameTime)
                {
                    // 稍微休息一下，让出 CPU 时间片，防止空转烧 CPU
                    // Sleep(0) 表示让出当前时间片给其他线程，但如果你 CPU 很好，可能会马上又回来
                    // 这里为了节能，如果剩余时间很多，可以用 Sleep(1)
                    if (TargetFrameTime - elapsedSinceLastUpdate > 2.0)
                        Thread.Sleep(1);
                    else
                        Thread.Sleep(0);

                    continue;
                }

                // 时间到了，开始更新逻辑和绘图
                UpdateLogic(currentTicks, elapsedSinceLastUpdate);
                this.Invalidate();

                // 返回，让 WinForms 处理 Paint 消息
                return;
            }
        }

        private long _lastUpdateTick = 0;

        private void UpdateLogic(long currentTicks, double elapsedMs)
        {
            _lastUpdateTick = currentTicks; // 记录这一帧的时间

            // 归一化时间系数 (基于 10ms 基准)
            double timeFactor = elapsedMs / 10.0;

            _animTime += (_targetSpeed * timeFactor);
            _targetX = _screenCenter.X + (float)(Math.Sin(_animTime) * _swingAmplitude);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Q && e.Control) Application.Exit();

            if (_currentState == AppState.Testing)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space) StopTest();

                if (e.KeyCode == Keys.Up) _sensitivity += 0.05f;
                if (e.KeyCode == Keys.Down) _sensitivity = Math.Max(0.05f, _sensitivity - 0.05f);

                if (e.KeyCode == Keys.Right) _targetSpeed += 0.005f;
                if (e.KeyCode == Keys.Left) _targetSpeed = Math.Max(0.005f, _targetSpeed - 0.005f);
            }
            else if (_currentState == AppState.Results)
            {
                if (e.KeyCode == Keys.Escape) { _currentState = AppState.Intro; Invalidate(); }
                if (e.KeyCode == Keys.Space) StartTest();
            }
            else if (_currentState == AppState.Intro)
            {
                if (e.KeyCode == Keys.Escape) Application.Exit();
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (_currentState == AppState.Intro) StartTest();
        }

        private void OnMouseWheel(object sender, MouseEventArgs e)
        {
            if (_currentState == AppState.Results)
            {
                _scrollY += e.Delta;
                if (_scrollY > 0) _scrollY = 0;
                Invalidate();
            }
        }

        private void StartTest()
        {
            _currentState = AppState.Testing;
            _history.Clear();
            _virtualCursorX = _screenCenter.X;
            _animTime = 0;
            _scrollY = 0;
            Cursor.Hide();
            Cursor.Position = _screenCenter;
            _lastUpdateTick = _gameLoopStopwatch.ElapsedTicks;
            Invalidate();
        }

        private void StopTest()
        {
            _strokeManager.EndCurrentStroke();
            _currentState = AppState.Results;
            Cursor.Show();
            Invalidate();
        }

        private void OnRawInput(int dx, int dy)
        {
            if (_currentState != AppState.Testing) return;

            if ((Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left)
                _strokeManager.Process(dx, dy);
            else
                _strokeManager.EndCurrentStroke();

            _virtualCursorX += (dx * _sensitivity);
            if (_virtualCursorX < 0) _virtualCursorX = 0;
            if (_virtualCursorX > this.Width) _virtualCursorX = this.Width;
            Cursor.Position = _screenCenter;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            _frameCount++;
            long now = Stopwatch.GetTimestamp();
            if (now - _lastFpsTick >= Stopwatch.Frequency)
            {
                _fps = _frameCount;
                _frameCount = 0;
                _lastFpsTick = now;
            }
            g.DrawString($"FPS: {_fps} (Cap: 240)", _fpsFont, Brushes.Gray, this.Width - 110, 10);

            if (_currentState == AppState.Intro)
            {
                string title = "Mouse Sensor Tilt Calculator / 鼠标传感器倾斜角计算工具";
                string sub = "Click to Start / 点击屏幕开始\n\n" +
                             "1. Move freely to adjust speed / 先随意移动适应速度\n" +
                             "2. Hold Left Click to record / 按住左键进行记录\n\n" +
                             "[ESC] Exit / 退出";
                SizeF tSize = g.MeasureString(title, _font);
                g.DrawString(title, _font, Brushes.Cyan, _screenCenter.X - tSize.Width / 2, _screenCenter.Y - 120);
                g.DrawString(sub, _font, Brushes.White, _screenCenter.X - 200, _screenCenter.Y - 50);
                return;
            }

            if (_currentState == AppState.Testing)
            {
                g.DrawLine(Pens.DarkGray, 0, _screenCenter.Y, this.Width, _screenCenter.Y);
                bool isRecording = (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left;
                float dist = Math.Abs(_virtualCursorX - _targetX);
                bool isHit = isRecording && (dist < (_targetSize / 2 + 5));
                Brush currentOrbBrush = isHit ? _targetBrushHit : _targetBrushRed;
                g.FillEllipse(currentOrbBrush, _targetX - _targetSize / 2, _screenCenter.Y - _targetSize / 2, _targetSize, _targetSize);
                float cSize = 15;
                g.DrawLine(_crosshairPen, _virtualCursorX - cSize, _screenCenter.Y, _virtualCursorX + cSize, _screenCenter.Y);
                g.DrawLine(_crosshairPen, _virtualCursorX, _screenCenter.Y - cSize, _virtualCursorX, _screenCenter.Y + cSize);
                string controls = $"Sensitivity / 灵敏度: {_sensitivity:F2} [↑/↓]\n" + $"Target Speed / 目标速度: {_targetSpeed:F3} [←/→]\n" + "Hold LMB to Record / 按住左键记录\n" + "Finish / 结束测试: [SPACE/ESC]";
                g.DrawString(controls, _font, _textBrush, 20, 20);
                return;
            }

            if (_currentState == AppState.Results)
            {
                g.Clear(Color.FromArgb(20, 20, 20));
                if (_history.Count == 0) { g.DrawString("No Data Recorded (Did you hold Left Click?) \n未采集到数据 (请确保测试时按住了鼠标左键)", _font, Brushes.White, 100, 100); return; }
                double avgAngle = _history.Average(h => h.Angle);
                string header = $"Test Complete / 测试完成 | Samples / 样本数: {_history.Count} | Average Tilt / 平均倾角: {avgAngle:F4}°\n";
                header += avgAngle > 0 ? "Suggestion: RawAccel Rotation (-) / 建议填入负数" : "Suggestion: RawAccel Rotation (+) / 建议填入正数";
                header += "\nScroll to view history / 滚轮查看详情 | [SPACE] Retry / 重测 | [ESC] Home / 主页";
                g.FillRectangle(Brushes.Black, 0, 0, this.Width, 120); g.DrawString(header, _font, Brushes.Yellow, 20, 20); g.DrawLine(Pens.Gray, 0, 120, this.Width, 120);
                int itemHeight = 60; int startY = 140 + _scrollY; int centerX = this.Width / 2;
                for (int i = 0; i < _history.Count; i++)
                {
                    var item = _history[i]; int currentY = startY + (i * itemHeight);
                    if (currentY < 120 - itemHeight || currentY > this.Height) continue;
                    g.DrawLine(_gridPen, 50, currentY, this.Width - 50, currentY);
                    if (item.Trajectory.Count > 1)
                    {
                        var screenPoints = item.Trajectory.Select(p => new PointF(centerX + p.X, currentY + p.Y)).ToArray();
                        Pen linePen = item.IsRight ? Pens.Cyan : Pens.Orange; g.DrawLines(linePen, screenPoints);
                    }
                    string dirText = item.IsRight ? "Right/右" : "Left/左"; string info = $"#{i + 1} {dirText} : {item.Angle:F2}°";
                    g.DrawString(info, _smallFont, Brushes.LightGray, 20, currentY - 10);
                }
            }
        }
    }
}