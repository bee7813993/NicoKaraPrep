using System.Runtime.InteropServices;
using Windows.UI;

namespace NicoKaraPrep.App.Services;

/// <summary>
/// 画面のどこからでも色を拾う（スポイト）。始めた時点の画面全体（すべてのモニター）を写して最前面に広げ、
/// カーソルの横の拡大鏡で狙った点の色を返す。クリック・Enter で決める、Esc・右クリックでやめる、矢印キーで 1 px（Shift で 10 px）動かす、
/// Tab でにこぷれっぷの窓を隠して写し直す（もう一度 Tab で出す。にこぷれっぷの後ろにある窓の色を拾うとき）。
/// 写した画面を止めて見せるので、動画の 1 コマからでも拾える。UI スレッドで呼ぶ（窓のメッセージは XAML のメッセージループで届く）。
/// </summary>
internal sealed class ScreenColorPicker
{
    /// <summary>拡大鏡に出す画素の数（縦横。奇数で中央が拾う点）。</summary>
    private const int Cells = 11;

    private static ScreenColorPicker? _current;
    private static WindowProc? _wndProc;
    private static ushort _classAtom;
    private const string ClassName = "NicoKaraPrep.ScreenColorPicker";

    private readonly TaskCompletionSource<Color?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Microsoft.UI.Windowing.AppWindow? _appWindow;
    private IntPtr _hwnd;
    private IntPtr _memDc;
    private IntPtr _bitmap;
    private IntPtr _oldBitmap;
    private int _originX;
    private int _originY;
    private int _width;
    private int _height;
    private POINT _cursor;
    private RECT _drawn;
    private bool _appHidden;
    private bool _recapturing;
    private bool _leftDown;
    private bool _rightDown;
    private int _keyDown;
    private bool _finished;

    private ScreenColorPicker(Microsoft.UI.Windowing.AppWindow? appWindow)
    {
        _appWindow = appWindow;
    }

    /// <summary>色を拾っている最中か。</summary>
    public static bool IsPicking => _current is not null;

    /// <summary>
    /// 色を拾う。決めた色（不透明）を返し、やめたら null。<paramref name="appWindow"/> は Tab で隠すにこぷれっぷの窓。
    /// 拾っている最中にもう一度呼んだら null。
    /// </summary>
    public static Task<Color?> PickAsync(Microsoft.UI.Windowing.AppWindow? appWindow)
    {
        if (_current is not null) return Task.FromResult<Color?>(null);
        var picker = new ScreenColorPicker(appWindow);
        try
        {
            picker.Start();
        }
        catch
        {
            picker.Cleanup();
            throw;
        }
        return picker._result.Task;
    }

    // ------------------------------------------------------------ 始める・終える

    private void Start()
    {
        EnsureClass();
        Capture();

        _current = this;
        _hwnd = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW, ClassName, "色を拾う", WS_POPUP,
            _originX, _originY, _width, _height, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            _current = null;
            throw new InvalidOperationException($"色を拾う画面を作れませんでした（{Marshal.GetLastWin32Error()}）");
        }

