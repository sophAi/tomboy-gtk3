using System;
using System.Threading.Tasks;
using Tmds.DBus;
using Tomboy.Models;
using Tomboy.Views;

namespace Tomboy.Services
{
    [DBusInterface("org.gnome.Tomboy.RemoteControl")]
    public interface ITomboyRemoteControl : IDBusObject
    {
        Task<string> CreateNoteAsync();
        Task<string> CreateNamedNoteAsync(string title);
        Task DisplaySearchAsync();
        Task DisplaySearchWithTextAsync(string searchText);
        Task<bool> DisplayNoteAsync(string uri);
        Task<bool> DisplayNoteWithSearchAsync(string uri, string search);
        Task<string> FindNoteAsync(string linkedTitle);
        Task<string> FindStartHereNoteAsync();
        Task<bool> ShowNoteAsync(string uri);
        Task<bool> HideNoteAsync(string uri);
        Task QuitAsync();
    }

    public class TomboyRemoteControl : ITomboyRemoteControl
    {
        public ObjectPath ObjectPath => new ObjectPath("/org/gnome/Tomboy/RemoteControl");

        private MainWindow mainWindow;

        public TomboyRemoteControl(MainWindow window)
        {
            this.mainWindow = window;
        }

        public Task<string> CreateNoteAsync()
        {
            var tcs = new TaskCompletionSource<string>();
            Gtk.Application.Invoke((s, e) =>
            {
                try
                {
                    var note = mainWindow.CreateNewNote();
                    tcs.SetResult($"note://tomboy/{note.Guid}");
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

        public Task<string> CreateNamedNoteAsync(string title)
        {
            var tcs = new TaskCompletionSource<string>();
            Gtk.Application.Invoke((s, e) =>
            {
                try
                {
                    var note = mainWindow.CreateNewNote(title);
                    tcs.SetResult($"note://tomboy/{note.Guid}");
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

        public Task DisplaySearchAsync()
        {
            Gtk.Application.Invoke((s, e) =>
            {
                mainWindow.ShowAll();
                mainWindow.Present();
                mainWindow.SelectAllNotesNotebook();
            });
            return Task.CompletedTask;
        }

        public Task DisplaySearchWithTextAsync(string searchText)
        {
            Gtk.Application.Invoke((s, e) =>
            {
                mainWindow.ShowAll();
                mainWindow.Present();
                mainWindow.SetSearchText(searchText);
            });
            return Task.CompletedTask;
        }

        public Task<bool> DisplayNoteAsync(string uri)
        {
            var tcs = new TaskCompletionSource<bool>();
            Gtk.Application.Invoke((s, e) =>
            {
                try
                {
                    bool ok = mainWindow.OpenNoteByUriOrTitle(uri);
                    tcs.SetResult(ok);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

        public Task<bool> DisplayNoteWithSearchAsync(string uri, string search)
        {
            return DisplayNoteAsync(uri);
        }

        public Task<bool> ShowNoteAsync(string uri) => DisplayNoteAsync(uri);

        public Task<string> FindNoteAsync(string linkedTitle)
        {
            var notes = NoteStorage.LoadAllNotes();
            var note = notes.Find(n => string.Equals(n.Title, linkedTitle, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(note != null ? $"note://tomboy/{note.Guid}" : string.Empty);
        }

        public Task<string> FindStartHereNoteAsync()
        {
            var notes = NoteStorage.LoadAllNotes();
            var note = notes.Find(n => string.Equals(n.Title, "Start Here", StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(note != null ? $"note://tomboy/{note.Guid}" : string.Empty);
        }

        public Task<bool> HideNoteAsync(string uri)
        {
            return Task.FromResult(true);
        }

        public Task QuitAsync()
        {
            Gtk.Application.Invoke((s, e) =>
            {
                Gtk.Application.Quit();
            });
            return Task.CompletedTask;
        }
    }

    public class RemoteControlServer
    {
        private Connection? connection;

        public async Task RegisterAsync(MainWindow mainWindow)
        {
            try
            {
                connection = new Connection(Address.Session);
                await connection.ConnectAsync();

                var service = new TomboyRemoteControl(mainWindow);
                await connection.RegisterObjectAsync(service);
                await connection.RegisterServiceAsync("org.gnome.Tomboy");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DBus registration warning: {ex.Message}");
            }
        }
    }
}
