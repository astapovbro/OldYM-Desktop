using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OldYM
{
    public partial class MainWindow : Window
    {
        public sealed class TrackRow : INotifyPropertyChanged
        {
            bool isLiked;
            bool isDownloaded;
            bool isDownloading;
            public string Title { get; private set; }
            public string Artist { get; private set; }
            public string Length { get; private set; }
            public Track Track { get; private set; }
            public Playlist Playlist { get; private set; }
            public bool IsTrack { get { return Track != null; } }
            public bool IsLiked { get { return isLiked; } }
            public bool IsDownloaded { get { return isDownloaded; } }
            public bool IsDownloading { get { return isDownloading; } }
            public string LikeGlyph { get { return isLiked ? "♥" : "♡"; } }
            public string DownloadGlyph { get { return isDownloading ? "…" : (isDownloaded ? "✓" : "↓"); } }
            public string LikeTip { get { return isLiked ? "Убрать из избранного" : "Добавить в избранное"; } }
            public string DownloadTip { get { return isDownloaded ? "Файл уже скачан" : (isDownloading ? "Идёт скачивание" : "Скачать трек"); } }
            public event PropertyChangedEventHandler PropertyChanged;

            public TrackRow(Track track, bool liked, bool downloaded, bool downloading)
            {
                Track = track;
                Title = track.Title;
                Artist = track.Artist;
                Length = track.Time;
                isLiked = liked;
                isDownloaded = downloaded;
                isDownloading = downloading;
            }

            public TrackRow(Playlist playlist)
            {
                Playlist = playlist;
                Title = playlist.Title;
                Artist = playlist.IsOwned ? "Мой плейлист" : "Плейлист";
                Length = "";
            }

            public void SetState(bool liked, bool downloaded, bool downloading)
            {
                if (isLiked != liked) { isLiked = liked; Changed("IsLiked"); Changed("LikeGlyph"); Changed("LikeTip"); }
                if (isDownloaded != downloaded) { isDownloaded = downloaded; Changed("IsDownloaded"); Changed("DownloadGlyph"); Changed("DownloadTip"); }
                if (isDownloading != downloading) { isDownloading = downloading; Changed("IsDownloading"); Changed("DownloadGlyph"); Changed("DownloadTip"); }
            }

            void Changed(string name)
            {
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs(name));
            }
        }

        readonly ObservableCollection<TrackRow> rows = new ObservableCollection<TrackRow>();
        readonly List<Track> shown = new List<Track>();
        readonly List<Track> queue = new List<Track>();
        readonly List<Playlist> playlists = new List<Playlist>();
        readonly AudioPlayer audio = new AudioPlayer();
        readonly DispatcherTimer playerTimer = new DispatcherTimer();

        Net net;
        Storage store;
        Api api;
        List<string> likedIds = new List<string>();
        readonly HashSet<string> downloadingIds = new HashSet<string>();
        Playlist currentPlaylist;
        string currentView = "";
        string searchText = "";
        int page;
        int index = -1;
        int screenVersion;
        int playVersion;
        bool busy;
        bool waveQueue;
        bool waveBusy;
        bool queueFromSearch;
        bool searchQueueLoading;
        int searchQueuePage;
        string searchQueueText = "";
        bool seeking;
        bool closing;

        public MainWindow()
        {
            InitializeComponent();
            TrackList.ItemsSource = rows;
            MoreButton.IsEnabled = false;

            SearchBox.TextChanged += delegate
            {
                SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            AccountButton.Click += AccountButton_Click;
            NavWave.Click += delegate { OpenWave(); SelectNavigation(NavWave); };
            NavLikes.Click += delegate { OpenLikes(); SelectNavigation(NavLikes); };
            NavPlaylists.Click += delegate { OpenPlaylists(); SelectNavigation(NavPlaylists); };
            NavDownloaded.Click += delegate { OpenDownloaded(); SelectNavigation(NavDownloaded); };
            NavAbout.Click += delegate
            {
                AboutWindow dialog = new AboutWindow();
                dialog.Owner = this;
                dialog.ShowDialog();
            };
            MoreButton.Click += delegate { if (currentView == "playlists") CreatePlaylist(); else LoadMore(); };
            PrevButton.Click += delegate { Previous(); };
            PlayButton.Click += delegate { TogglePlayback(); };
            NextButton.Click += delegate { Play(index + 1); };

            ContextMenu trackMenu = new ContextMenu();

MenuItem addPlaylistItem = new MenuItem
{
    Header = "Добавить в плейлист…"
};
addPlaylistItem.Click += delegate { AddSelectedToPlaylist(); };

MenuItem removePlaylistItem = new MenuItem
{
    Header = "Убрать из этого плейлиста"
};
removePlaylistItem.Click += delegate { RemoveSelectedFromPlaylist(); };

MenuItem renamePlaylistItem = new MenuItem
{
    Header = "Переименовать плейлист"
};
renamePlaylistItem.Click += delegate { RenameSelectedPlaylist(); };

MenuItem deletePlaylistItem = new MenuItem
{
    Header = "Удалить плейлист"
};
deletePlaylistItem.Click += delegate { DeleteSelectedPlaylist(); };

trackMenu.Items.Add(addPlaylistItem);
trackMenu.Items.Add(removePlaylistItem);
trackMenu.Items.Add(renamePlaylistItem);
trackMenu.Items.Add(deletePlaylistItem);

trackMenu.Opened += delegate
{
    TrackRow selected = TrackList.SelectedItem as TrackRow;

    bool isTrack = selected != null && selected.Track != null;
    bool isPlaylist = selected != null && selected.Playlist != null;

    addPlaylistItem.Visibility = isTrack
        ? Visibility.Visible : Visibility.Collapsed;

    removePlaylistItem.Visibility =
        isTrack && currentView == "playlist" &&
        currentPlaylist != null && currentPlaylist.IsOwned
        ? Visibility.Visible : Visibility.Collapsed;

    renamePlaylistItem.Visibility = isPlaylist
        ? Visibility.Visible : Visibility.Collapsed;

    deletePlaylistItem.Visibility = isPlaylist
        ? Visibility.Visible : Visibility.Collapsed;

    renamePlaylistItem.IsEnabled = isPlaylist && selected.Playlist.IsOwned;
    deletePlaylistItem.IsEnabled = isPlaylist && selected.Playlist.IsOwned;
};

TrackList.PreviewMouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e)
{
    DependencyObject element = e.OriginalSource as DependencyObject;

    while (element != null && !(element is ListBoxItem))
        element = VisualTreeHelper.GetParent(element);

    ListBoxItem item = element as ListBoxItem;

    if (item != null)
        item.IsSelected = true;
};

TrackList.ContextMenu = trackMenu;

            playerTimer.Interval = TimeSpan.FromMilliseconds(500);
            playerTimer.Tick += delegate { TickPlayer(); };
            playerTimer.Start();

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dataRoot = Path.Combine(appData, "OldYMDesktop");
            string rootFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "roots.pem");
            try
            {
                if (!File.Exists(rootFile)) throw new IOException("Не найден файл assets\\roots.pem");
                net = new Net(rootFile);
                store = new Storage(dataRoot);
                StatusText.Text = "Войди в аккаунт — можно подтвердить вход кодом на другом устройстве.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка инициализации: " + ex.Message;
                AccountButton.IsEnabled = false;
            }

            SelectNavigation(NavWave);
        }

        void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (store == null || net == null) return;
            try
            {
                string token = store.ReadToken();
                if (!String.IsNullOrEmpty(token)) SignInWithToken(token, true, true);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Не удалось прочитать сохранённый вход. Войди заново: " + ex.Message;
            }
        }

        void MainWindow_Closed(object sender, EventArgs e)
        {
            closing = true;
            Interlocked.Increment(ref playVersion);
            playerTimer.Stop();
            try { audio.Dispose(); } catch { }
        }

        void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Search();
            }
        }

        void SelectNavigation(Button selected)
        {
            Button[] items = new Button[] { NavWave, NavLikes, NavPlaylists, NavDownloaded, NavAbout };
            foreach (Button item in items)
            {
                item.Background = Brushes.Transparent;
                item.Foreground = (Brush)FindResource("Dim");
            }
            selected.Background = (Brush)FindResource("RowSel");
            selected.Foreground = (Brush)FindResource("Accent");
        }

        void AccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (net == null || store == null) return;
            if (api == null)
            {
                LoginWindow dialog = new LoginWindow(net);
                dialog.Owner = this;
                if (dialog.ShowDialog() == true && !String.IsNullOrEmpty(dialog.Token))
                    SignInWithToken(dialog.Token, dialog.Remember, false);
                return;
            }

            ContextMenu menu = new ContextMenu();
            MenuItem name = new MenuItem { Header = "Вошли как: " + api.Login, IsEnabled = false };
            MenuItem logout = new MenuItem { Header = "Выйти из аккаунта" };
            logout.Click += delegate { SignOut(); };
            menu.Items.Add(name);
            menu.Items.Add(new Separator());
            menu.Items.Add(logout);
            menu.PlacementTarget = AccountButton;
            menu.IsOpen = true;
        }

        void SignOut()
        {
            StopPlayback();
            api = null;
            queue.Clear();
            index = -1;
            likedIds.Clear();
            downloadingIds.Clear();
            currentPlaylist = null;
            shown.Clear();
            rows.Clear();
            playlists.Clear();
            currentView = "";
            AccountButton.Content = "Аккаунт";
            try { if (store != null) store.Forget(); }
            catch (Exception ex) { StatusText.Text = "Не удалось удалить сохранённую сессию: " + ex.Message; }
            Heading.Text = "Моя волна";
            PlayerTitle.Text = "ничего не играет";
            PlayerArtist.Text = "";
            CoverImage.Source = null;
            CoverImage.Visibility = Visibility.Collapsed;
            CoverGlyph.Visibility = Visibility.Visible;
            StatusText.Text = "Выполнен выход из аккаунта.";
            MoreButton.Content = "Показать ещё";
            MoreButton.IsEnabled = false;
        }

        void SignInWithToken(string token, bool remember, bool automatic)
        {
            if (net == null || store == null) return;
            Api candidate = new Api(net, token);
            List<string> initialLikes = null;
            RunJob(delegate
            {
                candidate.Account();
                initialLikes = candidate.LikedIds();
                if (!automatic)
                {
                    if (remember) store.SaveToken(token);
                    else store.Forget();
                }
            }, delegate
            {
                StopPlayback();
                queue.Clear();
                index = -1;
                api = candidate;
                likedIds = initialLikes ?? new List<string>();
                screenVersion++;
                currentView = "";
                currentPlaylist = null;
                shown.Clear();
                rows.Clear();
                AccountButton.Content = String.IsNullOrEmpty(candidate.Login) ? "Аккаунт" : candidate.Login;
                StatusText.Text = "Вход выполнен. Загружаю персональную волну…";
                OpenWave();
            });
        }

        void RunJob(Action work, Action success)
        {
            if (busy)
            {
                StatusText.Text = "Дождись завершения текущего запроса.";
                return;
            }
            busy = true;
            SetBusy(true);
            StatusText.Text = "Загружаю…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    work();
                    Post(delegate
                    {
                        busy = false;
                        SetBusy(false);
                        success();
                    });
                }
                catch (Exception ex)
                {
                    Post(delegate
                    {
                        busy = false;
                        SetBusy(false);
                        Report(ex);
                    });
                }
            });
        }

        void SetBusy(bool value)
        {
            NavWave.IsEnabled = !value;
            NavLikes.IsEnabled = !value;
            NavPlaylists.IsEnabled = !value;
            NavAbout.IsEnabled = !value;
            AccountButton.IsEnabled = !value && net != null && store != null;
            SearchBox.IsEnabled = !value;
        }

        void Post(Action action)
        {
            if (closing) return;
            try
            {
                Dispatcher.BeginInvoke(new Action(action), DispatcherPriority.Normal);
            }
            catch (InvalidOperationException) { }
        }

        void Report(Exception ex)
        {
            StatusText.Text = ex is OperationCanceledException ? "Операция отменена." : ex.Message;
        }

        bool NeedAccount()
        {
            if (api != null) return false;
            StatusText.Text = "Сначала войди через кнопку «Аккаунт».";
            return true;
        }

        void Search()
        {
            if (NeedAccount()) return;
            string query = SearchBox.Text.Trim();
            if (query.Length == 0) return;
            int version = ++screenVersion;
            List<Track> result = null;
            RunJob(delegate { result = api.Search(query, 0); }, delegate
            {
                if (version != screenVersion) return;
                currentView = "search";
                currentPlaylist = null;
                searchText = query;
                page = 0;
                SetTracks(result);
                MoreButton.Content = "Показать ещё";
                MoreButton.IsEnabled = result.Count > 0;
                Heading.Text = "Результаты поиска";
                StatusText.Text = "Найдено: " + shown.Count;
            });
        }

        void OpenWave()
        {
            if (NeedAccount()) return;
            int version = ++screenVersion;
            List<Track> result = null;
            RunJob(delegate { result = api.WavePreview(18); }, delegate
            {
                if (version != screenVersion) return;
                currentView = "wave";
                currentPlaylist = null;
                SetTracks(result);
                MoreButton.Content = "Показать ещё";
                MoreButton.IsEnabled = result.Count > 0;
                Heading.Text = "Моя волна";
                StatusText.Text = "Моя волна · " + shown.Count + " рекомендаций · двойной щелчок запускает бесконечную очередь.";
            });
        }

        void OpenLikes()
        {
            if (NeedAccount()) return;
            int version = ++screenVersion;
            List<string> ids = null;
            List<Track> tracks = null;
            RunJob(delegate
            {
                ids = api.LikedIds();
                tracks = api.Details(ids, 0, 50);
            }, delegate
            {
                if (version != screenVersion) return;
                currentView = "likes";
                currentPlaylist = null;
                likedIds = ids;
                page = tracks.Count;
                SetTracks(tracks);
                MoreButton.Content = "Показать ещё";
                MoreButton.IsEnabled = page < likedIds.Count;
                Heading.Text = "Мне нравится";
                StatusText.Text = "Мне нравится: " + shown.Count + " / " + likedIds.Count;
            });
        }

        void OpenPlaylists()
        {
            if (NeedAccount()) return;
            int version = ++screenVersion;
            List<Playlist> result = null;
            RunJob(delegate { result = api.Playlists(); }, delegate
            {
                if (version != screenVersion) return;
                currentView = "playlists";
                DisplayPlaylists(result);
                StatusText.Text = "Плейлистов: " + result.Count + " · двойной щелчок открывает плейлист.";
            });
        }

        void OpenDownloaded()
        {
            if (store == null) return;
            int version = ++screenVersion;
            List<Track> tracks = store.DownloadedTracks();
            if (version != screenVersion) return;
            currentView = "downloads";
            currentPlaylist = null;
            page = tracks.Count;
            SetTracks(tracks);
            MoreButton.Content = "Все скачанные треки";
            MoreButton.IsEnabled = false;
            Heading.Text = "Скачанное";
            StatusText.Text = tracks.Count == 0 ? "Пока нет скачанных треков. Скачанные MP3 появятся здесь." : "Скачанных треков: " + tracks.Count + " · двойной щелчок запускает локальный файл.";
        }

        void OpenPlaylist(Playlist playlist)
        {
            int version = ++screenVersion;
            List<Track> tracks = null;
            RunJob(delegate { tracks = api.PlaylistTracks(playlist); }, delegate
            {
                if (version != screenVersion) return;
                currentView = "playlist";
                currentPlaylist = playlist;
                SetTracks(tracks);
                MoreButton.Content = "Показать ещё";
                MoreButton.IsEnabled = false;
                Heading.Text = playlist.Title;
                StatusText.Text = playlist.Title + " · " + tracks.Count + " треков.";
            });
        }

        void SetTracks(List<Track> tracks)
        {
            shown.Clear();
            if (tracks != null) shown.AddRange(tracks);
            rows.Clear();
            foreach (Track track in shown) rows.Add(CreateTrackRow(track));
        }

        void LoadMore()
        {
            if (NeedAccount()) return;
            if (currentView == "wave")
            {
                AppendWave(false);
                return;
            }
            if (currentView == "search" && queueFromSearch && searchQueueText == searchText)
            {
                AppendSearchQueue();
                return;
            }
            if (shown.Count >= 1000)
            {
                StatusText.Text = "В списке уже 1000 треков. Уточни запрос.";
                MoreButton.IsEnabled = false;
                return;
            }

            int version = screenVersion;
            List<Track> result = null;
            if (currentView == "search")
            {
                RunJob(delegate { result = api.Search(searchText, page + 1); }, delegate
                {
                    if (version != screenVersion) return;
                    page++;
                    AppendUniqueToShown(result);
                    MoreButton.IsEnabled = result.Count > 0;
                    StatusText.Text = "В результатах: " + shown.Count;
                });
            }
            else if (currentView == "likes")
            {
                RunJob(delegate { result = api.Details(likedIds, page, 50); }, delegate
                {
                    if (version != screenVersion) return;
                    page += Math.Min(50, likedIds.Count - page);
                    AppendUniqueToShown(result);
                    MoreButton.IsEnabled = page < likedIds.Count;
                    StatusText.Text = "Мне нравится: " + shown.Count + " / " + likedIds.Count;
                });
            }
        }

        void AppendUniqueToShown(List<Track> extra)
        {
            HashSet<string> ids = new HashSet<string>();
            foreach (Track track in shown) ids.Add(track.Id);
            foreach (Track track in extra)
            {
                if (ids.Add(track.Id))
                {
                    shown.Add(track);
                    rows.Add(CreateTrackRow(track));
                }
            }
        }

        void AppendWave(bool forQueue)
        {
            if (waveBusy || api == null) return;
            List<Track> target = forQueue ? queue : shown;
            if (target.Count == 0) return;
            string last = target[target.Count - 1].Id;
            int generation = playVersion;
            int version = screenVersion;
            Api client = api;
            List<Track> sourceQueue = queue;
            waveBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<Track> extra = client.Wave(last);
                    Post(delegate
                    {
                        waveBusy = false;
                        if (api != client) return;
                        if (forQueue)
                        {
                            if (!waveQueue || queue != sourceQueue) return;
                            int added = AddUnique(queue, extra);
                            if (added == 0)
                            {
                                StatusText.Text = "Волна вернула прежние треки. Нажми «Далее» для повтора.";
                                return;
                            }
                            if (index > 20)
                            {
                                queue.RemoveRange(0, index - 5);
                                index = 5;
                            }
                            if (currentView == "wave")
                            {
                                SetTracks(queue);
                                MoreButton.IsEnabled = true;
                            }
                            if (generation == playVersion && audio.Mode == "stopped") Play(index + 1);
                        }
                        else
                        {
                            if (version != screenVersion) return;
                            int added = AddUnique(shown, extra);
                            while (shown.Count > 200) shown.RemoveAt(0);
                            SetRowsFromShown();
                            StatusText.Text = "Волна: добавлено " + added + " треков.";
                        }
                    });
                }
                catch (Exception ex)
                {
                    Post(delegate { waveBusy = false; Report(ex); });
                }
            });
        }

        void SetRowsFromShown()
        {
            rows.Clear();
            foreach (Track track in shown) rows.Add(CreateTrackRow(track));
        }

        static int AddUnique(List<Track> target, List<Track> extra)
        {
            HashSet<string> ids = new HashSet<string>();
            foreach (Track track in target) ids.Add(track.Id);
            int added = 0;
            foreach (Track track in extra)
            {
                if (ids.Add(track.Id))
                {
                    target.Add(track);
                    added++;
                }
            }
            return added;
        }

        void TrackList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DependencyObject element = e.OriginalSource as DependencyObject;
            while (element != null)
            {
                if (element is Button) return;
                ListBoxItem item = element as ListBoxItem;
                if (item != null)
                {
                    item.IsSelected = true;
                    ActivateSelected();
                    e.Handled = true;
                    return;
                }
                element = VisualTreeHelper.GetParent(element);
            }
        }

        void ActivateSelected()
        {
            TrackRow row = TrackList.SelectedItem as TrackRow;
            if (row == null) return;
            if (row.Playlist != null)
            {
                OpenPlaylist(row.Playlist);
                return;
            }
            if (row.Track == null) return;
            if (api == null && (String.IsNullOrEmpty(row.Track.LocalPath) || !File.Exists(row.Track.LocalPath))) return;

            queue.Clear();
            queue.AddRange(shown);
            waveQueue = currentView == "wave";
            queueFromSearch = currentView == "search";
            searchQueueText = queueFromSearch ? searchText : "";
            searchQueuePage = page;
            searchQueueLoading = false;
            int position = queue.FindIndex(delegate(Track t) { return t.Id == row.Track.Id; });
            Play(position);
        }

        TrackRow CreateTrackRow(Track track)
        {
            return new TrackRow(track, likedIds.Contains(track.Id), store != null && store.IsDownloaded(track), downloadingIds.Contains(track.Id));
        }

        void UpdateTrackRows(Track track)
        {
            foreach (TrackRow row in rows)
                if (row.Track != null && row.Track.Id == track.Id)
                    row.SetState(likedIds.Contains(track.Id), store != null && store.IsDownloaded(track), downloadingIds.Contains(track.Id));
        }

        void LikeTrack(Track track)
        {
            if (NeedAccount() || track == null) return;
            bool wasLiked = likedIds.Contains(track.Id);
            RunJob(delegate
            {
                if (wasLiked) api.Unlike(track); else api.Like(track);
            }, delegate
            {
                if (wasLiked) likedIds.Remove(track.Id);
                else if (!likedIds.Contains(track.Id)) likedIds.Insert(0, track.Id);
                if (currentView == "likes" && wasLiked)
                {
                    shown.RemoveAll(delegate(Track t) { return t.Id == track.Id; });
                    SetRowsFromShown();
                    page = Math.Max(0, page - 1);
                    MoreButton.IsEnabled = page < likedIds.Count;
                    StatusText.Text = "Убрано из избранного: " + track.Title;
                }
                else
                {
                    UpdateTrackRows(track);
                    StatusText.Text = wasLiked ? "Убрано из избранного: " + track.Title : "Добавлено в избранное: " + track.Title;
                }
            });
        }

        void LikeSelected()
        {
            TrackRow row = TrackList.SelectedItem as TrackRow;
            if (row != null && row.Track != null) LikeTrack(row.Track);
        }

        void LikeTrack_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            TrackRow row = (sender as FrameworkElement).Tag as TrackRow;
            if (row != null) LikeTrack(row.Track);
        }

        void DownloadSelected()
        {
            TrackRow row = TrackList.SelectedItem as TrackRow;
            if (row != null && row.Track != null) DownloadTrack(row.Track);
        }

        void DownloadTrack_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            TrackRow row = (sender as FrameworkElement).Tag as TrackRow;
            if (row != null) DownloadTrack(row.Track);
        }

        void DownloadTrack(Track track)
        {
            if (NeedAccount() || track == null) return;
            string target = store.DownloadPath(track);
            if (File.Exists(target))
            {
                try { Process.Start("explorer.exe", "/select,\"" + target + "\""); }
                catch (Exception ex) { StatusText.Text = "Файл скачан: " + target + " · " + ex.Message; }
                return;
            }
            if (downloadingIds.Contains(track.Id)) return;
            downloadingIds.Add(track.Id);
            UpdateTrackRows(track);
            StatusText.Text = "Скачиваю: " + track.Title;
            Api client = api;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception failure = null;
                try
                {
                    string url = client.Audio(track);
                    if (closing || api != client) throw new OperationCanceledException();
                    net.Download(url, target, delegate { return closing || api != client; });
                    store.SaveDownloadInfo(track, target);
                }
                catch (Exception ex) { failure = ex; }
                Post(delegate
                {
                    downloadingIds.Remove(track.Id);
                    UpdateTrackRows(track);
                    if (failure != null) Report(failure);
                    else
                    {
                        StatusText.Text = "Скачано: " + track.Title + " · " + target;
                        if (currentView == "downloads") OpenDownloaded();
                    }
                });
            });
        }

        void AddSelectedToPlaylist()
        {
            TrackRow row = TrackList.SelectedItem as TrackRow;
            if (row != null && row.Track != null) AddTrackToPlaylist(row.Track);
        }

        void AddTrackToPlaylist(Track track)
        {
            if (NeedAccount()) return;
            if (playlists.Count == 0)
            {
                List<Playlist> loaded = null;
                RunJob(delegate { loaded = api.Playlists(); }, delegate
                {
                    playlists.Clear(); playlists.AddRange(loaded);
                    ChoosePlaylistAndAdd(track);
                });
            }
            else ChoosePlaylistAndAdd(track);
        }

        void ChoosePlaylistAndAdd(Track track)
        {
            List<Playlist> owned = new List<Playlist>();
            foreach (Playlist playlist in playlists) if (playlist.IsOwned) owned.Add(playlist);
            if (owned.Count == 0) { StatusText.Text = "Нет собственных плейлистов. Создай плейлист в разделе «Плейлисты»."; return; }
            Playlist selected = PickPlaylist(owned);
            if (selected == null) return;
            RunJob(delegate { api.AddTrackToPlaylist(selected, track); }, delegate
            {
                StatusText.Text = "Добавлено в плейлист «" + selected.Title + "»: " + track.Title;
                if (currentView == "playlist" && currentPlaylist != null && currentPlaylist.Kind == selected.Kind && currentPlaylist.Owner == selected.Owner) OpenPlaylist(selected);
            });
        }

        void RemoveSelectedFromPlaylist()
        {
            if (NeedAccount() || currentPlaylist == null || !currentPlaylist.IsOwned) return;
            int position = TrackList.SelectedIndex;
            TrackRow row = TrackList.SelectedItem as TrackRow;
            if (row == null || row.Track == null || position < 0) return;
            Playlist playlist = currentPlaylist;
            Track track = row.Track;
            if (MessageBox.Show(this, "Убрать «" + track.Title + "» из плейлиста «" + playlist.Title + "»?", "OldYM", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            RunJob(delegate { api.RemoveTrackFromPlaylist(playlist, position); }, delegate { StatusText.Text = "Трек удалён из плейлиста."; OpenPlaylist(playlist); });
        }

        void CreatePlaylist()
        {
            if (NeedAccount()) return;
            string title = AskText("Новый плейлист", "Название плейлиста", "");
            if (String.IsNullOrWhiteSpace(title)) return;
            List<Playlist> updated = null;
            RunJob(delegate { api.CreatePlaylist(title); updated = api.Playlists(); }, delegate
            {
                DisplayPlaylists(updated);
                StatusText.Text = "Плейлист создан: " + title.Trim();
            });
        }

        void RenameSelectedPlaylist()
        {
            TrackRow row = TrackList.SelectedItem as TrackRow;
            if (NeedAccount() || row == null || row.Playlist == null || !row.Playlist.IsOwned) return;
            Playlist playlist = row.Playlist;
            string title = AskText("Переименовать плейлист", "Новое название", playlist.Title);
            if (String.IsNullOrWhiteSpace(title)) return;
            List<Playlist> updated = null;
            RunJob(delegate { api.RenamePlaylist(playlist, title); updated = api.Playlists(); }, delegate
            {
                DisplayPlaylists(updated);
                StatusText.Text = "Плейлист переименован.";
            });
        }

        void DeleteSelectedPlaylist()
        {
            TrackRow row = TrackList.SelectedItem as TrackRow;
            if (NeedAccount() || row == null || row.Playlist == null || !row.Playlist.IsOwned) return;
            Playlist playlist = row.Playlist;
            if (MessageBox.Show(this, "Удалить плейлист «" + playlist.Title + "»? Это действие нельзя отменить.", "OldYM", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            List<Playlist> updated = null;
            RunJob(delegate { api.DeletePlaylist(playlist); updated = api.Playlists(); }, delegate
            {
                DisplayPlaylists(updated);
                StatusText.Text = "Плейлист удалён.";
            });
        }

        void DisplayPlaylists(List<Playlist> result)
        {
            currentView = "playlists";
            currentPlaylist = null;
            playlists.Clear();
            playlists.AddRange(result);
            shown.Clear();
            rows.Clear();
            foreach (Playlist playlist in playlists) rows.Add(new TrackRow(playlist));
            MoreButton.Content = "Создать плейлист";
            MoreButton.IsEnabled = true;
            Heading.Text = "Плейлисты";
        }

        string AskText(string title, string prompt, string initial)
        {
            Window dialog = new Window { Title = title, Width = 360, Height = 150, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Owner = this };
            Grid grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            TextBlock label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 7) };
            TextBox input = new TextBox { Text = initial ?? "", MinWidth = 300, Padding = new Thickness(6) };
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Button ok = new Button { Content = "Сохранить", MinWidth = 84, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            Button cancel = new Button { Content = "Отмена", MinWidth = 84, IsCancel = true };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            Grid.SetRow(input, 1); Grid.SetRow(buttons, 2);
            grid.Children.Add(label); grid.Children.Add(input); grid.Children.Add(buttons);
            dialog.Content = grid;
            ok.Click += delegate { dialog.DialogResult = true; };
            dialog.Loaded += delegate { input.Focus(); input.SelectAll(); };
            return dialog.ShowDialog() == true ? input.Text.Trim() : null;
        }

        Playlist PickPlaylist(List<Playlist> choices)
        {
            Window dialog = new Window { Title = "Добавить в плейлист", Width = 360, Height = 150, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Owner = this };
            Grid grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ComboBox combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Title", MinWidth = 300, Margin = new Thickness(0, 8, 0, 0) };
            if (choices.Count > 0) combo.SelectedIndex = 0;
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            Button ok = new Button { Content = "Добавить", MinWidth = 84, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            Button cancel = new Button { Content = "Отмена", MinWidth = 84, IsCancel = true };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            grid.Children.Add(new TextBlock { Text = "Выбери свой плейлист:" });
            Grid.SetRow(combo, 1); grid.Children.Add(combo);
            Grid.SetRow(buttons, 2); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.Children.Add(buttons);
            dialog.Content = grid;
            ok.Click += delegate { if (combo.SelectedItem != null) dialog.DialogResult = true; };
            return dialog.ShowDialog() == true ? combo.SelectedItem as Playlist : null;
        }

        void Previous()
        {
            try
            {
                if (index >= 0 && audio.Mode != "closed" && audio.Position > 3000)
                {
                    audio.Seek(0);
                    return;
                }
            }
            catch { }
            Play(index - 1);
        }

        void TogglePlayback()
        {
            try
            {
                string mode = audio.Mode;
                if (mode == "closed")
                {
                    if (index >= 0 && index < queue.Count) Play(index);
                    else ActivateSelected();
                    return;
                }
                if (mode == "stopped")
                {
                    if (index + 1 < queue.Count) Play(index + 1);
                    else if (waveQueue) AppendWave(true);
                    else if (queueFromSearch) AppendSearchQueue();
                    else Play(index);
                    return;
                }
                audio.Toggle();
                UpdatePlayButton();
            }
            catch (Exception ex) { Report(ex); }
        }

        void Play(int at)
        {
            if (at < 0) return;
            if (at >= queue.Count)
            {
                if (waveQueue)
                {
                    AppendWave(true);
                    StatusText.Text = "Подгружаю продолжение волны…";
                }
                else if (queueFromSearch)
                {
                    AppendSearchQueue();
                    StatusText.Text = "Подгружаю следующие результаты поиска…";
                }
                else StatusText.Text = "Конец очереди.";
                return;
            }

            index = at;
            Track track = queue[at];
            int version = Interlocked.Increment(ref playVersion);
            try { audio.Stop(); }
            catch (Exception ex) { Report(ex); }
            Seek.IsEnabled = false;
            PlayerTitle.Text = track.Title;
            PlayerArtist.Text = track.Artist;
            TimeText.Text = "0:00 / " + track.Time;
            UpdatePlayButton();
            bool localDownload = !String.IsNullOrEmpty(track.LocalPath) && File.Exists(track.LocalPath);
            if (!localDownload || !String.IsNullOrEmpty(track.Cover)) LoadCover(track, version);
            else
            {
                CoverImage.Source = null;
                CoverImage.Visibility = Visibility.Collapsed;
                CoverGlyph.Visibility = Visibility.Visible;
            }
            StatusText.Text = localDownload ? "Открываю скачанный MP3…" : "Загружаю MP3 в дисковый кэш…";
            Api client = api;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string path = localDownload ? track.LocalPath : store.AudioPath(track);
                    if (!localDownload)
                    {
                        if (client == null) throw new IOException("Для потокового воспроизведения войди в аккаунт. Локальный файл недоступен.");
                        if (!File.Exists(path))
                            net.Download(client.Audio(track), path, delegate { return closing || version != playVersion; });
                        if (closing || version != playVersion) return;
                        store.Trim(path);
                    }
                    if (closing || version != playVersion) return;
                    Post(delegate
                    {
                        if (version != playVersion) return;
                        try
                        {
                            audio.Open(path);
                            Seek.IsEnabled = true;
                            UpdatePlayButton();
                            StatusText.Text = "";
                            if (waveQueue && queue.Count - index <= 3) AppendWave(true);
                            else if (queueFromSearch && queue.Count - index <= 3) AppendSearchQueue();
                        }
                        catch (Exception ex) { Report(ex); }
                    });
                }
                catch (Exception ex)
                {
                    Post(delegate { if (version == playVersion) Report(ex); });
                }
            });
        }

        void AppendSearchQueue()
        {
            if (searchQueueLoading || !queueFromSearch || api == null || String.IsNullOrEmpty(searchQueueText)) return;
            searchQueueLoading = true;
            int generation = playVersion;
            Api client = api;
            string query = searchQueueText;
            int nextPage = searchQueuePage + 1;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<Track> extra = client.Search(query, nextPage);
                    Post(delegate
                    {
                        searchQueueLoading = false;
                        if (api != client || !queueFromSearch || searchQueueText != query) return;
                        int added = AddUnique(queue, extra);
                        if (added == 0)
                        {
                            queueFromSearch = false;
                            StatusText.Text = "Больше уникальных результатов поиска нет.";
                            return;
                        }
                        searchQueuePage = nextPage;
                        if (currentView == "search" && searchText == query)
                        {
                            AppendUniqueToShown(extra);
                            page = Math.Max(page, nextPage);
                            MoreButton.IsEnabled = extra.Count > 0;
                        }
                        StatusText.Text = "В очередь добавлено: " + added + " треков.";
                        if (generation == playVersion && audio.Mode == "stopped") Play(index + 1);
                    });
                }
                catch (Exception ex)
                {
                    Post(delegate
                    {
                        searchQueueLoading = false;
                        Report(ex);
                    });
                }
            });
        }

        void LoadCover(Track track, int version)
        {
            CoverImage.Source = null;
            CoverImage.Visibility = Visibility.Collapsed;
            CoverGlyph.Visibility = Visibility.Visible;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Track full = track;
                    if (String.IsNullOrEmpty(full.Cover) && api != null)
                    {
                        List<Track> details = api.Details(new List<string> { track.Id }, 0, 1);
                        if (details.Count > 0) full = details[0];
                    }
                    string url = full.CoverUrl(200);
                    if (String.IsNullOrEmpty(url)) return;
                    byte[] bytes = net.Bytes(url, null, null, 2 * 1024 * 1024);
                    BitmapImage image = new BitmapImage();
                    using (MemoryStream stream = new MemoryStream(bytes))
                    {
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = stream;
                        image.DecodePixelWidth = 160;
                        image.EndInit();
                        image.Freeze();
                    }
                    Post(delegate
                    {
                        if (version != playVersion) return;
                        CoverImage.Source = image;
                        CoverGlyph.Visibility = Visibility.Collapsed;
                        CoverImage.Visibility = Visibility.Visible;
                    });
                }
                catch (Exception ex)
                {
                    Post(delegate
                    {
                        if (version == playVersion) StatusText.Text = "Обложка: " + ex.Message;
                    });
                }
            });
        }

        void StopPlayback()
        {
            Interlocked.Increment(ref playVersion);
            waveQueue = false;
            queueFromSearch = false;
            searchQueueLoading = false;
            try { audio.Stop(); } catch (Exception ex) { Report(ex); }
            Seek.IsEnabled = false;
            Seek.Value = 0;
            TimeText.Text = "0:00 / 0:00";
            UpdatePlayButton();
        }

        void TickPlayer()
        {
            try
            {
                string mode = audio.Mode;
                if (mode == "closed") return;
                int length = audio.Length;
                int position = audio.Position;
                TimeText.Text = FormatTime(position) + " / " + FormatTime(length);
                if (!seeking && length > 0)
                    Seek.Value = Math.Min(1000, (int)(position * 1000L / length));
                if (mode == "stopped" && length > 0 && position >= length - 500)
                {
                    if (index + 1 < queue.Count) Play(index + 1);
                    else if (waveQueue) AppendWave(true);
                    else if (queueFromSearch) AppendSearchQueue();
                    else StopPlayback();
                }
                UpdatePlayButton();
            }
            catch (Exception ex) { Report(ex); }
        }

        void Seek_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            seeking = true;
        }

        void Seek_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            seeking = false;
            try
            {
                int length = audio.Length;
                if (length > 0) audio.Seek((int)(length * (Seek.Value / 1000.0)));
            }
            catch (Exception ex) { Report(ex); }
        }

        void UpdatePlayButton()
        {
            try
            {
                object key = "GeomPlay";
                try { if (audio.Mode == "playing") key = "GeomPause"; } catch { }
                object data = FindResource(key);
                if (PlayIcon != null && data is System.Windows.Media.Geometry)
                    PlayIcon.Data = (System.Windows.Media.Geometry)data;
            }
            catch { }
        }

        static string FormatTime(int milliseconds)
        {
            return String.Format("{0}:{1:00}", milliseconds / 60000, (milliseconds / 1000) % 60);
        }
    }
}
