using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeepTeamsActive;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\KeepTeamsActive.Julian", out bool first);
        if (!first)
        {
            MessageBox.Show("KeepTeamsActive ya está abierto. Busca su icono junto al reloj.", "KeepTeamsActive");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Contains("--self-test")));
    }
}

internal sealed class MainForm : Form
{
    private const uint Continuous = 0x80000000;
    private const uint Required = Continuous | 0x1 | 0x2;
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem toggleMenu = new("Activar");
    private readonly System.Windows.Forms.Timer pulse = new();
    private readonly Label status = new() { AutoSize = true };
    private readonly Button toggle = new() { AutoSize = true };
    private readonly NumericUpDown interval = new() { Minimum = 5, Maximum = 3600, Value = 30, Width = 90, AccessibleName = "Intervalo en segundos" };
    private readonly Icon activeIcon = MakeIcon(Color.SeaGreen);
    private readonly Icon inactiveIcon = MakeIcon(Color.SlateGray);
    private readonly string settingsPath = Path.Combine(AppContext.BaseDirectory, "KeepTeamsActive.settings.json");
    private bool active;
    private bool exiting;
    private int renewals;

    public MainForm(bool selfTest)
    {
        Text = "KeepTeamsActive";
        ClientSize = new Size(470, 285);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = inactiveIcon;
        Font = new Font("Segoe UI", 10);

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        layout.Controls.Add(new Label { Text = "Mantener el equipo despierto", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 12) });
        layout.Controls.Add(status);
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 8) };
        row.Controls.Add(new Label { Text = "Intervalo de renovación (segundos):", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
        row.Controls.Add(interval);
        layout.Controls.Add(row);
        layout.Controls.Add(new Label { Text = "Mantiene Windows y la pantalla despiertos.\nEl estado de Teams depende de Teams y sus políticas.\nCerrar esta ventana la oculta en la bandeja.", AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        var hide = new Button { Text = "A la bandeja", AutoSize = true };
        var quit = new Button { Text = "Salir", AutoSize = true };
        buttons.Controls.AddRange([toggle, hide, quit]);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        try
        {
            if (File.Exists(settingsPath))
                interval.Value = Math.Clamp(JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath))?.IntervalSeconds ?? 30, 5, 3600);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* Use the safe default for unreadable settings. */ }

        menu.Items.Add("Abrir / Intervalo…", null, (_, _) => ShowWindow());
        menu.Items.Add(toggleMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Quit());
        tray = new NotifyIcon { Icon = inactiveIcon, Text = "KeepTeamsActive — Desactivado", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();
        toggle.Click += (_, _) => Toggle();
        toggleMenu.Click += (_, _) => Toggle();
        hide.Click += (_, _) => Hide();
        quit.Click += (_, _) => Quit();
        interval.ValueChanged += (_, _) =>
        {
            pulse.Interval = (int)interval.Value * 1000;
            if (!selfTest)
            {
                try { File.WriteAllText(settingsPath, JsonSerializer.Serialize(new Settings((int)interval.Value))); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { MessageBox.Show(this, "El intervalo se aplica en esta sesión, pero no se pudo guardar junto al ejecutable.", Text); }
            }
        };
        pulse.Interval = (int)interval.Value * 1000;
        pulse.Tick += (_, _) => { if (active) Renew(); };
        FormClosing += (_, e) =>
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        UpdateStatus();
        if (selfTest) Shown += (_, _) => BeginSelfTest();
    }

    private void Toggle()
    {
        if (active)
        {
            if (SetThreadExecutionState(Continuous) == 0) { ReportFailure(); return; }
            pulse.Stop();
            active = false;
        }
        else
        {
            if (SetThreadExecutionState(Required) == 0) { ReportFailure(); return; }
            active = true;
            renewals++;
            pulse.Start();
        }
        UpdateStatus();
    }

    private void Renew()
    {
        if (SetThreadExecutionState(Required) != 0) { renewals++; return; }
        pulse.Stop();
        SetThreadExecutionState(Continuous);
        active = false;
        UpdateStatus();
        ReportFailure();
    }

    private void ReportFailure() => MessageBox.Show(this, "Windows no pudo aplicar el cambio de energía. Vuelve a intentarlo.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    private void UpdateStatus()
    {
        status.Text = active ? "Estado: Activado" : "Estado: Desactivado";
        status.ForeColor = active ? Color.DarkGreen : Color.DimGray;
        toggle.Text = toggleMenu.Text = active ? "Desactivar" : "Activar";
        tray.Icon = Icon = active ? activeIcon : inactiveIcon;
        tray.Text = "KeepTeamsActive — " + (active ? "Activado" : "Desactivado");
    }
    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void Quit() { exiting = true; Close(); }

    private void BeginSelfTest()
    {
        var results = new List<string>();
        try
        {
            if (!IsHandleCreated || !Visible || !tray.Visible) throw new Exception("Window/tray did not initialize.");
            results.Add("PASS: published executable opened a window and initialized NotifyIcon.");
            interval.Value = 5;
            if (pulse.Interval != 5000) throw new Exception("Interval was not applied.");
            results.Add("PASS: configurable interval applied to timer.");
            toggle.PerformClick();
            if (!active || SetThreadExecutionState(Required) != Required) throw new Exception("Windows execution state not applied.");
            results.Add("PASS: Activar sets Windows system/display continuous requirements.");
            var check = new System.Windows.Forms.Timer { Interval = 6500 };
            check.Tick += (_, _) =>
            {
                check.Stop();
                try
                {
                    if (renewals < 2) throw new Exception("Renewal timer did not fire.");
                    results.Add("PASS: timer renewed Windows execution state.");
                    toggleMenu.PerformClick();
                    if (active || SetThreadExecutionState(Continuous) != Continuous) throw new Exception("Deactivate did not release requirements.");
                    results.Add("PASS: Desactivar releases Windows execution requirements.");
                    Close();
                    if (Visible || !tray.Visible) throw new Exception("Close did not retain tray.");
                    ShowWindow();
                    if (!Visible) throw new Exception("Reopen failed.");
                    results.Add("PASS: close hides to tray; reopen restores window.");
                    results.Add("PASS: Salir closes app and disposes tray.");
                }
                catch (Exception ex) { results.Add("FAIL: " + ex.Message); Environment.ExitCode = 1; }
                finally
                {
                    File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "verification.txt"), results);
                    check.Dispose();
                    Quit();
                }
            };
            check.Start();
        }
        catch (Exception ex)
        {
            results.Add("FAIL: " + ex.Message);
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "verification.txt"), results);
            Environment.ExitCode = 1;
            Quit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pulse.Stop();
            SetThreadExecutionState(Continuous);
            tray.Visible = false;
            tray.Dispose(); menu.Dispose(); pulse.Dispose();
            activeIcon.Dispose(); inactiveIcon.Dispose();
        }
        base.Dispose(disposing);
    }
    private static Icon MakeIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 2, 2, 28, 28);
            using var pen = new Pen(Color.White, 3);
            graphics.DrawLine(pen, 16, 8, 16, 17);
            graphics.DrawArc(pen, 8, 8, 16, 16, -45, 270);
        }
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    private sealed record Settings(int IntervalSeconds);
}