        GetCursorPos(out var p);
        _cursor = new POINT { X = p.X - _originX, Y = p.Y - _originY };
        ShowWindow(_hwnd, SW_SHOW);
        SetForegroundWindow(_hwnd);
        UpdateWindow(_hwnd);
    }

    /// <summary>
    /// 終える（color が null ならやめた）。隠したにこぷれっぷの窓を出して前に戻してから、この画面を閉じる
    /// （先に閉じると、ほかのアプリの窓が前に来ることがあるため）。
    /// </summary>
    private void Finish(Color? color)
    {
        if (_finished) return;
        _finished = true;
        if (_leftDown || _rightDown) ReleaseCapture();
        try
        {
            if (_appHidden) _appWindow?.Show();
            App.MainWindow?.Activate();
        }
        catch (Exception)
        {
            // 窓を出し直せなくても、拾った色は返す
        }
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        Cleanup();
        _result.TrySetResult(color);
    }

    private void Cleanup()
    {
        if (ReferenceEquals(_current, this)) _current = null;
        _hwnd = IntPtr.Zero;
        ReleaseCaptureBitmap();
    }

    // ------------------------------------------------------------ 画面を写す

    /// <summary>画面全体（すべてのモニターを合わせた範囲）を写す。</summary>
    private void Capture()
    {
        ReleaseCaptureBitmap();
        _originX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        _originY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        _width = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        _height = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            _memDc = CreateCompatibleDC(screen);
            _bitmap = CreateCompatibleBitmap(screen, _width, _height);
            if (_memDc == IntPtr.Zero || _bitmap == IntPtr.Zero) throw new InvalidOperationException("画面を写せませんでした");
            _oldBitmap = SelectObject(_memDc, _bitmap);
            BitBlt(_memDc, 0, 0, _width, _height, screen, _originX, _originY, SRCCOPY | CAPTUREBLT);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private void ReleaseCaptureBitmap()
    {
        if (_memDc != IntPtr.Zero)
        {
            if (_oldBitmap != IntPtr.Zero) SelectObject(_memDc, _oldBitmap);
            DeleteDC(_memDc);
        }
        if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
        _memDc = _bitmap = _oldBitmap = IntPtr.Zero;
    }

    /// <summary>Tab: にこぷれっぷの窓を隠す・出して、画面を写し直す（窓が消える・出るのを待ってから写す）。</summary>
    private async void ToggleAppWindow()
    {
        if (_recapturing || _appWindow is null) return;
        _recapturing = true;
        try
        {
            // 写し直すあいだはこの画面を隠す（画面の録画にも拾う様子が写るよう、録画から外す設定は使わない）
            ShowWindow(_hwnd, SW_HIDE);
            if (_appHidden) _appWindow.Show(false);
            else _appWindow.Hide();
            _appHidden = !_appHidden;
            await Task.Delay(400);
            if (_finished) return;

            int oldX = _originX, oldY = _originY, oldW = _width, oldH = _height;
            Capture();
            if (_originX != oldX || _originY != oldY || _width != oldW || _height != oldH)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, _originX, _originY, _width, _height, SWP_NOACTIVATE);
                _cursor = new POINT { X = _cursor.X + oldX - _originX, Y = _cursor.Y + oldY - _originY };
            }
            ShowWindow(_hwnd, SW_SHOW);
            SetForegroundWindow(_hwnd);
            InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
        catch (Exception)
        {
            Finish(null);
        }
        finally
        {
            _recapturing = false;
        }
    }

    // ------------------------------------------------------------ 窓のメッセージ

    private static void EnsureClass()
    {
        if (_classAtom != 0) return;
        _wndProc = StaticWndProc;
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandleW(null),
            hCursor = LoadCursorW(IntPtr.Zero, IDC_CROSS),
            lpszClassName = ClassName,
        };
        _classAtom = RegisterClassExW(ref wc);
        if (_classAtom == 0) throw new InvalidOperationException($"色を拾う画面を作れませんでした（{Marshal.GetLastWin32Error()}）");
    }

    private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (_current is { } picker && picker._hwnd == hwnd) return picker.WndProc(hwnd, msg, wParam, lParam);
        }
        catch (Exception)
        {
            _current?.Finish(null);
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                return 1;
            case WM_PAINT:
                Paint(hwnd);
                return IntPtr.Zero;
            case WM_MOUSEMOVE:
                MoveTo(PointOf(lParam));
                return IntPtr.Zero;
            case WM_LBUTTONDOWN:
                _leftDown = true;
                SetCapture(hwnd);
                return IntPtr.Zero;
            case WM_LBUTTONUP:
                // 押したのも離したのもこの画面のときだけ決める（離したことが後ろの窓に届かないように、離したときに閉じる）
                if (_leftDown) Finish(ColorAt(PointOf(lParam)));
                return IntPtr.Zero;
            case WM_RBUTTONDOWN:
                _rightDown = true;
                SetCapture(hwnd);
                return IntPtr.Zero;
            case WM_RBUTTONUP:
                if (_rightDown) Finish(null);
                return IntPtr.Zero;
            case WM_KEYDOWN:
                OnKeyDown((int)wParam.ToInt64());
                return IntPtr.Zero;
            case WM_KEYUP:
                // Enter・Esc は離したときに閉じる（離したことが、にこぷれっぷのボタンなどに届かないように）
                int key = (int)wParam.ToInt64();
                if (key == _keyDown && key == VK_RETURN) Finish(ColorAt(_cursor));
                else if (key == _keyDown && key == VK_ESCAPE) Finish(null);
                return IntPtr.Zero;
            case WM_CLOSE:
                Finish(null);
                return IntPtr.Zero;
            case WM_DESTROY:
                return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnKeyDown(int key)
    {
        switch (key)
        {
            case VK_RETURN:
            case VK_ESCAPE:
                _keyDown = key;
                break;
            case VK_TAB:
                ToggleAppWindow();
                break;
            case VK_LEFT:
            case VK_RIGHT:
            case VK_UP:
            case VK_DOWN:
                int step = (GetKeyState(VK_SHIFT) & 0x8000) != 0 ? 10 : 1;
                GetCursorPos(out var p);
                int dx = key == VK_LEFT ? -step : key == VK_RIGHT ? step : 0;
                int dy = key == VK_UP ? -step : key == VK_DOWN ? step : 0;
                SetCursorPos(p.X + dx, p.Y + dy); // 動いたことは WM_MOUSEMOVE で届く
                break;
        }
    }

    /// <summary>マウスのメッセージの位置（窓の座標）。</summary>
    private static POINT PointOf(IntPtr lParam) =>
        new() { X = (short)(lParam.ToInt64() & 0xFFFF), Y = (short)((lParam.ToInt64() >> 16) & 0xFFFF) };

    private void MoveTo(POINT client)
    {
        if (client.X == _cursor.X && client.Y == _cursor.Y) return;
        _cursor = client;
        var next = PanelRect();
        InvalidateRect(_hwnd, ref _drawn, false);
        InvalidateRect(_hwnd, ref next, false);
    }

    /// <summary>写した画面の点の色（範囲外なら null）。</summary>
    private Color? ColorAt(POINT client)
    {
        if (_memDc == IntPtr.Zero || client.X < 0 || client.Y < 0 || client.X >= _width || client.Y >= _height) return null;
        uint c = GetPixel(_memDc, client.X, client.Y);
        if (c == CLR_INVALID) return null;
        return Color.FromArgb(255, (byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
    }

    // ------------------------------------------------------------ 描く

    /// <summary>カーソルのあるモニターの拡大率（1 = 96 dpi）。</summary>
    private double Scale()
    {
        IntPtr monitor = MonitorFromPoint(new POINT { X = _cursor.X + _originX, Y = _cursor.Y + _originY }, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>拡大鏡と説明の枠の位置（窓の座標）。カーソルの右下に置き、モニターからはみ出すなら反対側へ。</summary>
    private RECT PanelRect()
    {
        var (w, h) = PanelSize(Scale());
        double s = Scale();
        int gap = (int)Math.Round(20 * s);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        IntPtr monitor = MonitorFromPoint(new POINT { X = _cursor.X + _originX, Y = _cursor.Y + _originY }, MONITOR_DEFAULTTONEAREST);
        GetMonitorInfoW(monitor, ref info);
        var m = new RECT
        {
            Left = info.rcMonitor.Left - _originX,
            Top = info.rcMonitor.Top - _originY,
            Right = info.rcMonitor.Right - _originX,
            Bottom = info.rcMonitor.Bottom - _originY,
        };

        int x = _cursor.X + gap;
        int y = _cursor.Y + gap;
        if (x + w > m.Right) x = _cursor.X - gap - w;
        if (y + h > m.Bottom) y = _cursor.Y - gap - h;
        x = Math.Clamp(x, m.Left, Math.Max(m.Left, m.Right - w));
        y = Math.Clamp(y, m.Top, Math.Max(m.Top, m.Bottom - h));
        return new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
    }

    private const string HintLine1 = "クリック・Enter: この色にする　Esc・右クリック: やめる";
    private const string HintLine2 = "矢印: 1 px 動かす（Shift で 10 px）　Tab: にこぷれっぷを隠す・出す";

    private int CellSize(double s) => Math.Max(4, (int)Math.Round(11 * s));

    private int LineHeight(double s) => (int)Math.Round(18 * s);

    private (int W, int H) PanelSize(double s)
    {
        int pad = (int)Math.Round(8 * s);
        int loupe = Cells * CellSize(s);
        int textW = (int)Math.Round(430 * s); // 説明の 2 行が入る幅（描くときに測って足りなければ広げる）
        if (_measuredTextWidth > 0) textW = Math.Max(textW, _measuredTextWidth);
        int w = Math.Max(loupe, textW) + pad * 2;
        int h = pad + loupe + pad + LineHeight(s) * 2 + pad + LineHeight(s) * 2 + pad;
        return (w, h);
    }

    private int _measuredTextWidth;

    private void Paint(IntPtr hwnd)
    {
        IntPtr hdc = BeginPaint(hwnd, out var ps);
        try
        {
            var r = ps.rcPaint;
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0 || _memDc == IntPtr.Zero) return;

            // 塗る範囲だけを裏で描いてから一度に写す（ちらつかないように）
            IntPtr back = CreateCompatibleDC(hdc);
            IntPtr bmp = CreateCompatibleBitmap(hdc, w, h);
            IntPtr old = SelectObject(back, bmp);
            try
            {
                BitBlt(back, 0, 0, w, h, _memDc, r.Left, r.Top, SRCCOPY);
                var panel = PanelRect();
                DrawPanel(back, panel.Left - r.Left, panel.Top - r.Top);
                _drawn = PanelRect(); // 測った文字の幅で枠が広がっていれば、次に消す範囲も広げる
                BitBlt(hdc, r.Left, r.Top, w, h, back, 0, 0, SRCCOPY);
            }
            finally
            {
                SelectObject(back, old);
                DeleteObject(bmp);
                DeleteDC(back);
            }
        }
        finally
        {
            EndPaint(hwnd, ref ps);
        }
    }

    private void DrawPanel(IntPtr dc, int x, int y)
    {
        double s = Scale();
        var (w, h) = PanelSize(s);
        int pad = (int)Math.Round(8 * s);
        int cell = CellSize(s);
        int loupe = Cells * cell;
        int line = LineHeight(s);

        // 枠（暗い地に明るい縁）
        Fill(dc, x, y, w, h, Rgb(32, 32, 32));
        Frame(dc, x, y, w, h, Rgb(200, 200, 200));

        // 拡大鏡（写した画面の Cells×Cells 画素を拡大。画面の外は黒）
        int lx = x + (w - loupe) / 2, ly = y + pad;
        Fill(dc, lx, ly, loupe, loupe, Rgb(0, 0, 0));
        int half = Cells / 2;
        int sx0 = _cursor.X - half, sy0 = _cursor.Y - half;
        int cx0 = Math.Max(0, -sx0), cy0 = Math.Max(0, -sy0);
        int cx1 = Math.Min(Cells, _width - sx0), cy1 = Math.Min(Cells, _height - sy0);
        if (cx1 > cx0 && cy1 > cy0)
        {
            SetStretchBltMode(dc, COLORONCOLOR);
            StretchBlt(dc, lx + cx0 * cell, ly + cy0 * cell, (cx1 - cx0) * cell, (cy1 - cy0) * cell,
                _memDc, sx0 + cx0, sy0 + cy0, cx1 - cx0, cy1 - cy0, SRCCOPY);
        }
        // 拾う点の枠（どんな色の上でも見えるよう、黒と白の 2 重）
        int px = lx + half * cell, py = ly + half * cell;
        Frame(dc, px - 1, py - 1, cell + 2, cell + 2, Rgb(0, 0, 0));
        Frame(dc, px, py, cell, cell, Rgb(255, 255, 255));
        Frame(dc, lx - 1, ly - 1, loupe + 2, loupe + 2, Rgb(120, 120, 120));

        // 色の見本と 16 進・RGB
        var color = ColorAt(_cursor);
        int ty = ly + loupe + pad;
        int sw = line * 2 - (int)Math.Round(4 * s);
        if (color is { } c)
        {
            Fill(dc, x + pad, ty + 2, sw, sw, Rgb(c.R, c.G, c.B));
            Frame(dc, x + pad, ty + 2, sw, sw, Rgb(200, 200, 200));
        }
        int textX = x + pad + sw + pad;
        SetBkMode(dc, TRANSPARENT);
        IntPtr bold = CreateFontW(-(int)Math.Round(15 * s), 0, 0, 0, 700, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, "Yu Gothic UI");
        IntPtr normal = CreateFontW(-(int)Math.Round(12 * s), 0, 0, 0, 400, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, "Yu Gothic UI");
        IntPtr oldFont = SelectObject(dc, bold);
        try
        {
            SetTextColor(dc, Rgb(255, 255, 255));
            string hex = color is { } k ? $"#{k.R:X2}{k.G:X2}{k.B:X2}" : "（画面の外）";
            Text(dc, textX, ty, hex);
            SelectObject(dc, normal);
            SetTextColor(dc, Rgb(200, 200, 200));
            if (color is { } k2) Text(dc, textX, ty + line, $"R {k2.R}　G {k2.G}　B {k2.B}");

            int hy = ty + line * 2 + pad;
            int w1 = Text(dc, x + pad, hy, HintLine1);
            int w2 = Text(dc, x + pad, hy + line, HintLine2);
            int need = Math.Max(w1, w2);
            if (need > _measuredTextWidth) _measuredTextWidth = need;
        }
        finally
        {
            SelectObject(dc, oldFont);
            DeleteObject(bold);
            DeleteObject(normal);
        }
    }

    private static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    private static void Fill(IntPtr dc, int x, int y, int w, int h, uint color)
    {
        IntPtr brush = CreateSolidBrush(color);
        var rect = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        FillRect(dc, ref rect, brush);
        DeleteObject(brush);
    }

    private static void Frame(IntPtr dc, int x, int y, int w, int h, uint color)
    {
        IntPtr brush = CreateSolidBrush(color);
        var rect = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        FrameRect(dc, ref rect, brush);
        DeleteObject(brush);
    }

    /// <summary>文字を描き、幅を返す。</summary>
    private static int Text(IntPtr dc, int x, int y, string text)
    {
        TextOutW(dc, x, y, text, text.Length);
        return GetTextExtentPoint32W(dc, text, text.Length, out var size) ? size.cx : 0;
    }

    // ------------------------------------------------------------ Win32

    private delegate IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private static readonly IntPtr IDC_CROSS = new(32515);
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const int COLORONCOLOR = 3;
    private const int TRANSPARENT = 1;
    private const uint DEFAULT_CHARSET = 1;
    private const uint CLEARTYPE_QUALITY = 5;
    private const uint CLR_INVALID = 0xFFFFFFFF;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;

    private const int VK_TAB = 0x09;
    private const int VK_RETURN = 0x0D;
    private const int VK_SHIFT = 0x10;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hwnd, ref RECT rect, bool erase);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadCursorW(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr dc, ref RECT rect, IntPtr brush);

    [DllImport("user32.dll")]
    private static extern int FrameRect(IntPtr dc, ref RECT rect, IntPtr brush);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr dc, uint color);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TextOutW(IntPtr dc, int x, int y, string text, int length);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int length, out SIZE size);
}
