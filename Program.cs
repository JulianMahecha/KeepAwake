using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeepAwake;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\KeepAwake", out bool first);
        if (!first)
        {
            MessageBox.Show("KeepAwake ya está abierto. Busca su icono junto al reloj.", "KeepAwake");
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
    private readonly ToolStripMenuItem remainingMenu = new() { Enabled = false };
    private readonly System.Windows.Forms.Timer pulse = new();
    private readonly System.Windows.Forms.Timer countdown = new() { Interval = 1000 };
    private readonly Label status = new() { AutoSize = true };
    private readonly Label remaining = new() { AutoSize = true };
    private readonly Button toggle = new() { AutoSize = true };
    private readonly ComboBox duration = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175, AccessibleName = "Duración de la sesión" };
    private readonly NumericUpDown minutes = new() { Minimum = 1, Maximum = 10080, Value = 30, Width = 90, AccessibleName = "Duración personalizada en minutos" };
    private readonly NumericUpDown interval = new() { Minimum = 5, Maximum = 3600, Value = 30, Width = 90, AccessibleName = "Intervalo en segundos" };
    private readonly Icon activeIcon = MakeIcon(true);
    private readonly Icon inactiveIcon = MakeIcon(false);
    private readonly string settingsPath = Path.Combine(AppContext.BaseDirectory, "KeepAwake.settings.json");
    private bool active;
    private bool exiting;
    private int renewals;
    private long? deadline;
    private readonly bool selfTest;

    public MainForm(bool selfTest)
    {
        this.selfTest = selfTest;
        Text = "KeepAwake";
        ClientSize = new Size(540, 395);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = inactiveIcon;
        Font = new Font("Segoe UI", 10);

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        layout.Controls.Add(new Label { Text = "Mantener el equipo despierto", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 12) });
        layout.Controls.Add(status);
        layout.Controls.Add(remaining);
        duration.Items.AddRange(["15 minutos", "30 minutos", "1 hora", "2 horas", "Personalizado", "Hasta desactivar"]);
        duration.SelectedIndex = 1;
        minutes.Enabled = false;
        var durationRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        durationRow.Controls.Add(new Label { Text = "Mantener activo durante:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
        durationRow.Controls.Add(duration);
        layout.Controls.Add(durationRow);
        var customRow = new FlowLayoutPanel { AutoSize = true };
        customRow.Controls.Add(new Label { Text = "Tiempo personalizado (minutos):", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
        customRow.Controls.Add(minutes);
        layout.Controls.Add(customRow);
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 8) };
        row.Controls.Add(new Label { Text = "Intervalo de renovación (segundos):", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
        row.Controls.Add(interval);
        layout.Controls.Add(row);
        layout.Controls.Add(new Label { Text = "Mantiene Windows y la pantalla despiertos.\nÚtil para descargas, presentaciones o procesos largos.\nCerrar esta ventana la oculta en la bandeja.", AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        var hide = new Button { Text = "A la bandeja", AutoSize = true };
        var quit = new Button { Text = "Salir", AutoSize = true };
        buttons.Controls.AddRange([toggle, hide, quit]);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        try
        {
            if (File.Exists(settingsPath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath));
                interval.Value = Math.Clamp(settings?.IntervalSeconds ?? 30, 5, 3600);
                int savedMinutes = Math.Clamp(settings?.DurationMinutes ?? 30, 0, 10080);
                duration.SelectedIndex = savedMinutes switch { 15 => 0, 30 => 1, 60 => 2, 120 => 3, 0 => 5, _ => 4 };
                minutes.Value = Math.Max(1, savedMinutes);
                minutes.Enabled = duration.SelectedIndex == 4;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* Use the safe default for unreadable settings. */ }

        menu.Items.Add("Abrir / Duración…", null, (_, _) => ShowWindow());
        menu.Items.Add(remainingMenu);
        menu.Items.Add(toggleMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Quit());
        tray = new NotifyIcon { Icon = inactiveIcon, Text = "KeepAwake — Desactivado", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();
        toggle.Click += (_, _) => Toggle();
        toggleMenu.Click += (_, _) => Toggle();
        hide.Click += (_, _) => Hide();
        quit.Click += (_, _) => Quit();
        interval.ValueChanged += (_, _) =>
        {
            pulse.Interval = (int)interval.Value * 1000;
            SaveSettings();
        };
        duration.SelectedIndexChanged += (_, _) => { minutes.Enabled = !active && duration.SelectedIndex == 4; SaveSettings(); UpdateStatus(); };
        minutes.ValueChanged += (_, _) => { SaveSettings(); UpdateStatus(); };
        pulse.Interval = (int)interval.Value * 1000;
        pulse.Tick += (_, _) => { if (active) Renew(); };
        countdown.Tick += (_, _) => CheckDeadline();
        FormClosing += (_, e) =>
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        UpdateStatus();
        if (selfTest) Shown += (_, _) => BeginSelfTest();
    }

    private int SelectedMinutes => duration.SelectedIndex switch { 0 => 15, 1 => 30, 2 => 60, 3 => 120, 4 => (int)minutes.Value, _ => 0 };

    private void SaveSettings()
    {
        if (selfTest) return;
        try { File.WriteAllText(settingsPath, JsonSerializer.Serialize(new Settings((int)interval.Value, SelectedMinutes))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, "El ajuste se aplica en esta sesión, pero no se pudo guardar junto al ejecutable.", Text); }
    }

    private bool StopSession()
    {
        if (SetThreadExecutionState(Continuous) == 0) { ReportFailure(); return false; }
        pulse.Stop();
        countdown.Stop();
        deadline = null;
        active = false;
        UpdateStatus();
        return true;
    }

    private void CheckDeadline()
    {
        if (active && deadline is long end && Environment.TickCount64 >= end)
        {
            if (StopSession() && !selfTest)
                tray.ShowBalloonTip(5000, "KeepAwake — Tiempo cumplido", "El equipo vuelve a su comportamiento normal de energía.", ToolTipIcon.Info);
            return;
        }
        UpdateStatus();
    }

    private void Toggle()
    {
        if (active)
        {
            StopSession();
            return;
        }
        else
        {
            if (SetThreadExecutionState(Required) == 0) { ReportFailure(); return; }
            active = true;
            deadline = SelectedMinutes == 0 ? null : Environment.TickCount64 + SelectedMinutes * 60000L;
            renewals++;
            pulse.Start();
            countdown.Start();
        }
        UpdateStatus();
    }

    private void Renew()
    {
        if (deadline is long end && Environment.TickCount64 >= end) { CheckDeadline(); return; }
        CheckDeadline();
        if (!active) return;
        if (SetThreadExecutionState(Required) != 0) { renewals++; return; }
        pulse.Stop();
        countdown.Stop();
        deadline = null;
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
        duration.Enabled = !active;
        minutes.Enabled = !active && duration.SelectedIndex == 4;
        var left = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, (deadline ?? Environment.TickCount64) - Environment.TickCount64) / 1000d));
        string time = deadline.HasValue ? $"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}" : "Hasta desactivar";
        remaining.Text = active ? "Tiempo restante: " + time : "Elige la duración y pulsa Activar.";
        remainingMenu.Text = active ? "Tiempo restante: " + time : "Desactivado";
        tray.Text = active ? "KeepAwake — Activado · " + time : "KeepAwake — Desactivado";
    }
    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void Quit() { exiting = true; Close(); }

    private async void BeginSelfTest()
    {
        var results = new List<string>();
        try
        {
            if (!IsHandleCreated || !Visible || !tray.Visible) throw new Exception("Window/tray did not initialize.");
            results.Add("PASS: published executable opened a window and initialized NotifyIcon.");
            if (JsonSerializer.Deserialize<Settings>("{\"IntervalSeconds\":45}")?.DurationMinutes != 30)
                throw new Exception("Legacy settings did not default to 30 minutes.");
            duration.SelectedIndex = 4;
            minutes.Value = 7;
            if (!minutes.Enabled || SelectedMinutes != 7) throw new Exception("Custom duration failed.");
            duration.SelectedIndex = 0;
            if (minutes.Enabled || SelectedMinutes != 15) throw new Exception("Preset duration failed.");
            results.Add("PASS: duration presets, custom minutes and legacy settings.");
            interval.Value = 5;
            if (pulse.Interval != 5000) throw new Exception("Interval was not applied.");
            results.Add("PASS: configurable interval applied to timer.");
            toggle.PerformClick();
            if (!active || SetThreadExecutionState(Required) != Required) throw new Exception("Windows execution state not applied.");
            if (deadline is not long end || Math.Abs(end - Environment.TickCount64 - 900000) > 2000 || duration.Enabled || minutes.Enabled)
                throw new Exception("Duration was not fixed at activation.");
            if (tray.Icon != activeIcon || !tray.Text.Contains("Activado") || !remaining.Text.Contains("00:15:"))
                throw new Exception("Active tray/countdown indicator failed.");
            results.Add("PASS: Activar sets Windows system/display continuous requirements.");
            results.Add("PASS: green check icon and remaining time in window and tray.");
            await Task.Delay(6500);
            if (renewals < 2) throw new Exception("Renewal timer did not fire.");
            results.Add("PASS: timer renewed Windows execution state.");
            toggleMenu.PerformClick();
            if (active || deadline != null || countdown.Enabled || pulse.Enabled || SetThreadExecutionState(Continuous) != Continuous)
                throw new Exception("Deactivate did not release requirements.");
            if (tray.Icon != inactiveIcon || !tray.Text.Contains("Desactivado") || !duration.Enabled)
                throw new Exception("Inactive tray/control state failed.");
            results.Add("PASS: manual stop releases energy requirements and restores gray pause icon.");
            duration.SelectedIndex = 5;
            toggle.PerformClick();
            if (!active || deadline != null) throw new Exception("Unlimited mode failed.");
            CheckDeadline();
            if (!active) throw new Exception("Unlimited mode expired.");
            toggle.PerformClick();
            results.Add("PASS: until-disabled mode and manual cancellation.");
            duration.SelectedIndex = 1;
            toggle.PerformClick();
            deadline = Environment.TickCount64 + 1500;
            Close();
            if (Visible || !tray.Visible) throw new Exception("Close did not retain tray.");
            await Task.Delay(3500);
            if (active || deadline != null || pulse.Enabled || countdown.Enabled || tray.Icon != inactiveIcon ||
                SetThreadExecutionState(Continuous) != Continuous)
                throw new Exception("Hidden timed session did not expire and release energy requirements.");
            results.Add("PASS: real countdown timer automatically stops a hidden session and releases energy requirements.");
            ShowWindow();
            if (!Visible) throw new Exception("Reopen failed.");
            toggle.PerformClick();
            if (deadline is not long restarted || restarted - Environment.TickCount64 < 1798000)
                throw new Exception("Reactivation did not create a fresh deadline.");
            deadline = Environment.TickCount64 - 60000;
            Renew();
            if (active || SetThreadExecutionState(Continuous) != Continuous) throw new Exception("Overdue session renewed after deadline.");
            results.Add("PASS: reactivation resets duration; overdue session expires before renewal.");
            results.Add("PASS: close hides to tray; reopen restores window.");
        }
        catch (Exception ex)
        {
            results.Add("FAIL: " + ex.Message);
            Environment.ExitCode = 1;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "verification.txt"), results);
            Quit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pulse.Stop();
            countdown.Stop();
            SetThreadExecutionState(Continuous);
            tray.Visible = false;
            tray.Dispose(); menu.Dispose(); pulse.Dispose(); countdown.Dispose();
            activeIcon.Dispose(); inactiveIcon.Dispose();
        }
        base.Dispose(disposing);
    }
    private static Icon MakeIcon(bool enabled)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(enabled ? Color.SeaGreen : Color.SlateGray);
            graphics.FillEllipse(brush, 2, 2, 28, 28);
            using var pen = new Pen(Color.White, 3);
            if (enabled)
            {
                graphics.DrawLines(pen, new Point[] { new(8, 16), new(14, 22), new(24, 10) });
            }
            else
            {
                graphics.DrawLine(pen, 12, 10, 12, 22);
                graphics.DrawLine(pen, 20, 10, 20, 22);
            }
        }
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    private sealed record Settings(int IntervalSeconds, int DurationMinutes = 30);
}
