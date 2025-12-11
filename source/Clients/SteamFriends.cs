using CommonPluginsStores.Steam;
using CommonPluginsStores.Steam.Models.SteamKit;
using CommonPluginsStores.Models;
using PlayerActivities.Models;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayerActivities.Clients
{
    /// <summary>
    /// Client for retrieving Steam friends and their game statistics.
    /// </summary>
    public class SteamFriends : GenericFriends
    {
        private static PlayerActivitiesDatabase PluginDatabase => PlayerActivities.PluginDatabase;

        public SteamFriends() : base("Steam")
        {
            StoreApi = PlayerActivities.SteamApi;
        }

        /// <summary>
        /// Overrides the base BuildPlayerFriend to include Steam achievement unlock data.
        /// </summary>
        protected override PlayerFriend BuildPlayerFriend(AccountInfos account, IEnumerable<AccountGameInfos> games = null)
        {
            var playerFriend = base.BuildPlayerFriend(account, games);

            // TODO: Add setting to control this behavior (very slow with many games/friends)
            // For now, only fetch achievement data if this is enabled and we have an API key
            bool fetchAchievementData = PluginDatabase.PluginSettings.Settings.EnableSteamFriends;
            
            if (fetchAchievementData && !playerFriend.IsUser)
            {
                PopulateAchievementUnlocks(playerFriend);
            }

            return playerFriend;
        }

        /// <summary>
        /// Populates achievement unlock data for all games owned by a friend.
        /// This is a slow operation as it makes one API call per game.
        /// </summary>
        private void PopulateAchievementUnlocks(PlayerFriend friend)
        {
            if (friend.Games == null || friend.Games.Count == 0)
            {
                return;
            }

            LogManager.GetLogger().Info($"Steam - Fetching achievement unlock data for {friend.FriendPseudo} ({friend.Games.Count} games)");

            int successCount = 0;
            int failCount = 0;

            foreach (var game in friend.Games.Where(g => g.Achievements > 0))
            {
                if (PluginDatabase.FriendsDataIsCanceled)
                {
                    LogManager.GetLogger().Info("Steam - Achievement data fetch canceled");
                    break;
                }

                if (!uint.TryParse(game.Id, out uint appId))
                {
                    continue;
                }

                try
                {
                    var unlocks = GetFriendAchievementUnlocks(friend.FriendId, appId);
                    if (unlocks.Count > 0)
                    {
                        game.AchievementUnlocks = unlocks;
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    LogManager.GetLogger().Warn(ex, $"Steam - Failed to fetch achievements for game {appId}");
                    failCount++;
                }
            }

            LogManager.GetLogger().Info($"Steam - Fetched achievement data for {successCount} games, {failCount} failed for {friend.FriendPseudo}");
        }

        /// <summary>
        /// Fetches detailed achievement unlock data for a friend's game.
        /// Only called when friend achievement timeline feature is enabled.
        /// </summary>
        /// <param name="friendId">The Steam ID of the friend</param>
        /// <param name="appId">The Steam app ID of the game</param>
        /// <returns>List of achievement unlocks with timestamps, or empty list if unavailable</returns>
        protected List<FriendAchievementUnlock> GetFriendAchievementUnlocks(string friendId, uint appId)
        {
            var unlocks = new List<FriendAchievementUnlock>();
            
            try
            {
                var steamApi = StoreApi as SteamApi;
                if (steamApi == null || steamApi.CurrentAccountInfos == null || 
                    string.IsNullOrEmpty(steamApi.CurrentAccountInfos.ApiKey))
                {
                    return unlocks;
                }

                if (!ulong.TryParse(friendId, out ulong steamId))
                {
                    return unlocks;
                }

                // Fetch achievement data from Steam API
                var achievements = SteamKit.GetPlayerAchievements(
                    steamApi.CurrentAccountInfos.ApiKey, 
                    appId, 
                    steamId, 
                    "english"
                );

                if (achievements != null)
                {
                    foreach (var ach in achievements.Where(a => a.Achieved == 1))
                    {
                        unlocks.Add(new FriendAchievementUnlock
                        {
                            AchievementId = ach.ApiName,
                            AchievementName = ach.Name,
                            UnlockTime = ach.UnlockTime
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Warn(ex, $"Failed to fetch achievement unlocks for friend {friendId}, game {appId}");
            }

            return unlocks;
        }
    }
}