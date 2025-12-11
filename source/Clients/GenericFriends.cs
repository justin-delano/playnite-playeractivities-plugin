using CommonPluginsShared;
using CommonPluginsShared.Extensions;
using CommonPluginsStores.Models;
using PlayerActivities.Models;
using PlayerActivities.Services;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using static CommonPluginsShared.PlayniteTools;

namespace PlayerActivities.Clients
{
    /// <summary>
    /// Abstract base class for managing friends from different client APIs.
    /// Handles cookie storage, connection caching, and notification for authentication errors.
    /// </summary>
    public abstract class GenericFriends
    {
        #region Properties

        protected ILogger Logger => LogManager.GetLogger();

        protected static PlayerActivitiesDatabase PluginDatabase => PlayerActivities.PluginDatabase;

        /// <summary>
        /// The store API associated with the client, to be set by derived classes.
        /// </summary>
        protected CommonPluginsStores.StoreApi StoreApi { get; set; }

        protected bool IsUserLoggedIn => StoreApi?.IsUserLoggedIn ?? false;

        /// <summary>
        /// Name of the client this class represents (e.g., Steam, Epic).
        /// </summary>
        protected string ClientName { get; }

        #endregion

        /// <summary>
        /// Constructor initializes client name and cookie file path.
        /// </summary>
        /// <param name="clientName">Client identifier string.</param>
        public GenericFriends(string clientName)
        {
            ClientName = clientName ?? throw new ArgumentNullException(nameof(clientName), "Client name cannot be null.");
        }

        #region Methods

