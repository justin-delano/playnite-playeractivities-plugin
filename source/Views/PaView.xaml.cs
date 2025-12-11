using CommonPlayniteShared.Commands;
using CommonPluginsShared;
using CommonPluginsShared.Controls;
using CommonPluginsShared.Extensions;
using PlayerActivities.Controls;
using PlayerActivities.Models;
using PlayerActivities.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Input;

namespace PlayerActivities.Views
{
    /// <summary>
    /// Logique d'interaction pour PaView.xaml
    /// </summary>
    public partial class PaView : UserControl
    {
        private static ILogger Logger => LogManager.GetLogger();

        private PlayerActivities Plugin { get; }
        private PlayerActivitiesDatabase PluginDatabase => PlayerActivities.PluginDatabase;

        internal static PaViewData ControlDataContext { get; set; } = new PaViewData();

        private List<string> SearchSources { get; set; } = new List<string>();

        private bool TimeLineFilter(object item)
        {
            if (item == null)
            {
                return false;
            }

            ActivityListGrouped el = item as ActivityListGrouped;

            bool txtFilter = el.GameContext.Name.Contains(TextboxSearch.Text, StringComparison.InvariantCultureIgnoreCase);

            bool sourceFilter = true;
            if (SearchSources.Count > 0)
            {
                sourceFilter = SearchSources.Where(x => PlayniteTools.GetSourceName(el.GameContext).IsEqual(x)).Count() > 0;
            }

            return txtFilter && sourceFilter;
        }

        private bool IsDataFinished = false;
        private bool IsFriendsFinished = false;

        public PaView(PlayerActivities plugin)
        {
            Plugin = plugin;

            InitializeComponent();
            DataContext = ControlDataContext;

            PART_DataLoad.Visibility = Visibility.Visible;
            PART_Data.Visibility = Visibility.Hidden;
            PART_DataRerefsh.Visibility = Visibility.Collapsed;

            // Initialize date filter options
            InitializeDateFilterOptions();

            GetData();
            GetFriends();


            PluginDatabase.Database.ItemUpdated += Database_ItemUpdated;

            PluginDatabase.Database.Select(x => PlayniteTools.GetSourceName(x.Game)).Distinct().ForEach(x =>
            {
                string icon = TransformIcon.Get(x) + " ";
                ControlDataContext.FilterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + x, SourceNameShort = x, IsCheck = false });
            });


