using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    private static readonly object StdoutLock = new object();
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private static StreamReader stdinReader;
    private static StreamWriter stdoutWriter;
    private static MainForm mainForm;

    [STAThread]
    private static void Main()
    {
        SetupStandardStreams();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        mainForm = new MainForm();
        mainForm.Shown += (sender, args) =>
        {
            var stdinThread = new Thread(ReadStdinLoop)
            {
                IsBackground = true,
                Name = "NativeHostStdinReader"
            };
            stdinThread.Start();

            SendEvent("本地程序已启动", "\"pid\":" + Process.GetCurrentProcess().Id);
        };

        Application.Run(mainForm);
    }

    private static void SetupStandardStreams()
    {
        try
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Utf8NoBom;
        }
        catch
        {
            // WinForms 双击启动时可能没有控制台，标准流仍然会在 Electron 托管启动时可用。
        }

        stdinReader = new StreamReader(Console.OpenStandardInput(), Utf8NoBom, true);
        stdoutWriter = new StreamWriter(Console.OpenStandardOutput(), Utf8NoBom)
        {
            AutoFlush = true
        };
    }

    private static void ReadStdinLoop()
    {
        try
        {
            string line;
            while ((line = stdinReader.ReadLine()) != null)
            {
                AddLog("收到 stdin：" + line);

                if (line.IndexOf("\"type\":\"shutdown\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("\"type\": \"shutdown\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SendEvent("收到关闭通知", null);
                    CloseWindow();
                    break;
                }

                SendRaw("{\"type\":\"message\",\"payload\":{\"event\":\"收到插件消息\",\"received\":\"" +
                    Escape(line) + "\",\"at\":\"" + Escape(DateTime.UtcNow.ToString("o")) + "\"}}");
            }
        }
        catch (Exception error)
        {
            AddLog("读取 stdin 失败：" + error.Message);
            SendEvent("读取 stdin 失败", "\"message\":\"" + Escape(error.Message) + "\"");
        }
    }

    public static void SendEvent(string eventName, string extraJson)
    {
        var json = "{\"type\":\"message\",\"payload\":{\"event\":\"" + Escape(eventName) + "\"";
        if (!string.IsNullOrEmpty(extraJson))
        {
            json += "," + extraJson;
        }
        json += "}}";

        SendRaw(json);
    }

    private static void SendRaw(string value)
    {
        try
        {
            lock (StdoutLock)
            {
                stdoutWriter.WriteLine(value);
            }
            AddLog("发送 stdout：" + value);
        }
        catch (Exception error)
        {
            AddLog("写入 stdout 失败：" + error.Message);
        }
    }

    private static void AddLog(string message)
    {
        var form = mainForm;
        if (form == null || form.IsDisposed)
        {
            return;
        }

        form.AddLog(message);
    }

    private static void CloseWindow()
    {
        var form = mainForm;
        if (form == null || form.IsDisposed)
        {
            return;
        }

        if (form.IsHandleCreated)
        {
            form.BeginInvoke(new Action(() => form.Close()));
        }
        else
        {
            Application.Exit();
        }
    }

    private static string Escape(string value)
    {
        if (value == null)
        {
            return "";
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
}

internal sealed class MainForm : Form
{
    private readonly TextBox logBox;

    public MainForm()
    {
        Text = "萌音本地程序通信测试";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 720;
        Height = 420;
        MinimumSize = new Size(560, 320);

        var title = new Label
        {
            Text = "Native Host 测试程序正在运行",
            Dock = DockStyle.Top,
            Height = 36,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 0),
            Font = new Font(Font.FontFamily, 11, FontStyle.Bold)
        };

        var tip = new Label
        {
            Text = "这里会显示插件通过 stdin 发来的消息，以及本程序写回 stdout 的 JSON Lines。",
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 0)
        };

        logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Font = new Font("Consolas", 10),
            BackColor = Color.White
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8)
        };

        var sendButton = new Button
        {
            Text = "发送测试事件",
            Width = 120,
            Height = 30
        };
        sendButton.Click += (sender, args) =>
        {
            Program.SendEvent("用户点击测试按钮", "\"at\":\"" + DateTime.UtcNow.ToString("o") + "\"");
        };

        var clearButton = new Button
        {
            Text = "清空日志",
            Width = 90,
            Height = 30
        };
        clearButton.Click += (sender, args) => logBox.Clear();

        buttonPanel.Controls.Add(sendButton);
        buttonPanel.Controls.Add(clearButton);

        Controls.Add(logBox);
        Controls.Add(buttonPanel);
        Controls.Add(tip);
        Controls.Add(title);

        AddLog("窗口已创建，等待插件消息。");
    }

    public void AddLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AddLog(message)));
            return;
        }

        logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
    }
}