        /// <summary>
        /// Retrieves list of friends including current user with their game stats.
        /// </summary>
        /// <returns>List of PlayerFriend instances.</returns>
        public virtual List<PlayerFriend> GetFriends()
        {
            var friends = new List<PlayerFriend>();

            if (!EnsureAuthenticated())
            {
                Logger.Warn($"{ClientName} - User not authenticated");
                return friends;
            }

            try
            {
                Logger.Info($"{ClientName} - Starting to fetch friends data");
                
                var currentUser = StoreApi.CurrentAccountInfos;
                if (currentUser == null)
                {
                    Logger.Warn($"{ClientName} - CurrentAccountInfos is null, authentication may have failed");
                    ShowAuthenticationExpiredNotification();
                    return friends;
                }
                
                Logger.Info($"{ClientName} - Current user: {currentUser.Pseudo} (ID: {currentUser.UserId})");
                
                var currentGamesInfos = StoreApi.CurrentGamesInfos;
                Logger.Info($"{ClientName} - Current user has {currentGamesInfos?.Count() ?? 0} games");

                // Check if we got no games which indicates a library issue with cookie propagation
                if (currentGamesInfos == null || !currentGamesInfos.Any())
                {
                    Logger.Error($"{ClientName} - Current user has no games data. This is a known issue with the CommonPluginsStores library not properly passing Steam cookies to game data endpoints.");
                    ShowLibraryIssueNotification();
                    // Still try to add the user with empty games list
                }

                var playerFriendsUs = BuildPlayerFriend(currentUser, currentGamesInfos);
                friends.Add(playerFriendsUs);

                var currentFriendsInfos = StoreApi.CurrentFriendsInfos;
                if (currentFriendsInfos == null)
                {
                    Logger.Warn($"{ClientName} - CurrentFriendsInfos is null, no friends data available");
                    return friends;
                }
                
                Logger.Info($"{ClientName} - Found {currentFriendsInfos.Count} friends");

                PluginDatabase.FriendsDataLoading.FriendCount = currentFriendsInfos.Count;

                int failedFriends = 0;
                
                // Enumerate friends and add with stats and games
                foreach (var friend in currentFriendsInfos)
                {
                    if (PluginDatabase.FriendsDataIsCanceled)
                    {
                        Logger.Info($"{ClientName} - Friends data fetch canceled");
                        break;
                    }

                    PluginDatabase.FriendsDataLoading.FriendName = friend.Pseudo;
                    Logger.Info($"{ClientName} - Fetching data for friend: {friend.Pseudo}");

                    var playerFriend = BuildPlayerFriend(friend);
                    
                    if (playerFriend.Games.Count == 0)
                    {
                        failedFriends++;
                    }
                    
                    Logger.Info($"{ClientName} - Friend {friend.Pseudo} has {playerFriend.Games.Count} games, {playerFriend.Stats.Achievements} achievements");
                    
                    PluginDatabase.FriendsDataLoading.ActualCount++;
                    friends.Add(playerFriend);
                }
                
                Logger.Info($"{ClientName} - Successfully fetched data for {friends.Count} friends (including current user). {failedFriends} friends have no game data due to library limitations.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"{ClientName} - Error fetching friends data");
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return friends;
        }

        /// <summary>
        /// Updates detailed information for a single PlayerFriend.
        /// </summary>
        /// <param name="pf">PlayerFriend instance to update.</param>
        /// <returns>Updated PlayerFriend.</returns>
        public virtual PlayerFriend GetFriends(PlayerFriend pf)
        {
            if (!EnsureAuthenticated())
            {
                return pf;
            }

            try
            {
                var updatedFriend = BuildPlayerFriend(pf);
                updatedFriend.LastUpdate = DateTime.Now;
                return updatedFriend;
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return pf;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Builds a <see cref="PlayerFriend"/> instance using provided account information and optional game data.
        /// </summary>
        /// <param name="account">
        /// The <see cref="AccountInfos"/> object containing identity and metadata of the user or friend.
        /// </param>
        /// <param name="games">
        /// An optional collection of <see cref="AccountGameInfos"/> representing the user's or friend's games. 
        /// If null, the method retrieves the game data using the <see cref="StoreApi"/>.
        /// </param>
        /// <returns>
        /// A fully populated <see cref="PlayerFriend"/> instance containing identity, game statistics,
        /// game list, and metadata.
        /// </returns>
        protected virtual PlayerFriend BuildPlayerFriend(AccountInfos account, IEnumerable<AccountGameInfos> games = null)
        {
            try
            {
                if (games == null)
                {
                    games = StoreApi.GetAccountGamesInfos(account);
                    
                    if (games == null || !games.Any())
                    {
                        Logger.Warn($"{ClientName} - No games data available for {account.Pseudo}. Profile may be private or have restricted game details visibility.");
                    }
                }

                return new PlayerFriend
                {
                    ClientName = ClientName,
                    FriendId = account.UserId,
                    ClientId = account.ClientId,
                    FriendPseudo = account.Pseudo,
                    FriendsAvatar = account.Avatar,
                    FriendsLink = account.Link,
                    IsUser = account.IsCurrent,
                    AcceptedAt = account.DateAdded,
                    Stats = new PlayerStats
                    {
                        GamesOwned = games?.Count() ?? 0,
                        Achievements = games?.Sum(x => x.AchievementsUnlocked) ?? 0,
                        Playtime = games?.Sum(x => x.Playtime) ?? 0
                    },
                    Games = games?.Select(x => new PlayerGame
                    {
                        Achievements = x.AchievementsUnlocked,
                        Playtime = x.Playtime,
                        Id = x.Id,
                        IsCommun = x.IsCommun,
                        Link = x.Link,
                        Name = x.Name
                    }).ToList() ?? new List<PlayerGame>(),
                    LastUpdate = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"{ClientName} - Error building PlayerFriend for {account.Pseudo}");
                throw;
            }
        }

        /// <summary>
        /// Builds a <see cref="PlayerFriend"/> instance using provided account information and optional game data.
        /// </summary>
        /// <param name="pf">
        /// The existing <see cref="PlayerFriend"/> instance containing identity and metadata information.
        /// </param>
        /// <param name="games">
        /// An optional collection of <see cref="AccountGameInfos"/> representing the user's or friend's games. 
        /// If null, the method retrieves the game data using the <see cref="StoreApi"/>.
        /// </param>
        /// <returns>
        /// A fully populated <see cref="PlayerFriend"/> instance containing identity, game statistics,
        /// game list, and metadata.
        /// </returns>
        protected PlayerFriend BuildPlayerFriend(PlayerFriend pf, IEnumerable<AccountGameInfos> games = null)
        {
            var accountInfos = new AccountInfos
            {
                UserId = pf.FriendId,
                ClientId = pf.ClientId,
                Pseudo = pf.FriendPseudo,
                Avatar = pf.FriendsAvatar,
                Link = pf.FriendsLink,
                IsCurrent = pf.IsUser,
                DateAdded = pf.AcceptedAt
            };

            return BuildPlayerFriend(accountInfos, games);
        }

        /// <summary>
        /// Verifies whether the user is authenticated. If not, shows a notification and returns false.
        /// </summary>
        /// <returns><c>true</c> if authenticated; otherwise <c>false</c>.</returns>
        protected bool EnsureAuthenticated()
        {
            if (!IsUserLoggedIn)
            {
                ShowNotificationPluginNoAuthenticate(
                    string.Format(ResourceProvider.GetString("LOCCommonPluginNoAuthenticate"), ClientName),
                    ClientName.IsEqual("EA") ? ExternalPlugin.OriginLibrary : ExternalPlugin.PlayerActivities
                );
                return false;
            }
            return true;
        }

        #endregion

        #region Errors

        /// <summary>
        /// Shows a notification about a known library issue with Steam cookie propagation.
        /// </summary>
        private void ShowLibraryIssueNotification()
        {
            string message = $"Unable to retrieve {ClientName} game data due to a known issue with the CommonPluginsStores library.\n\n" +
                           $"The library is not properly passing Steam session cookies to game data endpoints, causing all requests to be redirected to the login page.\n\n" +
                           $"This is a limitation of the current version of the dependency library and requires an update from the plugin author.\n\n" +
                           $"Possible solutions:\n" +
                           $"• Wait for a plugin update that fixes this library issue\n" +
                           $"• Contact the plugin author about this Steam cookie propagation bug\n" +
                           $"• The plugin author could implement direct Steam Web API calls instead";

            API.Instance.Notifications.Add(new NotificationMessage(
                $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-library-issue",
                $"{PluginDatabase.PluginName}\r\n{message}",
                NotificationType.Error
            ));
        }

        /// <summary>
        /// Shows a notification about cookie/session issues with Steam.
        /// </summary>
        private void ShowCookieIssueNotification()
        {
            string message = $"{ClientName} is authenticated but unable to retrieve game data.\n\n" +
                           $"This is a known issue with Steam's web session cookies.\n\n" +
                           $"To fix:\n" +
                           $"1. Close Playnite completely\n" +
                           $"2. Open Steam in your browser and log in\n" +
                           $"3. Visit steamcommunity.com/my/games and verify you can see your games\n" +
                           $"4. Restart Playnite and try again\n\n" +
                           $"If this doesn't work, you may need to clear your browser's Steam cookies and re-authenticate.";

            API.Instance.Notifications.Add(new NotificationMessage(
                $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-cookie-issue",
                $"{PluginDatabase.PluginName}\r\n{message}",
                NotificationType.Error,
                () =>
                {
                    try
                    {
                        // Open Steam community in browser
                        System.Diagnostics.Process.Start("https://steamcommunity.com/my/games");
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }
            ));
        }

        /// <summary>
        /// Shows a notification about privacy settings or expired authentication.
        /// </summary>
        private void ShowPrivacyOrAuthNotification()
        {
            string message = $"{ClientName} friends data could not be retrieved. This can happen because:\n\n" +
                           $"1. Your friends have their game details set to PRIVATE in {ClientName}\n" +
                           $"2. Your authentication has expired\n\n" +
                           $"To fix:\n" +
                           $"• Ask friends to set their game details to PUBLIC in {ClientName} privacy settings\n" +
                           $"• Or try re-authenticating in plugin settings";

            API.Instance.Notifications.Add(new NotificationMessage(
                $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-privacy-or-auth",
                $"{PluginDatabase.PluginName}\r\n{message}",
                NotificationType.Info,
                () =>
                {
                    try
                    {
                        Plugin plugin = API.Instance.Addons.Plugins.Find(x => x.Id == PlayniteTools.GetPluginId(
                            ClientName.IsEqual("EA") ? ExternalPlugin.OriginLibrary : ExternalPlugin.PlayerActivities));
                        if (plugin != null)
                        {
                            _ = plugin.OpenSettingsView();
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }
            ));
        }

        /// <summary>
        /// Shows a notification error when authentication cookies have expired.
        /// Offers to open the plugin settings for re-authentication.
        /// </summary>
        private void ShowAuthenticationExpiredNotification()
        {
            API.Instance.Notifications.Add(new NotificationMessage(
                $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-expired-auth",
                $"{PluginDatabase.PluginName}\r\n{ClientName} authentication has expired. Please re-authenticate in the plugin settings to fetch friends data.",
                NotificationType.Error,
                () =>
                {
                    try
                    {
                        Plugin plugin = API.Instance.Addons.Plugins.Find(x => x.Id == PlayniteTools.GetPluginId(
                            ClientName.IsEqual("EA") ? ExternalPlugin.OriginLibrary : ExternalPlugin.PlayerActivities));
                        if (plugin != null)
                        {
                            _ = plugin.OpenSettingsView();
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }
            ));
        }

        /// <summary>
        /// Shows a notification error when plugin authentication is missing or failed.
        /// Offers to open the plugin settings for re-authentication.
        /// </summary>
        /// <param name="message">Error message to display.</param>
        /// <param name="pluginSource">Source plugin reference.</param>
        private void ShowNotificationPluginNoAuthenticate(string message, ExternalPlugin pluginSource)
        {
            API.Instance.Notifications.Add(new NotificationMessage(
                $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-noauthenticate",
                $"{PluginDatabase.PluginName}\r\n{message}",
                NotificationType.Error,
                () =>
                {
                    try
                    {
                        Plugin plugin = API.Instance.Addons.Plugins.Find(x => x.Id == PlayniteTools.GetPluginId(pluginSource));
                        if (plugin != null)
                        {
                            StoreApi.ResetIsUserLoggedIn();
                            _ = plugin.OpenSettingsView();
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }
            ));
        }

        #endregion
    }
}