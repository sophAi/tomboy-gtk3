using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gtk;
using Tmds.DBus;
using Tomboy.Services;
using Tomboy.Views;

namespace Tomboy
{
    enum CommandLineAction
    {
        None,
        Help,
        Version,
        Quit,
        OpenNote,
        NewNote,
        Search,
        StartHere,
        Tray
    }

    class ParsedArgs
    {
        public CommandLineAction Action { get; set; } = CommandLineAction.None;
        public string? Parameter { get; set; }
    }

    class Program
    {
        private static TrayManager? instanceTray;
        private static MainWindow? mainWindow;

        private static MainWindow GetOrCreateMainWindow()
        {
            if (mainWindow == null || mainWindow.Handle == IntPtr.Zero)
            {
                mainWindow = new MainWindow();
            }
            return mainWindow;
        }

        private static ParsedArgs ParseCommandLine(string[] args)
        {
            var parsed = new ParsedArgs();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg == "--help" || arg == "-h" || arg == "--usage")
                {
                    parsed.Action = CommandLineAction.Help;
                    return parsed;
                }
                if (arg == "--version" || arg == "-v")
                {
                    parsed.Action = CommandLineAction.Version;
                    return parsed;
                }
                if (arg == "-q" || arg == "--quit" || arg == "--quite")
                {
                    parsed.Action = CommandLineAction.Quit;
                    return parsed;
                }
                if (arg == "--start-here")
                {
                    parsed.Action = CommandLineAction.StartHere;
                    return parsed;
                }
                if (arg == "--tray" || arg == "--background" || arg == "-b")
                {
                    parsed.Action = CommandLineAction.Tray;
                    return parsed;
                }
                if (arg == "--open-note")
                {
                    parsed.Action = CommandLineAction.OpenNote;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        parsed.Parameter = args[++i];
                    }
                    return parsed;
                }
                if (arg == "--new-note")
                {
                    parsed.Action = CommandLineAction.NewNote;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        parsed.Parameter = args[++i];
                    }
                    return parsed;
                }
                if (arg == "--search")
                {
                    parsed.Action = CommandLineAction.Search;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        parsed.Parameter = args[++i];
                    }
                    return parsed;
                }

                // Support URI directly: note://tomboy/... or path to .note file
                if (arg.StartsWith("note://tomboy/", StringComparison.OrdinalIgnoreCase) ||
                    (File.Exists(arg) && arg.EndsWith(".note", StringComparison.OrdinalIgnoreCase)))
                {
                    parsed.Action = CommandLineAction.OpenNote;
                    parsed.Parameter = arg;
                    return parsed;
                }
            }

            return parsed;
        }

        [STAThread]
        public static void Main(string[] args)
        {
            var parsed = ParseCommandLine(args);

            if (parsed.Action == CommandLineAction.Help)
            {
                Console.WriteLine(
@"Tomboy: A simple, easy to use desktop note-taking application.
Usage:
  tomboy [options]

Options:
  --open-note [title/url/path]  Display the existing note matching title, URI, or file path.
  --new-note                    Create and display a new note.
  --new-note [title]            Create and display a new note, with a title.
  --search [text]               Open the search all notes window with optional search text.
  --start-here                  Display the 'Start Here' note.
  --tray, --background, -b      Start Tomboy resident in notification tray without showing the main window.
  -q, --quit                    Close running Tomboy instance.
  --help, -h, --usage           Print this usage message.
  --version, -v                 Print version information.");
                return;
            }

            if (parsed.Action == CommandLineAction.Version)
            {
                Console.WriteLine("Tomboy Notes 2.0.0 (.NET 8 + GTK3)");
                return;
            }

            // 1. Check if an instance is already running via DBus
            bool isAlreadyRunning = false;
            try
            {
                isAlreadyRunning = Task.Run(async () =>
                {
                    var connection = new Connection(Address.Session);
                    await connection.ConnectAsync();
                    return await connection.IsServiceActiveAsync("org.gnome.Tomboy");
                }).GetAwaiter().GetResult();
            }
            catch
            {
                isAlreadyRunning = false;
            }

            // If another instance is running, forward the command via DBus and exit
            if (isAlreadyRunning)
            {
                try
                {
                    Task.Run(async () =>
                    {
                        var connection = new Connection(Address.Session);
                        await connection.ConnectAsync();
                        var remote = connection.CreateProxy<ITomboyRemoteControl>("org.gnome.Tomboy", "/org/gnome/Tomboy/RemoteControl");

                        switch (parsed.Action)
                        {
                            case CommandLineAction.Quit:
                                Console.WriteLine("Quitting running Tomboy instance via DBus...");
                                await remote.QuitAsync();
                                break;

                            case CommandLineAction.OpenNote:
                                if (!string.IsNullOrEmpty(parsed.Parameter))
                                {
                                    bool ok = false;
                                    try
                                    {
                                        ok = await remote.DisplayNoteAsync(parsed.Parameter);
                                    }
                                    catch (DBusException dbusEx) when (dbusEx.ErrorName == "org.freedesktop.DBus.Error.UnknownMethod")
                                    {
                                        string guidOrTitle = System.IO.Path.GetFileNameWithoutExtension(parsed.Parameter);
                                        ok = await remote.ShowNoteAsync(guidOrTitle);
                                    }

                                    if (!ok)
                                    {
                                        Console.WriteLine($"Tomboy note not found: {parsed.Parameter}");
                                    }
                                }
                                else
                                {
                                    await remote.DisplaySearchAsync();
                                }
                                break;

                            case CommandLineAction.NewNote:
                                if (!string.IsNullOrEmpty(parsed.Parameter))
                                {
                                    string existing = string.Empty;
                                    try
                                    {
                                        existing = await remote.FindNoteAsync(parsed.Parameter);
                                    }
                                    catch {}

                                    if (!string.IsNullOrEmpty(existing))
                                    {
                                        try { await remote.DisplayNoteAsync(existing); }
                                        catch { await remote.ShowNoteAsync(existing); }
                                    }
                                    else
                                    {
                                        await remote.CreateNamedNoteAsync(parsed.Parameter);
                                    }
                                }
                                else
                                {
                                    await remote.CreateNoteAsync();
                                }
                                break;

                            case CommandLineAction.Search:
                                if (!string.IsNullOrEmpty(parsed.Parameter))
                                {
                                    await remote.DisplaySearchWithTextAsync(parsed.Parameter);
                                }
                                else
                                {
                                    await remote.DisplaySearchAsync();
                                }
                                break;

                            case CommandLineAction.StartHere:
                                string uri = string.Empty;
                                try
                                {
                                    uri = await remote.FindStartHereNoteAsync();
                                }
                                catch {}

                                if (!string.IsNullOrEmpty(uri))
                                {
                                    try { await remote.DisplayNoteAsync(uri); }
                                    catch { await remote.ShowNoteAsync(uri); }
                                }
                                else
                                {
                                    try { await remote.ShowNoteAsync("Start Here"); }
                                    catch { await remote.DisplaySearchAsync(); }
                                }
                                break;

                            case CommandLineAction.Tray:
                                // Tomboy is already running; quietly exit without displaying search window
                                break;

                            case CommandLineAction.None:
                            default:
                                await remote.DisplaySearchAsync();
                                break;
                        }
                    }).Wait(3000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error communicating with running Tomboy instance: {ex.Message}");
                }

                return;
            }

            // If user requested quit but Tomboy is not running:
            if (parsed.Action == CommandLineAction.Quit)
            {
                Console.WriteLine("Tomboy is not running.");
                return;
            }

            // 2. Cold Start: Initialize GTK Application
            Application.Init();
            SyncService.InitializeAutosync();

            var mainWin = GetOrCreateMainWindow();

            // 3. Handle initial action on cold start
            switch (parsed.Action)
            {
                case CommandLineAction.OpenNote:
                    bool opened = false;
                    if (!string.IsNullOrEmpty(parsed.Parameter))
                    {
                        opened = mainWin.OpenNoteByUriOrTitle(parsed.Parameter);
                    }
                    if (!opened)
                    {
                        mainWin.ShowAll();
                        mainWin.Present();
                    }
                    break;

                case CommandLineAction.NewNote:
                    mainWin.CreateNewNote(parsed.Parameter);
                    break;

                case CommandLineAction.StartHere:
                    mainWin.OpenStartHereNote();
                    break;

                case CommandLineAction.Search:
                    mainWin.ShowAll();
                    mainWin.Present();
                    if (!string.IsNullOrEmpty(parsed.Parameter))
                    {
                        mainWin.SetSearchText(parsed.Parameter);
                    }
                    break;

                case CommandLineAction.Tray:
                    // Start resident in tray without displaying the main search window
                    break;

                case CommandLineAction.None:
                default:
                    mainWin.ShowAll();
                    mainWin.Present();
                    break;
            }

            // 4. Initialize System Tray Icon
            try
            {
                instanceTray = new TrayManager
                {
                    OnCreateNewNote = () => {
                        var win = GetOrCreateMainWindow();
                        win.CreateNewNote();
                    },

                    OnOpenStartHere = () => {
                        var win = GetOrCreateMainWindow();
                        win.OpenStartHereNote();
                    },

                    OnSearchNotes = () => {
                        var win = GetOrCreateMainWindow();
                        win.ShowAll();
                        win.Present();
                    },

                    OnSyncNotes = () => {
                        var win = GetOrCreateMainWindow();
                        win.ShowAll();
                        win.OnSyncAction();
                    },

                    OnShowPreferences = () => {
                        var win = GetOrCreateMainWindow();
                        new PreferencesWindow(win).ShowAll();
                    },

                    OnShowAbout = () => {
                        var about = new AboutDialog
                        {
                            ProgramName = "Tomboy Notes",
                            Version = "2.0.0",
                            Comments = "Classic desktop note-taking application ported to .NET 8 and GTK3.\nFully compatible with Linux Mint (MATE Panel) System Tray & DBus RemoteControl.",
                            Website = "https://wiki.gnome.org/Apps/Tomboy",
                            Authors = new[] { "Alex Graveley", "Boyd Timothy", "Tomboy Contributors", "Antigravity Team" }
                        };
                        about.Run();
                        about.Hide();
                        about.Dispose();
                    },

                    OnQuitApp = () => {
                        Application.Quit();
                    },

                    OnToggleMainWindow = () => {
                        var win = GetOrCreateMainWindow();
                        if (win.Visible)
                        {
                            win.Hide();
                        }
                        else
                        {
                            win.ShowAll();
                            win.Present();
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Tray icon initialization error: {ex.Message}");
            }

            // 5. Register DBus Service Asynchronously (Non-blocking)
            Task.Run(async () =>
            {
                try
                {
                    var dbusServer = new RemoteControlServer();
                    await dbusServer.RegisterAsync(GetOrCreateMainWindow());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DBus Service Registration Exception: {ex.Message}");
                }
            });

            // 6. Enter GTK Main Loop
            Application.Run();
        }
    }
}
