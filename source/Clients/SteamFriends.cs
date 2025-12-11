using CommonPluginsStores.Steam;
using CommonPluginsStores.Steam.Models.SteamKit;
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