            if (!PluginDatabase.PluginSettings.Settings.EnableEpicFriends && !PluginDatabase.PluginSettings.Settings.EnableGogFriends && !PluginDatabase.PluginSettings.Settings.EnableOriginFriends && !PluginDatabase.PluginSettings.Settings.EnableSteamFriends)
            {
                PART_BtFriends.IsEnabled = false;
            }
        }

        private void InitializeDateFilterOptions()
        {
            ControlDataContext.DateFilterOptions.Add(new DateFilterOption { Name = ResourceProvider.GetString("LOCPaDateFilterLast7Days"), Days = 7, IsCustom = false });
            ControlDataContext.DateFilterOptions.Add(new DateFilterOption { Name = ResourceProvider.GetString("LOCPaDateFilterLast30Days"), Days = 30, IsCustom = false });
            ControlDataContext.DateFilterOptions.Add(new DateFilterOption { Name = ResourceProvider.GetString("LOCPaDateFilterLast90Days"), Days = 90, IsCustom = false });
            ControlDataContext.DateFilterOptions.Add(new DateFilterOption { Name = ResourceProvider.GetString("LOCPaDateFilterAllTime"), Days = null, IsCustom = false });

            // Set default filter based on settings (default to 7 days)
            int defaultDays = PluginDatabase.PluginSettings.Settings.DateFilterDays;
            ControlDataContext.SelectedDateFilter = ControlDataContext.DateFilterOptions.FirstOrDefault(x => x.Days == defaultDays) 
                ?? ControlDataContext.DateFilterOptions.FirstOrDefault(x => x.Days == 7);
        }

        private void Database_ItemUpdated(object sender, ItemUpdatedEventArgs<PlayerActivitiesData> e)
        {
            // Invalidate cache when data is updated
            PluginDatabase.InvalidateCache();
            GetData();
        }

        #region Data

        private void GetData()
        {
            _ = Task.Run(() =>
            {
                DateTime? startDate = null;
                DateTime? endDate = null;

                if (PluginDatabase.PluginSettings.Settings.EnableDateFilter && ControlDataContext.SelectedDateFilter != null)
                {
                    startDate = ControlDataContext.SelectedDateFilter.GetStartDate();
                    endDate = ControlDataContext.SelectedDateFilter.GetEndDate();
                }

                ControlDataContext.ItemsSource = PluginDatabase.GetActivitiesData(grouped: true, startDate: startDate, endDate: endDate);

                IsDataFinished = true;
                IsFinish();
            });
        }

        private void GetFriends()
        {
            _ = Task.Run(() =>
            {
                _ = SpinWait.SpinUntil(() => PluginDatabase.FriendsDataIsDownloaded, -1);
                FriendsData friendsData = PluginDatabase.GetFriends(Plugin);
                ControlDataContext.FriendsSource = friendsData.PlayerFriends.ToObservable();
                ControlDataContext.LastFriendsRefresh = friendsData.PlayerFriends.Count == 0 ? (DateTime?)null : friendsData.LastUpdate;
                IsFriendsFinished = true;
                IsFinish();
            });
        }

        private void IsFinish()
        {
            if (IsDataFinished && IsFriendsFinished)
            {
                _ = Dispatcher?.BeginInvoke(DispatcherPriority.Loaded, new ThreadStart(delegate
                {
                    PART_DataLoad.Visibility = Visibility.Hidden;
                    PART_Data.Visibility = Visibility.Visible;
                }));

                _ = Task.Run(() =>
                {
                    Thread.Sleep(3000);

                    _ = (Dispatcher?.BeginInvoke(DispatcherPriority.Loaded, new ThreadStart(delegate
                    {
                        CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(PART_LbTimeLine.ItemsSource);
                        view.Filter = TimeLineFilter;
                    })));
                });
            }
        }

        #endregion

        #region Filter

        private void TextboxSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(PART_LbTimeLine.ItemsSource).Refresh();
        }

        private void ChkSource_Checked(object sender, RoutedEventArgs e)
        {
            FilterCbSource(sender as CheckBox);
        }

        private void ChkSource_Unchecked(object sender, RoutedEventArgs e)
        {
            FilterCbSource(sender as CheckBox);
        }

        private void FilterCbSource(CheckBox sender)
        {
            FilterSource.Text = string.Empty;

            if ((bool)sender.IsChecked)
            {
                SearchSources.Add((string)sender.Tag);
            }
            else
            {
                SearchSources.Remove((string)sender.Tag);
            }

            if (SearchSources.Count != 0)
            {
                FilterSource.Text = string.Join(", ", SearchSources);
            }

            CollectionViewSource.GetDefaultView(PART_LbTimeLine.ItemsSource).Refresh();

        }

        private void CbDateFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ControlDataContext.SelectedDateFilter == null)
            {
                return;
            }

            // Reload data with new date filter
            GetData();
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Pass mouse wheel events to parent to prevent scroll getting stuck
            if (sender is ScrollViewer scrollViewer)
            {
                // Get the parent ScrollViewer (the main ListBox's scroll viewer)
                var parentScrollViewer = FindParent<ScrollViewer>(scrollViewer);
                if (parentScrollViewer != null)
                {
                    // Only forward if we're at the boundary
                    bool atTop = scrollViewer.VerticalOffset == 0 && e.Delta > 0;
                    bool atBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight && e.Delta < 0;
                    
                    if (atTop || atBottom || scrollViewer.ScrollableHeight == 0)
                    {
                        e.Handled = true;
                        var routedEvent = new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                        {
                            RoutedEvent = UIElement.MouseWheelEvent,
                            Source = sender
                        };
                        parentScrollViewer.RaiseEvent(routedEvent);
                    }
                }
            }
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;

            T parent = parentObject as T;
            return parent ?? FindParent<T>(parentObject);
        }

        #endregion

        private void Button_RefreshFriendsData(object sender, RoutedEventArgs e)
        {
            PART_BtFriends.IsEnabled = false;
            PART_DataRerefsh.Visibility = Visibility.Visible;
            ControlDataContext.FriendsDataLoading = PluginDatabase.FriendsDataLoading;

            _ = Task.Run(() => {
                PluginDatabase.RefreshFriends(Plugin);
                GetFriends();

                _ = (Dispatcher?.BeginInvoke(DispatcherPriority.Loaded, new ThreadStart(delegate
                {
                    PART_BtFriends.IsEnabled = true;
                    PART_DataRerefsh.Visibility = Visibility.Collapsed;
                })));
            });
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                if (button.Tag is PlayerFriend item)
                {
                    button.IsEnabled = false;
                    PART_BtFriends.IsEnabled = false;
                    _ = Task.Run(() => {
                        PluginDatabase.RefreshFriends(Plugin, item.ClientName, item);
                        GetFriends();

                        _ = (Dispatcher?.BeginInvoke(DispatcherPriority.Loaded, new ThreadStart(delegate
                        {
                            button.IsEnabled = true;
                            PART_BtFriends.IsEnabled = true;
                        })));
                    });
                }
            }
        }

        private void ListViewExtend_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender == null)
            {
                return;
            }

            ListViewExtend lv = sender as ListViewExtend;
            if (!(lv.SelectedItem is PlayerFriend pf))
            {
                return;
            }

            PlayerFriend pf_us = ControlDataContext.FriendsSource.Where(x => x.IsUser && x.ClientName.IsEqual(pf.ClientName))?.First() ?? null;

            ObservableCollection<ListFriendsInfo> listFriendsInfos = new ObservableCollection<ListFriendsInfo>();
            pf.Games.ForEach(x =>
            {
                listFriendsInfos.Add(new ListFriendsInfo
                {
                    Id = x.Id,
                    Name = x.Name,
                    Achievements = x.Achievements,
                    IsCommun = pf.IsUser ? true : x.IsCommun,
                    Link = x.Link,
                    Playtime = x.Playtime,
                    UsAchievements = pf_us?.Games?.Find(y => x.Id == y.Id)?.Achievements ?? 0,
                    UsPlaytime = pf_us?.Games?.Find(y => x.Id == y.Id)?.Playtime ?? 0
                });
            });

            ControlDataContext.FriendsDetailsSource = listFriendsInfos;
            PART_LvFriendsDetails.Sorting();
        }
    }


    public class PaViewData : ObservableObject
    {
        private static PlayerActivitiesDatabase PluginDatabase => PlayerActivities.PluginDatabase;

        private ObservableCollection<ActivityListGrouped> _itemsSource = new ObservableCollection<ActivityListGrouped>();
        public ObservableCollection<ActivityListGrouped> ItemsSource { get => _itemsSource; set => SetValue(ref _itemsSource, value); }

        private ObservableCollection<PlayerFriend> _friendsSource = new ObservableCollection<PlayerFriend>();
        public ObservableCollection<PlayerFriend> FriendsSource { get => _friendsSource; set => SetValue(ref _friendsSource, value); }

        private ObservableCollection<ListFriendsInfo> _friendsDetailsSource = new ObservableCollection<ListFriendsInfo>();
        public ObservableCollection<ListFriendsInfo> FriendsDetailsSource { get => _friendsDetailsSource; set => SetValue(ref _friendsDetailsSource, value); }

        private ObservableCollection<ListSource> _filterSourceItems = new ObservableCollection<ListSource>();
        public ObservableCollection<ListSource> FilterSourceItems { get => _filterSourceItems; set => SetValue(ref _filterSourceItems, value); }

        private FriendsDataLoading _friendsDataLoading = new FriendsDataLoading();
        public FriendsDataLoading FriendsDataLoading { get => _friendsDataLoading; set => SetValue(ref _friendsDataLoading, value); }

        private DateTime? _lastFriendsRefresh;
        public DateTime? LastFriendsRefresh { get => _lastFriendsRefresh; set => SetValue(ref _lastFriendsRefresh, value); }

        private ObservableCollection<DateFilterOption> _dateFilterOptions = new ObservableCollection<DateFilterOption>();
        public ObservableCollection<DateFilterOption> DateFilterOptions { get => _dateFilterOptions; set => SetValue(ref _dateFilterOptions, value); }

        private DateFilterOption _selectedDateFilter;
        public DateFilterOption SelectedDateFilter { get => _selectedDateFilter; set => SetValue(ref _selectedDateFilter, value); }


        #region Menus

        public RelayCommand<Game> StartGameCommand { get; } = new RelayCommand<Game>((game) 
            => API.Instance.StartGame(game.Id));

        public RelayCommand<Game> InstallGameCommand { get; } = new RelayCommand<Game>((game) 
            => API.Instance.InstallGame(game.Id));

        public RelayCommand<Game> ShowGameInLibraryCommand { get; } = new RelayCommand<Game>((game)
            => Commands.GoToGame.Execute(game.Id));

        public RelayCommand<Game> RefreshGameDataCommand { get; } = new RelayCommand<Game>((game) =>
        {
            if (game == null)
            {
                return;
            }

            PluginDatabase.InitializePluginData(true, game.Id);
            
            DateTime? startDate = null;
            DateTime? endDate = null;

            if (PluginDatabase.PluginSettings.Settings.EnableDateFilter && PaView.ControlDataContext.SelectedDateFilter != null)
            {
                startDate = PaView.ControlDataContext.SelectedDateFilter.GetStartDate();
                endDate = PaView.ControlDataContext.SelectedDateFilter.GetEndDate();
            }

            PaView.ControlDataContext.ItemsSource = PluginDatabase.GetActivitiesData(grouped: true, startDate: startDate, endDate: endDate);
        });

        public RelayCommand<Game> ShowGameSuccessStoryCommand { get; } = new RelayCommand<Game>((game) 
            => SuccessStoryPlugin.SuccessStoryView(game));

        public RelayCommand<Game> ShowGameHowLongToBeatCommand { get; } = new RelayCommand<Game>((game) 
            => HowLongToBeatPlugin.HowLongToBeatView(game));

        public RelayCommand<Game> ShowGameScreenshotsVisualizerCommand { get; } = new RelayCommand<Game>((game) 
            => ScreenshotsVisualizerPlugin.ScreenshotsVisualizerView(game));
        
        #endregion
    }


    public class ListSource : ObservableObject
    {
        public string SourceName { get; set; }
        public string SourceNameShort { get; set; }

        private bool isCheck;
        public bool IsCheck { get => isCheck; set => SetValue(ref isCheck, value); }
    }


    public class ListFriendsInfo : PlayerGame
    {
        public int UsAchievements { get; set; }
        public long UsPlaytime { get; set; }

        public RelayCommand<object> NavigateUrl { get; } = new RelayCommand<object>((url) => GlobalCommands.NavigateUrl(url));
    }


    public class DateFilterOption : ObservableObject
    {
        public string Name { get; set; }
        public int? Days { get; set; }
        public bool IsCustom { get; set; }

        public DateTime? GetStartDate()
        {
            if (IsCustom)
            {
                return null;
            }
            if (Days.HasValue)
            {
                return DateTime.Now.AddDays(-Days.Value).Date;
            }
            return null;
        }

        public DateTime? GetEndDate()
        {
            return DateTime.Now.Date.AddDays(1).AddSeconds(-1);
        }
    }
}