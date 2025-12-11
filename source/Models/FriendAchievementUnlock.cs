using System;

namespace PlayerActivities.Models
{
    /// <summary>
    /// Represents a single achievement unlock event for a friend.
    /// Contains the achievement ID, name, and the timestamp when it was unlocked.
    /// </summary>
    public class FriendAchievementUnlock
    {
        /// <summary>
        /// The API name/ID of the achievement.
        /// </summary>
        public string AchievementId { get; set; }

        /// <summary>
        /// The display name of the achievement.
        /// </summary>
        public string AchievementName { get; set; }

        /// <summary>
        /// The date and time when the achievement was unlocked.
        /// </summary>
        public DateTime UnlockTime { get; set; }
    }
}
