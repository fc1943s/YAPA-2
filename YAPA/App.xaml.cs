﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Autofac;
using NLog;
using SimpleFeedReader;
using Squirrel;
using YAPA.Shared;
using YAPA.Shared.Common;
using YAPA.Shared.Contracts;
using YAPA.WPF;
using YAPA.WPF.SettingsMananger;
using IContainer = Autofac.IContainer;

namespace YAPA
{
    public class StickToTop
    {
        private readonly Application _app;

        public StickToTop(Application app) => _app = app;

        public void Run()
        {
            if (_app.MainWindow == null)
            {
                return;
            }

            // _app.MainWindow.Deactivated += MainWindow_Deactivated;
            _app.Dispatcher.BeginInvoke(new Action(async () => await StartLoop()));
        }

        private async Task StartLoop()
        {
            if (_app.MainWindow != null)
            {
                _app.MainWindow.Topmost = false;
                _app.MainWindow.Topmost = true;
            }

            await Task.Delay(10000);
            await StartLoop();
        }

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            if (_app.MainWindow == null)
            {
                return;
            }

            _app.MainWindow.Topmost = false;
            _app.MainWindow.Topmost = true;
            
            var hwnd = new WindowInteropHelper(_app.MainWindow).Handle;
            _app.Dispatcher.BeginInvoke(new Action(async () => await RetrySetTopMost(hwnd)));
        }
        
        private const int RetrySetTopMostDelay = 200;
        private const int RetrySetTopMostMax = 20;

        private static async Task RetrySetTopMost(IntPtr hwnd)
        {
            for (var i = 0; i < RetrySetTopMostMax; i++)
            { 
                await Task.Delay(RetrySetTopMostDelay);
                var winStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

                if ((winStyle & WS_EX_TOPMOST) != 0)
                {
                    break;
                }

                if (Application.Current.MainWindow != null)
                {
                    Application.Current.MainWindow.Topmost = false;
                    Application.Current.MainWindow.Topmost = true;
                }
            }
        }

        private static readonly int GWL_EXSTYLE = -20;
        private static readonly int WS_EX_TOPMOST = 0x00000008;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
    }

    public partial class App : ISingleInstanceApp
    {
        private static IContainer Container { get; set; }
        private static IPluginManager PluginManager { get; set; }
        private static IThemeManager ThemeManager { get; set; }
        private static Dashboard Dashboard { get; set; }

        
        [STAThread]
        public static void Main()
        {
            if (SingleInstance<App>.InitializeAsFirstInstance("YAPA2"))
            {
                var application = new App();

                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException; ;

                var dCont = new DependencyContainer();
                Container = dCont.Container;
                PluginManager = dCont.PluginManager;
                ThemeManager = dCont.ThemeManager;
                Dashboard = dCont.Dashboard;

                //Load theme
                Current.MainWindow = (Window)dCont.MainWindow;
#if !DEBUG
                Task.Run(async () =>
                {
                    await Update(Container.Resolve<ISettingManager>(), Container.Resolve<IEnvironment>(), Container.Resolve<PomodoroEngineSettings>());
                });
#endif

                if (Current.MainWindow != null)
                {
                    Current.MainWindow.Loaded += MainWindow_Loaded;
                    Current.MainWindow.Closing += MainWindowOnClosing;
                    Current.MainWindow.Show();
                    Current.MainWindow.Closed += MainWindow_Closed;
                }

                new StickToTop(Current).Run();
                

                application.Init();
                application.Run();

                SingleInstance<App>.Cleanup();
            }
        }

        private static void MainWindowOnClosing(object sender, CancelEventArgs cancelEventArgs)
        {
            try
            {
                var settingsWindow = Container.Resolve<SettingsWindow>();
                settingsWindow.Close();
            }
            catch
            {
            }

            SaveSnapshot();
        }

        private static void SaveSnapshot()
        {
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"YAPA2");
            var json = Container.Resolve<IJson>();
            var engine = Container.Resolve<IPomodoroEngine>();

            var file = Path.Combine(baseDir, "snapshot.json");
            File.WriteAllText(file, json.Serialize(engine.GetSnapshot()));
        }

        private static void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSnapshot();

            var settings = Container.Resolve<PomodoroEngineSettings>();
            if (!string.IsNullOrEmpty(settings.ReleaseNotes))
            {
                var parent = (Window)sender;
                var releaseNoteWindow = new ReleaseNotesWindow(settings.ReleaseNotes) { Owner = parent };
                releaseNoteWindow.ShowDialog();
                settings.ReleaseNotes = null;
            }
        }

        private static void LoadSnapshot()
        {
            var engine = Container.Resolve<IPomodoroEngine>();
            var date = Container.Resolve<IDate>();
            var json = Container.Resolve<IJson>();

            try
            {
                var file = SnapshotFile();

                if (!File.Exists(file))
                {
                    return;
                }

                var snapshotJson = File.ReadAllText(file);
                if (string.IsNullOrEmpty(snapshotJson))
                {
                    return;
                }
                var snapshot = json.Deserialize<PomodoroEngineSnapshot>(snapshotJson);
                if (snapshot == null)
                {
                    return;
                }
                var args = Environment.GetCommandLineArgs();
                var startImmediately = args.Select(x => x.ToLowerInvariant()).Contains(CommandLineArguments.Start);

                var remainingTime = TimeSpan.FromSeconds(snapshot.PomodoroProfile.WorkTime - snapshot.PausedTime);
                if ((snapshot.Phase == PomodoroPhase.Work || snapshot.Phase == PomodoroPhase.Pause)
                    && (startImmediately ||
                        MessageBox.Show(
                            $"Remaining time: {remainingTime.Minutes:00}:{remainingTime.Seconds:00}. Resume pomodoro ?",
                            "Unfinished pomodoro", MessageBoxButton.YesNo) == MessageBoxResult.Yes))
                {
                    snapshot.StartDate = date.DateTimeUtc();
                    engine.LoadSnapshot(snapshot);
                }
            }
            catch
            {
                //Ignore corrupted snapshots
            }
            finally
            {
                RemoveSnapshotFile();
            }
        }

        private static string SnapshotFile()
        {
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"YAPA2");

            var file = Path.Combine(baseDir, "snapshot.json");
            return file;
        }

        private static void RemoveSnapshotFile()
        {
            try
            {
                var file = SnapshotFile();
                if (!File.Exists(file))
                {
                    return;
                }
                File.Delete(file);
            }
            catch (Exception)
            {
                //Ignore
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var logger = LogManager.GetLogger("YAPA2");
            logger.Fatal($"Unhandled exception: {e.ExceptionObject}");
        }

        private static void MainWindow_Closed(object sender, EventArgs e)
        {
            Current.Shutdown();
        }

        private static async Task Update(ISettingManager settings, IEnvironment environment, PomodoroEngineSettings engineSettings)
        {
            try
            {
                var releaseUrl = "yapa-2/";
                var preReleaseUrl = "yapa-2-pre-release/";

                var ftpUrl = "ftp://s1.floatas.net";
                var httpUrl = "http://app.floatas.net/installers";

                var updateType = environment.PreRelease() ? preReleaseUrl : releaseUrl;

                try
                {
                    var httpUpdateUrl = CombineUri(httpUrl, updateType);
                    var newVersion = await UpdateFromUrl(httpUpdateUrl);
                    UpdateSettingsWithReleaseInfo(newVersion, settings, engineSettings);
                }
                catch (Exception)
                {
                    var ftpUpdateUrl = CombineUri(ftpUrl, updateType);
                    var newVersion = await UpdateFromUrl(ftpUpdateUrl);
                    UpdateSettingsWithReleaseInfo(newVersion, settings, engineSettings);
                }
            }
            catch (Exception)
            {
                //Ignore
            }
        }

        private static string CombineUri(params string[] parts)
        {
            return string.Join("/", parts);
        }

        private static void UpdateSettingsWithReleaseInfo(string newVersion, ISettingManager settings, PomodoroEngineSettings engineSettings)
        {
            if (string.IsNullOrEmpty(newVersion))
            {
                return;
            }
            settings.RestartNeeded = true;
            settings.NewVersion = newVersion;
            engineSettings.ReleaseNotes = GetReleaseNotesFor(newVersion);
        }

        private static async Task<string> UpdateFromUrl(string updateUrl)
        {
            var version = string.Empty;
            using (var mgr = new UpdateManager(updateUrl))
            {
                var update = await mgr.UpdateApp();
                if (!string.IsNullOrEmpty(update?.Filename))
                {
                    version = update.Version.ToString();
                }
            }

            return version;
        }

        private static string GetReleaseNotesFor(string newVersion)
        {
            var reader = new FeedReader(new RssFeedNormalizer());
            var releases = reader.RetrieveFeed("https://github.com/YetAnotherPomodoroApp/YAPA-2/releases.atom");
            var release = releases.First(x => x.Title.Contains(newVersion));
            return release?.Content;
        }

        public void Init()
        {
            InitializeComponent();
        }

        #region ISingleInstanceApp Members

        public bool SignalExternalCommandLineArgs(IList<string> args)
        {
            //the first index always contains the location of the exe
            if (args == null || args.Count < 2 || Current.MainWindow == null)
            {
                return true;
            }
            var arg = args[1];
            return ((IApplication)Current.MainWindow).ProcessCommandLineArg(arg);
        }

        #endregion
    }
}
